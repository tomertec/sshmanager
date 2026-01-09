$cert = Get-ChildItem -Path 'Cert:\CurrentUser\My' -CodeSigningCert |
    Where-Object { $_.Subject -like '*SshManager*' } |
    Select-Object -First 1

if ($null -eq $cert) {
    Write-Error "No code signing certificate found"
    exit 1
}

Write-Host "Using certificate: $($cert.Subject)"
Write-Host "Thumbprint: $($cert.Thumbprint)"

$result = Set-AuthenticodeSignature `
    -FilePath 'c:\Users\tomer.TEC\projects\sshmanager\publish\SshManager.App.exe' `
    -Certificate $cert `
    -TimestampServer 'http://timestamp.digicert.com' `
    -HashAlgorithm SHA256

Write-Host ""
Write-Host "Status: $($result.Status)"
Write-Host "Message: $($result.StatusMessage)"
