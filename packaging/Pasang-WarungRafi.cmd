@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-WarungRafi.ps1"
if errorlevel 1 echo Pemasangan belum berhasil. Baca pesan di atas; data transaksi tidak dihapus.
pause
