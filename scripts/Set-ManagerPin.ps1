# Open the protected settings UI in the same Windows user account as the cashier.
$ErrorActionPreference='Stop'
$localExe=Join-Path $PSScriptRoot 'WarungRafi.exe'
$launcher=Join-Path $env:LOCALAPPDATA 'Programs\WarungRafi\Start-WarungRafi.ps1'
if(Test-Path -LiteralPath $localExe){Start-Process -FilePath $localExe -ArgumentList '--maintenance'}
elseif(Test-Path -LiteralPath $launcher){& $launcher -Mode maintenance}
else{throw 'Tutup kasir lalu jalankan WarungRafi.exe --maintenance dari folder paket, atau buka Pengaturan Warung Rafi dari menu Start.'}
