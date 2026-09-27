param([string]$PackagePath='artifacts/Windows-install')
# CI smoke test: requires a fresh disposable Windows account, never a cashier account.
$ErrorActionPreference='Stop'
$data=Join-Path $env:LOCALAPPDATA 'WarungRafi\warung-rafi.db'
if(Test-Path -LiteralPath $data){throw 'Run this smoke test only in a fresh CI account; a cashier database already exists.'}
$root=Join-Path ([IO.Path]::GetTempPath()) ('WarungRafi-installed-check-'+[Guid]::NewGuid().ToString('N'))
$owned=$null
function Open-Installed([string]$mode){
 & (Join-Path $root 'Start-WarungRafi.ps1') -Mode $mode
 $release=(Get-Content (Join-Path $root 'Current.json') -Raw|ConvertFrom-Json).release
 $expected=Join-Path $root ('versions\'+$release+'\WarungRafi.exe')
 $deadline=[DateTime]::UtcNow.AddSeconds(30)
 do {
  $script:owned=Get-Process WarungRafi -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $expected} | Select-Object -First 1
  if($script:owned){$script:owned.Refresh();if($script:owned.MainWindowHandle -ne 0){break}}
  Start-Sleep -Milliseconds 100
 }while([DateTime]::UtcNow -lt $deadline)
 if(!$script:owned -or $script:owned.MainWindowHandle -eq 0){throw 'Installed application did not open a window.'}
 if($mode -eq 'maintenance' -and $script:owned.MainWindowTitle -notlike '*Pengaturan*'){throw 'Recovery shortcut did not open maintenance.'}
 $null=$script:owned.Handle
 $closeDeadline=[DateTime]::UtcNow.AddSeconds(10)
 do {$null=$script:owned.CloseMainWindow();if($script:owned.WaitForExit(100)){break}}while([DateTime]::UtcNow -lt $closeDeadline)
 if(!$script:owned.HasExited){throw 'Installed application did not close cleanly.'}
 if($script:owned.ExitCode -ne 0){throw 'Installed application returned an error.'}
 $script:owned=$null
}
try {
 & (Join-Path $PackagePath 'Install-WarungRafi.ps1') -PackagePath $PackagePath -InstallRoot $root
 foreach($name in @('Warung Rafi.lnk','Pemulihan Warung Rafi.lnk')){if(!(Test-Path (Join-Path ([Environment]::GetFolderPath('Programs')) ('Warung Rafi\'+$name)))){throw 'Start menu shortcut missing.'}}
 Open-Installed 'cashier'
 if(!(Test-Path -LiteralPath $data)){throw 'Installed cashier did not initialize its database.'}
 $before=(Get-FileHash -LiteralPath $data -Algorithm SHA256).Hash
 Open-Installed 'maintenance'
 & (Join-Path $root 'Uninstall-WarungRafi.ps1') -InstallRoot $root
 if((Get-FileHash -LiteralPath $data -Algorithm SHA256).Hash -ne $before){throw 'Maintenance/uninstall changed transaction database.'}
 Write-Host '4 installed package smoke checks passed: real shortcuts, bundled cashier startup, maintenance startup, retained database after uninstall.'
} finally {
 if($owned -and !$owned.HasExited){$owned.Kill();$owned.WaitForExit()}
 if(Test-Path -LiteralPath $root){Remove-Item -LiteralPath $root -Recurse -Force}
}
