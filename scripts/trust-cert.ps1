# Run this script as Administrator to trust the self-signed certificate

$cert = Get-ChildItem -Path 'Cert:\CurrentUser\My' -CodeSigningCert |
    Where-Object { $_.Subject -like '*SshManager*' } |
    Select-Object -First 1

if ($null -eq $cert) {
    Write-Error "Certificate not found"
    exit 1
}

# Export cert to temp file
$tempFile = [System.IO.Path]::GetTempFileName() + ".cer"
Export-Certificate -Cert $cert -FilePath $tempFile | Out-Null

# Import to Trusted Root (requires admin)
Import-Certificate -FilePath $tempFile -CertStoreLocation 'Cert:\LocalMachine\Root'

Remove-Item $tempFile

Write-Host "Certificate added to Trusted Root store"
Write-Host "Your signed executables will now be trusted on this machine"
