$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
cargo test --workspace
if ($LASTEXITCODE -ne 0) { throw 'Rust tests failed' }
cargo build --workspace
if ($LASTEXITCODE -ne 0) { throw 'Rust build failed' }
& ./target/debug/showdevice.exe package .
if ($LASTEXITCODE -ne 0) { throw 'Packaging failed. Close running ShowDevice instances and retry.' }
& ./target/debug/showdevice.exe self-test dist/plugins
if ($LASTEXITCODE -ne 0) { throw 'Isolation tests failed' }
dotnet build apps/windows/ShowDevice.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64
if ($LASTEXITCODE -ne 0) { throw 'WinUI build failed' }

