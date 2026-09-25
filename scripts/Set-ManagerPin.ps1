# Run in the Windows user account that operates the cashier. PIN is never echoed.
$ErrorActionPreference = 'Stop'
$securePin = Read-Host 'PIN pengelola baru (6-12 angka)' -AsSecureString
$secureConfirm = Read-Host 'Ulangi PIN pengelola' -AsSecureString
$pinPointer = [IntPtr]::Zero
$confirmPointer = [IntPtr]::Zero
try {
    $pinPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePin)
    $confirmPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureConfirm)
    $managerPinValue = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pinPointer)
    $confirmValue = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($confirmPointer)
    if ($managerPinValue -cnotmatch '^[0-9]{6,12}$' -or $managerPinValue -cne $confirmValue) { throw 'PIN harus 6-12 angka dan kedua isian harus sama.' }
    $salt = New-Object byte[] 16
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($salt) } finally { $rng.Dispose() }
    $derive = [Security.Cryptography.Rfc2898DeriveBytes]::new($managerPinValue, $salt, 600000, [Security.Cryptography.HashAlgorithmName]::SHA256)
    try { $digest = $derive.GetBytes(32) } finally { $derive.Dispose() }
    $encoded = 'pbkdf2-sha256$600000$' + [Convert]::ToBase64String($salt) + '$' + [Convert]::ToBase64String($digest)
    [Environment]::SetEnvironmentVariable('WARUNG_MANAGER_PIN_HASH', $encoded, 'User')
    Write-Host 'PIN tersimpan untuk akun Windows ini. Tutup aplikasi, lalu buka kembali dari sesi Windows baru agar konfigurasi terbaca.'
} finally {
    if ($pinPointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pinPointer) }
    if ($confirmPointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($confirmPointer) }
    $managerPinValue = $null; $confirmValue = $null; $encoded = $null
    $securePin.Dispose(); $secureConfirm.Dispose()
}
