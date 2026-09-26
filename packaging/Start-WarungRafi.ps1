param([ValidateSet('cashier','maintenance')][string]$Mode='cashier')
$ErrorActionPreference='Stop'
try {
 $current=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Current.json') -Raw | ConvertFrom-Json
 if($current.release -cnotmatch '^[a-f0-9]{40}$'){throw 'Versi terpasang tidak valid. Jalankan pemasangan kembali.'}
 $exe=Join-Path $PSScriptRoot ('versions\'+$current.release+'\WarungRafi.exe')
 if(!(Test-Path -LiteralPath $exe)){throw 'Aplikasi belum lengkap. Jalankan pemasangan kembali.'}
 if($Mode -eq 'maintenance'){Start-Process -FilePath $exe -ArgumentList '--maintenance'}else{Start-Process -FilePath $exe}
} catch {Add-Type -AssemblyName PresentationFramework;[Windows.MessageBox]::Show($_.Exception.Message,'Warung Rafi') | Out-Null;exit 1}
