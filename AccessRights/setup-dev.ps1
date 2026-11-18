# =====================================================================
#  Access Rights Management System - เตรียมเครื่องสำหรับพัฒนา (PostgreSQL)
#  วิธีรัน (PowerShell แบบ Run as Administrator):
#     cd <โฟลเดอร์โปรเจกต์>
#     powershell -ExecutionPolicy Bypass -File .\setup-dev.ps1
# =====================================================================

$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot

$PgBin     = "C:\Program Files\PostgreSQL\15\bin"
$PgService = "postgresql-x64-15"
$DbName    = "access_rights"
$DbUser    = "access_app"
$DbPass    = "ChangeMe_Strong123!"   # ต้องตรงกับ appsettings.Development.json

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }
function Ok($text)   { Write-Host "    [OK] $text" -ForegroundColor Green }

# ---------------------------------------------------------------------
Step "1/5 ตรวจ .NET 10 SDK"
$hasSdk10 = $false
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $hasSdk10 = [bool]((dotnet --list-sdks) -match "^10\.")
}
if ($hasSdk10) {
    Ok ".NET 10 SDK ติดตั้งแล้ว"
} else {
    Write-Host "    ยังไม่มี .NET 10 SDK -> ติดตั้งผ่าน winget" -ForegroundColor Yellow
    winget install --id Microsoft.DotNet.SDK.10 -e --accept-source-agreements --accept-package-agreements
    $env:Path = [Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [Environment]::GetEnvironmentVariable("Path", "User")
    Ok "ติดตั้ง .NET 10 SDK เสร็จ"
}

# ---------------------------------------------------------------------
Step "2/5 ตรวจ PostgreSQL 15"
if (-not (Test-Path "$PgBin\psql.exe")) { throw "ไม่พบ PostgreSQL 15 ที่ $PgBin" }
$svc = Get-Service -Name $PgService -ErrorAction SilentlyContinue
if (-not $svc) { throw "ไม่พบ Service '$PgService' (เปิด services.msc แล้วหาชื่อ postgresql)" }
if ($svc.StartType -ne "Automatic") { Set-Service -Name $PgService -StartupType Automatic }
if ($svc.Status -ne "Running") { Start-Service -Name $PgService }
Ok "PostgreSQL กำลังทำงาน"

# ---------------------------------------------------------------------
Step "3/5 สร้าง User และ Database"
$secure = Read-Host "    ใส่รหัสผ่านของ user 'postgres' (ที่ตั้งไว้ตอนติดตั้ง PostgreSQL)" -AsSecureString
$PostgresPass = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))

function Psql([string]$user, [string]$pass, [string]$db, [string[]]$psqlArgs) {
    $ErrorActionPreference = "Continue"
    $env:PGPASSWORD = $pass
    $out = & "$PgBin\psql.exe" -U $user -h localhost -d $db -v ON_ERROR_STOP=1 @psqlArgs 2>&1
    $code = $LASTEXITCODE
    Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
    if ($code -ne 0) { throw "psql error: $out" }
    return $out
}

if ((Psql "postgres" $PostgresPass "postgres" @("-tAc", "SELECT 1 FROM pg_roles WHERE rolname = '$DbUser'")) -ne "1") {
    Psql "postgres" $PostgresPass "postgres" @("-c", "CREATE USER $DbUser WITH PASSWORD '$DbPass'") | Out-Null
    Ok "สร้าง user $DbUser"
} else { Ok "มี user $DbUser อยู่แล้ว" }

if ((Psql "postgres" $PostgresPass "postgres" @("-tAc", "SELECT 1 FROM pg_database WHERE datname = '$DbName'")) -ne "1") {
    Psql "postgres" $PostgresPass "postgres" @("-c", "CREATE DATABASE $DbName OWNER $DbUser ENCODING 'UTF8'") | Out-Null
    Ok "สร้าง database $DbName"
} else { Ok "มี database $DbName อยู่แล้ว" }

# ---------------------------------------------------------------------
Step "4/5 สร้างตารางจาก database\schema-postgresql.sql"
Psql $DbUser $DbPass $DbName @("-f", (Join-Path $ProjectRoot "database\schema-postgresql.sql")) | Out-Null
Ok "สร้างตาราง / ดัชนี / Trigger / View เรียบร้อย"

# ---------------------------------------------------------------------
Step "5/5 Build และรัน Test"
Push-Location $ProjectRoot
try {
    dotnet build AccessRights.slnx
    if ($LASTEXITCODE -ne 0) { throw "Build ไม่ผ่าน" }
    dotnet test AccessRights.slnx --no-build
    if ($LASTEXITCODE -ne 0) { throw "Test ไม่ผ่าน" }
}
finally { Pop-Location }

Write-Host "`nเสร็จเรียบร้อย! เปิดระบบด้วย .\run-dev.ps1 แล้วเข้า http://localhost:5100" -ForegroundColor Green
Write-Host "Login: admin / Admin12345  (ผู้ใช้ตัวอย่าง: approver1, approver2, user1, user2 / Password123)" -ForegroundColor Green
