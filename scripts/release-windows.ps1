param([Parameter(Mandatory)][string]$Version,[Parameter(Mandatory)][string]$Repository)
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
if($Version -notmatch '^\d{1,5}\.\d{1,5}\.\d{1,5}$' -or $Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$'){throw 'Invalid version or repository'}
if(!$env:UPDATE_PRIVATE_KEY -or !$env:UPDATE_PUBLIC_KEY){throw 'UPDATE_PRIVATE_KEY secret and UPDATE_PUBLIC_KEY variable are required'}
$rsa=[Security.Cryptography.RSA]::Create();$public=[Security.Cryptography.RSA]::Create()
$configPath=Join-Path (Get-Location) 'apps/launcher/release-config.json'
$original=[IO.File]::ReadAllBytes($configPath)
function Checked {param([scriptblock]$Command) & $Command; if($LASTEXITCODE -ne 0){throw "Build step failed: $Command"}}
try {
    $rsa.ImportFromPem($env:UPDATE_PRIVATE_KEY);$public.ImportFromPem($env:UPDATE_PUBLIC_KEY)
    if($rsa.KeySize -lt 3072 -or $rsa.ExportSubjectPublicKeyInfoPem() -ne $public.ExportSubjectPublicKeyInfoPem()){throw 'Release signing keys do not match'}
    $pinned=[Text.Encoding]::UTF8.GetString($original)|ConvertFrom-Json
    if($pinned.PublicKeyPem -and $pinned.PublicKeyPem.Trim() -ne $public.ExportSubjectPublicKeyInfoPem().Trim()){throw 'Public key differs from pinned identity. Key rotation needs an explicit trust migration.'}
    if($pinned.Repository -and $pinned.Repository -ne $Repository){throw 'Release repository differs from the pinned update source.'}
    $out=Join-Path (Get-Location) "artifacts/release-$Version"
    if(Test-Path $out){throw 'Output exists. Choose a new version or archive the previous build first.'}
    $bundle=Join-Path $out 'bundle';$slot=Join-Path $bundle "versions/$Version";New-Item -ItemType Directory -Force $slot|Out-Null
    @{Repository=$Repository;PublicKeyPem=$env:UPDATE_PUBLIC_KEY}|ConvertTo-Json|Set-Content -Encoding utf8NoBOM $configPath
    Checked {cargo build --workspace --release --locked}
    Checked {& ./target/release/showdevice.exe package .}
    Checked {& ./target/release/showdevice.exe self-test dist/plugins}
    Checked {dotnet publish apps/windows/ShowDevice.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true -o $slot}
    foreach($resource in @('ShowDevice.pri','App.xbf','ThemePalette.xbf')) {
        if(!(Test-Path (Join-Path $slot $resource))){throw "Missing published WinUI resource: $resource"}
    }
    Copy-Item dist (Join-Path $slot 'dist') -Recurse
    [IO.File]::WriteAllText((Join-Path $slot 'release-version.txt'),$Version)
    Checked {dotnet publish apps/launcher/ShowDevice.Launcher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $out 'launcher')}
    Copy-Item (Join-Path $out 'launcher/ShowDevice.Launcher.exe') $bundle
    @{Current=$Version}|ConvertTo-Json|Set-Content -Encoding utf8NoBOM (Join-Path $bundle 'install-state.json')
    'Extract this entire folder to a writable location. Start ShowDevice.Launcher.exe. Keep versions/ and the launcher together. Settings remain in LocalAppData/ShowDevice.'|Set-Content (Join-Path $bundle 'START-HERE.txt')
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive="ShowDevice-$Version-win-x64-update.zip";$archivePath=Join-Path $out $archive
    [IO.Compression.ZipFile]::CreateFromDirectory($slot,$archivePath)
    $manifest=@{Schema=1;Version=$Version;Platform='win-x64';Archive=$archive;Size=(Get-Item $archivePath).Length;Sha256=(Get-FileHash $archivePath -Algorithm SHA256).Hash}
    if($manifest.Size -gt 805306368){throw 'Update archive exceeds client size limit'}
    $bytes=[Text.Encoding]::UTF8.GetBytes(($manifest|ConvertTo-Json -Compress))
    [IO.File]::WriteAllBytes((Join-Path $out 'update.json'),$bytes)
    $signature=$rsa.SignData($bytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss)
    [IO.File]::WriteAllText((Join-Path $out 'update.sig'),[Convert]::ToBase64String($signature))
    [IO.Compression.ZipFile]::CreateFromDirectory($bundle,(Join-Path $out "ShowDevice-$Version-win-x64.zip"))
} finally {[IO.File]::WriteAllBytes($configPath,$original);$rsa.Dispose();$public.Dispose()}
