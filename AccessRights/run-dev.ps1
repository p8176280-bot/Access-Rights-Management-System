# เปิดระบบสำหรับพัฒนา:  powershell -ExecutionPolicy Bypass -File .\run-dev.ps1
Set-Location $PSScriptRoot
Write-Host "เปิดระบบที่ http://localhost:5100  (กด Ctrl+C เพื่อหยุด)" -ForegroundColor Cyan
dotnet run --project src\AccessRights.Web
