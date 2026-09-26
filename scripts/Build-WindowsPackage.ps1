param([string]$Source='artifacts/Windows-preview',[string]$Destination='artifacts/Windows-install',[Parameter(Mandatory=$true)][string]$Release)
$ErrorActionPreference='Stop'
if($Release -cnotmatch '^[a-f0-9]{40}$'){throw 'Release harus SHA commit lengkap.'}
if(Test-Path -LiteralPath $Destination){throw 'Folder paket tujuan harus baru.'}
New-Item -ItemType Directory -Path $Destination | Out-Null
$payload=Join-Path $Destination 'payload';Copy-Item -LiteralPath $Source -Destination $payload -Recurse
$files=@(Get-ChildItem -LiteralPath $payload -Recurse -File | ForEach-Object {
 $relative=$_.FullName.Substring((Get-Item -LiteralPath $payload).FullName.Length+1).Replace('\','/')
 if($relative -match '(?i)(\.db($|-)|\.env|settings\.protected|\.wrbackup$)'){throw 'Data/rahasia tidak boleh masuk paket.'}
 @{path=$relative;bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
})
@{format=1;release=$Release;files=$files} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Destination 'Manifest.json') -Encoding UTF8
Copy-Item -Path 'packaging/*' -Destination $Destination
Copy-Item -LiteralPath 'docs/M6-SETUP-RECOVERY.md' -Destination $Destination
Write-Host "Paket pemasangan dibuat untuk $Release"
