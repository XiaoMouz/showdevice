param([ValidateSet('full','minimal')][string]$Flavor='full')
$ErrorActionPreference='Stop'
$suffix=if($Flavor -eq 'minimal'){'-minimal'}else{''}
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$testRoot=Join-Path ([IO.Path]::GetTempPath()) ('showdevice-update-integration-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $testRoot|Out-Null
$rsa=[Security.Cryptography.RSA]::Create(3072)
function Checked {param([scriptblock]$Command) & $Command; if($LASTEXITCODE -ne 0){throw "Failed: $Command"}}
function Assert {param($Condition,$Message) if(!$Condition){throw $Message};Write-Output "PASS $Message"}
function State($current,$previous=$null,$trial=$false){@{Current=$current;Previous=$previous;Trial=$trial}|ConvertTo-Json|Set-Content -Encoding utf8NoBOM (Join-Path $install 'install-state.json')}
function Slot($version){$dest=Join-Path $install "versions/$version";New-Item -ItemType Directory -Force $dest|Out-Null;Copy-Item "$testRoot/fixture/*" $dest -Recurse;[IO.File]::WriteAllText((Join-Path $dest 'release-version.txt'),$version);return $dest}
function Pending($version){
    $source=Join-Path $testRoot "payload-$version";New-Item -ItemType Directory -Force "$source/dist"|Out-Null
    Copy-Item "$testRoot/fixture/*" $source -Recurse;[IO.File]::WriteAllText("$source/release-version.txt",$version);[IO.File]::WriteAllText("$source/dist/showdevice.exe",'fixture-only')
    New-Item -ItemType Directory -Force "$install/updates"|Out-Null
    $zip="$install/updates/package.zip";[IO.Compression.ZipFile]::CreateFromDirectory($source,$zip)
    $m=@{Schema=1;Version=$version;Platform='win-x64';Flavor=$Flavor;Archive="ShowDevice-$version-win-x64$suffix-update.zip";Size=(Get-Item $zip).Length;Sha256=(Get-FileHash $zip).Hash}
    $bytes=[Text.Encoding]::UTF8.GetBytes(($m|ConvertTo-Json -Compress));$sig=$rsa.SignData($bytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss)
    @{Manifest=[Convert]::ToBase64String($bytes);Signature=[Convert]::ToBase64String($sig)}|ConvertTo-Json|Set-Content -Encoding utf8NoBOM "$install/updates/pending.json"
}
function Launch { $p=Start-Process "$install/ShowDevice.Launcher.exe" -PassThru -WindowStyle Hidden;if(!$p.WaitForExit(40000)){throw 'Launcher timeout'};Start-Sleep -Seconds 4 }
try{
    # Never touch the real user install or network: isolated fixture executable and ephemeral key.
    @{Repository='fixture/offline';PublicKeyPem=$rsa.ExportSubjectPublicKeyInfoPem();Flavor=$Flavor}|ConvertTo-Json|Set-Content -Encoding utf8NoBOM "$testRoot/config.json"
    Checked {dotnet build "$repo/apps/launcher/ShowDevice.Launcher.csproj" "-p:ReleaseConfigPath=$testRoot/config.json" -o "$testRoot/launcher"}
    Checked {dotnet build "$PSScriptRoot/fixture/Fixture.csproj" -o "$testRoot/fixture"}
    foreach($scenario in @('success','failure','interrupted','tampered')){
        $install=Join-Path $testRoot $scenario;New-Item -ItemType Directory $install|Out-Null;Copy-Item "$testRoot/launcher/*" $install -Recurse
        $old=Slot '1.0.0';State '1.0.0'
        if($scenario -eq 'interrupted'){$null=Slot '1.0.2';State '1.0.2' '1.0.0' $true}
        else{Pending $(if($scenario -eq 'failure'){'1.0.2'}else{'1.0.1'})}
        if($scenario -eq 'tampered'){[IO.File]::AppendAllText("$install/updates/package.zip",'tampered')}
        Launch
        $s=Get-Content "$install/install-state.json" -Raw|ConvertFrom-Json
        if($scenario -eq 'success'){Assert ($s.Current -eq '1.0.1' -and !$s.Trial) 'healthy update commits new version'}
        else{Assert ($s.Current -eq '1.0.0' -and (Test-Path "$old/started")) "$scenario retains or restores working version"}
    }
}finally{
    $rsa.Dispose()
    # Only remove this freshly generated, verified temporary workspace.
    $resolved=[IO.Path]::GetFullPath($testRoot);$temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if($resolved.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolved -Leaf).StartsWith('showdevice-update-integration-')){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
