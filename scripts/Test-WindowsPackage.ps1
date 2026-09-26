$ErrorActionPreference='Stop'
$directory=Join-Path ([IO.Path]::GetTempPath()) ('WarungRafi-package-checks-'+[Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $directory | Out-Null
$package=Join-Path $directory 'package';$install=Join-Path $directory 'installed';New-Item -ItemType Directory -Path (Join-Path $package 'payload') -Force | Out-Null
Copy-Item -Path 'packaging/*' -Destination $package
$outside=Join-Path $directory 'transactions.db';Set-Content -LiteralPath $outside -Value 'transaction-data-fixture'
function Manifest([string]$release){$file=Join-Path $package 'payload/WarungRafi.exe';@{format=1;release=$release;files=@(@{path='WarungRafi.exe';bytes=(Get-Item $file).Length;sha256=(Get-FileHash $file -Algorithm SHA256).Hash})}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $package 'Manifest.json')}
try {
 Set-Content -LiteralPath (Join-Path $package 'payload/WarungRafi.exe') -Value 'version-one-fixture';Manifest ('a'*40)
 Rename-Item -LiteralPath (Join-Path $package 'Start-WarungRafi.ps1') -NewName 'Start-WarungRafi.off';$rejected=$false
 try{& (Join-Path $package 'Install-WarungRafi.ps1') -PackagePath $package -InstallRoot $install -NoShortcuts}catch{$rejected=$true}
 if(!$rejected -or (Test-Path (Join-Path $install 'Current.json'))){throw 'Interrupted initial install activated'}
 Rename-Item -LiteralPath (Join-Path $package 'Start-WarungRafi.off') -NewName 'Start-WarungRafi.ps1'
 & (Join-Path $package 'Install-WarungRafi.ps1') -PackagePath $package -InstallRoot $install -NoShortcuts
 Set-Content -LiteralPath (Join-Path $package 'payload/WarungRafi.exe') -Value 'version-two-fixture';Manifest ('b'*40)
 & (Join-Path $package 'Install-WarungRafi.ps1') -PackagePath $package -InstallRoot $install -NoShortcuts
 if((Get-Content (Join-Path $install 'Current.json') -Raw|ConvertFrom-Json).release -ne ('b'*40)){throw 'Activation failed'}
 if((Get-Content (Join-Path $install 'Current.previous.json') -Raw|ConvertFrom-Json).release -ne ('a'*40)){throw 'Previous activation lost'}
 if(!(Test-Path (Join-Path $install ('versions/'+('a'*40)+'/WarungRafi.exe')))){throw 'Previous version lost'}
 Set-Content -LiteralPath (Join-Path $package 'payload/WarungRafi.exe') -Value 'corrupted-fixture';$rejected=$false
 try{& (Join-Path $package 'Install-WarungRafi.ps1') -PackagePath $package -InstallRoot $install -NoShortcuts}catch{$rejected=$true}
 if(!$rejected -or (Get-Content (Join-Path $install 'Current.json') -Raw|ConvertFrom-Json).release -ne ('b'*40)){throw 'Invalid update replaced installed version'}
 $bad=Get-Content (Join-Path $package 'Manifest.json') -Raw|ConvertFrom-Json;$bad.files[0].path='../transactions.db';$bad|ConvertTo-Json -Depth 5|Set-Content (Join-Path $package 'Manifest.json');$rejected=$false
 try{& (Join-Path $package 'Install-WarungRafi.ps1') -PackagePath $package -InstallRoot $install -NoShortcuts}catch{$rejected=$true};if(!$rejected){throw 'Traversal accepted'}
 & (Join-Path $package 'Uninstall-WarungRafi.ps1') -InstallRoot $install -NoShortcuts
 if((Get-Content -LiteralPath $outside -Raw).Trim() -ne 'transaction-data-fixture'){throw 'Uninstall touched data'}
 Manifest ('c'*40)
 & (Join-Path $package 'Install-WarungRafi.ps1') -PackagePath $package -InstallRoot $install -NoShortcuts
 if((Get-Content (Join-Path $install 'Current.json') -Raw|ConvertFrom-Json).release -ne ('c'*40)){throw 'Reinstall failed'}
 Write-Host '8 installer/update checks passed: interrupted install retry, initial install, activation, retained version, corrupt update, path traversal, data after uninstall, reinstall.'
} finally {Remove-Item -LiteralPath $directory -Recurse -Force}
