param([string]$Root=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$info=[System.Diagnostics.ProcessStartInfo]::new((Join-Path $Root 'target/debug/showdevice.exe'))
$info.UseShellExecute=$false
$info.RedirectStandardInput=$true
$info.RedirectStandardOutput=$true
$info.RedirectStandardError=$true
$info.CreateNoWindow=$true
$info.ArgumentList.Add('demo')
$info.ArgumentList.Add((Join-Path $Root 'dist/plugins'))
$info.Environment['SHOWDEVICE_POLL_PREFERENCES']='{"interval_seconds":5}'
$process=[System.Diagnostics.Process]::Start($info)
function Read-Snapshot {
    $pending=$process.StandardOutput.ReadLineAsync()
    if(-not $pending.Wait(12000)){throw 'Snapshot timed out'}
    if($null -eq $pending.Result){throw 'Core stopped unexpectedly'}
    return ($pending.Result | ConvertFrom-Json)
}
function Send-Preferences($value){
    $command=@{type='settings';preferences=$value}|ConvertTo-Json -Depth 8 -Compress
    $process.StandardInput.WriteLine($command)
    $process.StandardInput.Flush()
}
try{
    do {$snapshot=Read-Snapshot}while($snapshot.devices.Count -eq 0)
    $id=$snapshot.devices[0].device.id
    $start=$snapshot.devices[0].state.'battery.percent'.updated_at_ms
    $overrides=@{}; $overrides[$id]=6
    Send-Preferences @{interval_seconds=60;device_intervals=$overrides;excluded_devices=@()}
    do {$next=Read-Snapshot}while($next.devices.Count -eq 0 -or $next.devices[0].state.'battery.percent'.updated_at_ms -eq $start)
    $elapsed=$next.devices[0].state.'battery.percent'.updated_at_ms-$start
    if($elapsed -lt 5900 -or $elapsed -gt 10000){throw "Override interval incorrect: $elapsed ms"}
    Write-Output "PASS per-device interval overrides global schedule ($elapsed ms)"
    Send-Preferences @{interval_seconds=60;device_intervals=@{};excluded_devices=@($id)}
    do {$excluded=Read-Snapshot}while($excluded.devices.Count -ne 0)
    Write-Output 'PASS excluded device removed from live snapshots'
    Send-Preferences @{interval_seconds=60;device_intervals=@{};excluded_devices=@()}
    do {$restored=Read-Snapshot}while($restored.devices.Count -eq 0)
    if($restored.devices[0].device.id -ne $id){throw 'Wrong device restored'}
    Write-Output 'PASS excluded device can be enabled again without restarting core'
}finally{
    $process.StandardInput.Close()
    if(-not $process.WaitForExit(6000)){$process.Kill($true)}
    $process.Dispose()
}
