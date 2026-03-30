# Generate Secure API Key for QEP Integration
# Run this script to generate a cryptographically secure API key

# PowerShell command to generate a secure random key
$bytes = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
$apiKey = [Convert]::ToBase64String($bytes)

Write-Host "=== QEP API Key Generator ===" -ForegroundColor Green
Write-Host ""
Write-Host "Your new secure API key:" -ForegroundColor Yellow
Write-Host $apiKey -ForegroundColor Cyan
Write-Host ""
Write-Host "Instructions:" -ForegroundColor Green
Write-Host "1. Copy the API key above" -ForegroundColor White
Write-Host "2. In Azure App Service, add an Application Setting:" -ForegroundColor White
Write-Host "   Name: QepApiKey" -ForegroundColor White
Write-Host "   Value: <paste the key above>" -ForegroundColor White
Write-Host "3. NEVER commit this key to source control" -ForegroundColor Red
Write-Host "4. Share securely with QEP team via encrypted channel" -ForegroundColor White
Write-Host ""

# Alternative: Linux/macOS command (for reference)
Write-Host "=== Linux/macOS Alternative ===" -ForegroundColor Green
Write-Host "Run this command: openssl rand -base64 32" -ForegroundColor White
Write-Host ""
