param([string]$Destination='artifacts/Windows-preview')
$ErrorActionPreference='Stop'
$node=(Get-Command node.exe -ErrorAction Stop).Source
$version=& $node --version
if($version -notmatch '^v24\.'){throw 'Node 24 is required for the bundled Sheets worker.'}
$folder=Join-Path $Destination 'sheets';$lib=Join-Path $folder 'lib'
New-Item -ItemType Directory -Path $lib -Force | Out-Null
Copy-Item -LiteralPath $node -Destination (Join-Path $folder 'node.exe')
Copy-Item -LiteralPath 'scripts/direct-sheets-worker.mjs' -Destination (Join-Path $folder 'worker.mjs')
foreach($name in @('direct-sheets','simple-sales','reports','reconciliation','http','sheets')){
 Copy-Item -LiteralPath ('apps/admin/lib/'+$name+'.ts') -Destination $lib
}
'{"type":"module"}' | Set-Content -LiteralPath (Join-Path $folder 'package.json') -Encoding ascii
# Use the exact packaged executable and script, without installed Node in the child PATH.
$result='{}' | & (Join-Path $folder 'node.exe') (Join-Path $folder 'worker.mjs')
if($LASTEXITCODE -ne 0 -or ($result | ConvertFrom-Json).state -ne 'retry'){throw 'Bundled Sheets worker smoke test failed.'}
Write-Host 'Bundled Sheets worker starts with its own runtime; invalid input is rejected without leaking details.'
