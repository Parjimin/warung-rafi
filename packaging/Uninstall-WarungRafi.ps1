param([string]$InstallRoot=$PSScriptRoot,[switch]$NoShortcuts)
$ErrorActionPreference='Stop'
$InstallRoot=[IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
$dataRoot=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'WarungRafi')).TrimEnd('\')
if($InstallRoot -eq $dataRoot -or $dataRoot.StartsWith($InstallRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or $InstallRoot.StartsWith($dataRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Folder aplikasi tidak boleh mencakup data transaksi.'}
$marker=Join-Path $InstallRoot '.warung-install.json'
if(!(Test-Path -LiteralPath $marker) -or (Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json).application -ne 'WarungRafi'){throw 'Folder ini bukan pemasangan Warung Rafi.'}
if((Get-Item -LiteralPath $InstallRoot).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Folder pemasangan tidak boleh berupa link.'}
$running=Get-Process WarungRafi -ErrorAction SilentlyContinue
if($running){throw 'Tutup aplikasi Warung Rafi sebelum menghapus pemasangan.'}
$lock=[IO.File]::Open((Join-Path $InstallRoot '.install-lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try {
$versions=Join-Path $InstallRoot 'versions'
if(Test-Path -LiteralPath $versions){
 if((Get-Item -LiteralPath $versions).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Folder versi tidak boleh berupa link.'}
 foreach($version in Get-ChildItem -LiteralPath $versions -Directory){
  if($version.Name -cmatch '^[a-f0-9]{40}$' -and (Test-Path -LiteralPath (Join-Path $version.FullName '.release-manifest.json'))){
   if($version.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Folder versi tidak boleh berupa link.'}
   Remove-Item -LiteralPath $version.FullName -Recurse -Force
  }
 }
}
foreach($name in @('Current.json','Current.previous.json','Start-WarungRafi.ps1','Uninstall-WarungRafi.ps1','.warung-install.json')){Remove-Item -LiteralPath (Join-Path $InstallRoot $name) -Force -ErrorAction SilentlyContinue}
if(!$NoShortcuts){
 $menu=Join-Path ([Environment]::GetFolderPath('Programs')) 'Warung Rafi'
 foreach($name in @('Warung Rafi.lnk','Pemulihan Warung Rafi.lnk','Pengaturan Warung Rafi.lnk')){Remove-Item -LiteralPath (Join-Path $menu $name) -ErrorAction SilentlyContinue}
 Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Warung Rafi.lnk') -ErrorAction SilentlyContinue
}
} finally {$lock.Dispose()}
Remove-Item -LiteralPath (Join-Path $InstallRoot '.install-lock') -Force
if((Test-Path -LiteralPath $versions) -and @(Get-ChildItem -LiteralPath $versions -Force).Count -eq 0){Remove-Item -LiteralPath $versions}
if(@(Get-ChildItem -LiteralPath $InstallRoot -Force).Count -eq 0){Remove-Item -LiteralPath $InstallRoot}
Write-Host 'Aplikasi dihapus. Database, pengaturan dan backup tetap tersimpan. Pemasangan ulang memakai data yang sama.'
