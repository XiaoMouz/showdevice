param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $Directory | Out-Null
$private=Join-Path $Directory 'update-private.pem'; $public=Join-Path $Directory 'update-public.pem'
if((Test-Path $private) -or (Test-Path $public)){throw 'Key files already exist; refusing to replace the release identity.'}
$rsa=[Security.Cryptography.RSA]::Create(4096)
try {
    [IO.File]::WriteAllText($private,$rsa.ExportPkcs8PrivateKeyPem())
    [IO.File]::WriteAllText($public,$rsa.ExportSubjectPublicKeyInfoPem())
} finally {$rsa.Dispose()}
Write-Output 'Created update key pair. Keep the private key outside the repository; add it as the UPDATE_PRIVATE_KEY GitHub Actions secret. Add the public PEM as UPDATE_PUBLIC_KEY repository variable.'
