param([string]$PackagePath=$PSScriptRoot,[string]$InstallRoot=(Join-Path $env:LOCALAPPDATA 'Programs\WarungRafi'),[switch]$NoShortcuts)
$ErrorActionPreference='Stop'
$PackagePath=[IO.Path]::GetFullPath($PackagePath)
$InstallRoot=[IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
$dataRoot=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'WarungRafi')).TrimEnd('\')
if($InstallRoot.Length -lt 10 -or $InstallRoot -eq $dataRoot -or $dataRoot.StartsWith($InstallRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or $InstallRoot.StartsWith($dataRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Folder aplikasi harus terpisah dari folder data transaksi.'}
$manifest=Get-Content -LiteralPath (Join-Path $PackagePath 'Manifest.json') -Raw | ConvertFrom-Json
if($manifest.format -ne 1 -or $manifest.release -cnotmatch '^[a-f0-9]{40}$' -or @($manifest.files).Count -lt 1){throw 'Manifest paket tidak valid.'}
$payload=Join-Path $PackagePath 'payload'
$known=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach($entry in $manifest.files){
 $relative=[string]$entry.path
 if([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative) -or $relative -match '(^|[\\/])\.\.?([\\/]|$)' -or $relative -match '[:\x00-\x1f]' -or !$known.Add($relative)){throw 'Path paket tidak valid/berulang.'}
 $source=Join-Path $payload $relative
 if(!(Test-Path -LiteralPath $source -PathType Leaf) -or (Get-Item -LiteralPath $source).Length -ne $entry.bytes -or (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ine $entry.sha256){throw ('Isi paket tidak cocok: '+$relative)}
}
if(!$known.Contains('WarungRafi.exe')){throw 'Aplikasi tidak ada dalam paket.'}
$marker=Join-Path $InstallRoot '.warung-install.json'
if((Test-Path -LiteralPath $InstallRoot) -and !(Test-Path -LiteralPath $marker) -and @(Get-ChildItem -LiteralPath $InstallRoot -Force).Count -gt 0){throw 'Folder tujuan berisi berkas lain dan bukan pemasangan Warung Rafi.'}
if(Test-Path -LiteralPath $marker){$owned=Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json;if($owned.application -ne 'WarungRafi' -or $owned.format -ne 1){throw 'Identitas pemasangan tidak valid.'}}
New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
if((Get-Item -LiteralPath $InstallRoot).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Folder pemasangan tidak boleh berupa junction/link.'}
$lock=[IO.File]::Open((Join-Path $InstallRoot '.install-lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
$stage=Join-Path $InstallRoot ('.pending-'+[Guid]::NewGuid().ToString('N'))
try {
 # Mark ownership before staging so an interrupted first install remains retryable.
 if(!(Test-Path -LiteralPath $marker)){@{format=1;application='WarungRafi'} | ConvertTo-Json | Set-Content -LiteralPath $marker -Encoding UTF8}
 New-Item -ItemType Directory -Path $stage | Out-Null
 foreach($entry in $manifest.files){$target=Join-Path $stage $entry.path;New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null;Copy-Item -LiteralPath (Join-Path $payload $entry.path) -Destination $target}
 foreach($entry in $manifest.files){if((Get-FileHash -LiteralPath (Join-Path $stage $entry.path) -Algorithm SHA256).Hash -ine $entry.sha256){throw 'Verifikasi salinan pemasangan gagal.'}}
 Copy-Item -LiteralPath (Join-Path $PackagePath 'Manifest.json') -Destination (Join-Path $stage '.release-manifest.json')
 $versions=Join-Path $InstallRoot 'versions';New-Item -ItemType Directory -Path $versions -Force | Out-Null
 if((Get-Item -LiteralPath $versions).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Folder versi tidak boleh berupa junction/link.'}
 $release=Join-Path $versions $manifest.release
 if(Test-Path -LiteralPath $release){if((Get-Item -LiteralPath $release).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Folder rilis tidak boleh berupa link.'};foreach($entry in $manifest.files){if((Get-FileHash -LiteralPath (Join-Path $release $entry.path) -Algorithm SHA256).Hash -ine $entry.sha256){throw 'Versi terpasang memiliki isi berbeda.'}}}
 else {[IO.Directory]::Move($stage,$release)}
 foreach($name in @('Start-WarungRafi.ps1','Uninstall-WarungRafi.ps1')){Copy-Item -LiteralPath (Join-Path $PackagePath $name) -Destination (Join-Path $InstallRoot $name) -Force}
 @{format=1;application='WarungRafi'} | ConvertTo-Json | Set-Content -LiteralPath $marker -Encoding UTF8
 $current=Join-Path $InstallRoot 'Current.json';$temporary=$current+'.tmp';@{release=$manifest.release} | ConvertTo-Json | Set-Content -LiteralPath $temporary -Encoding UTF8
 if(Test-Path -LiteralPath $current){[IO.File]::Replace($temporary,$current,(Join-Path $InstallRoot 'Current.previous.json'))}else{[IO.File]::Move($temporary,$current)}
 if(!$NoShortcuts){
  $shell=New-Object -ComObject WScript.Shell
  $menu=Join-Path ([Environment]::GetFolderPath('Programs')) 'Warung Rafi';New-Item -ItemType Directory -Path $menu -Force | Out-Null
  foreach($link in @(@{Name='Warung Rafi';Mode='cashier'},@{Name='Pemulihan Warung Rafi';Mode='maintenance'})){
   $shortcut=$shell.CreateShortcut((Join-Path $menu ($link.Name+'.lnk')));$shortcut.TargetPath=Join-Path $PSHOME 'powershell.exe';$shortcut.Arguments='-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "'+(Join-Path $InstallRoot 'Start-WarungRafi.ps1')+'" -Mode '+$link.Mode;$shortcut.WorkingDirectory=$InstallRoot;$shortcut.IconLocation=(Join-Path $release 'WarungRafi.exe');$shortcut.Save()
  }
  $shortcut=$shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'Warung Rafi.lnk'));$shortcut.TargetPath=Join-Path $PSHOME 'powershell.exe';$shortcut.Arguments='-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "'+(Join-Path $InstallRoot 'Start-WarungRafi.ps1')+'"';$shortcut.WorkingDirectory=$InstallRoot;$shortcut.IconLocation=Join-Path $release 'WarungRafi.exe';$shortcut.Save()
 }
 Write-Host 'Pemasangan selesai. Tutup versi lama, lalu buka Warung Rafi dari menu Start. Data transaksi tetap di folder data sebelumnya.'
} finally {if(Test-Path -LiteralPath $stage){Remove-Item -LiteralPath $stage -Recurse -Force};$lock.Dispose()}
