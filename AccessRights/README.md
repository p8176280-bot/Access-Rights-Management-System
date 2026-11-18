# ระบบขอสิทธิใช้งานโปรแกรม บริษัท โตโยต้า (Access Rights Management System)

ระบบจัดการรายการสิทธิและการขออนุมัติสิทธิ พัฒนาตามเอกสาร System Analysis

- **Requester** เลือกสิทธิ ระบุวันสิ้นสุด แล้วส่งคำขอ แก้ไขหรือยกเลิกได้ระหว่างสถานะ *รอพิจารณา*
- **Approver** (ผู้อนุมัติประจำสิทธิ) อนุมัติหรือปฏิเสธคำขอ โดยต้องระบุเหตุผลเมื่อปฏิเสธ
- **Admin** สร้างบัญชีผู้ใช้ จัดการรายการสิทธิ และพิจารณาคำขอใดก็ได้ ยกเว้นคำขอของตนเอง
- แจ้งเตือนทั้งในระบบและทางอีเมล และบันทึกประวัติทุกการดำเนินการ

## เทคโนโลยี

| ส่วน | เทคโนโลยี |
|---|---|
| Frontend | Blazor (C#) + Bootstrap 5 |
| Backend | ASP.NET Core (.NET 10), Cookie Authentication |
| ORM | Entity Framework Core 10 |
| Database | PostgreSQL 15 (ค่าเริ่มต้น) หรือ SQL Server สลับได้ใน config |
| Test | xUnit |

## โครงสร้าง

```
AccessRights.slnx
├── database/
│   ├── schema-postgresql.sql      ตาราง, constraint, index, trigger, view (PostgreSQL)
│   └── schema-sqlserver.sql       ชุดเดียวกันสำหรับ SQL Server
├── src/
│   ├── AccessRights.Domain/          Entity + Business Rules (BR-02 ถึง BR-10)
│   ├── AccessRights.Infrastructure/  EF Core, Services, อีเมล, ข้อมูลเริ่มต้น
│   └── AccessRights.Web/             หน้าเว็บ Blazor + Login
├── tests/AccessRights.Tests/         Unit test ของ Business Rules (20 กรณี)
├── setup-dev.ps1                     ติดตั้งและเตรียมฐานข้อมูล
└── run-dev.ps1                       เปิดระบบ
```

## เริ่มใช้งาน (Windows + PostgreSQL 15)

1. แตกไฟล์ไว้ที่ เช่น `C:\Projects\AccessRights`
2. เปิด **PowerShell แบบ Run as Administrator** แล้วรัน
   ```powershell
   cd C:\Projects\AccessRights
   powershell -ExecutionPolicy Bypass -File .\setup-dev.ps1
   ```
   สคริปต์จะติดตั้ง .NET 10 SDK (ถ้ายังไม่มี) สร้าง user `access_app` และ database `access_rights` รัน `schema-postgresql.sql` แล้ว Build และรัน Test
3. เปิดระบบด้วย `powershell -ExecutionPolicy Bypass -File .\run-dev.ps1` แล้วเข้า http://localhost:5100

**บัญชีสำหรับทดลอง** (ระบบสร้างให้อัตโนมัติตอนเปิดครั้งแรกในโหมด Development)

| ชื่อผู้ใช้ | รหัสผ่าน | บทบาท |
|---|---|---|
| admin | Admin12345 | Admin |
| approver1 | Password123 | Approver (ผู้อนุมัติสิทธิ ERP) |
| approver2 | Password123 | Approver + Requester (ผู้อนุมัติสิทธิ HR, VPN) |
| user1, user2 | Password123 | Requester |

ตอนพัฒนา อีเมลจะไม่ถูกส่งจริง ระบบจะเขียนเป็นไฟล์ `.eml` ไว้ในโฟลเดอร์ `src/AccessRights.Web/mail-outbox` ดับเบิลคลิกเปิดดูได้ด้วย Outlook หรือ Mail

## ใช้ SQL Server แทน PostgreSQL

1. สร้าง Database: `CREATE DATABASE AccessRights COLLATE Thai_CI_AS;`
2. รัน `sqlcmd -S localhost -d AccessRights -E -i database\schema-sqlserver.sql`
3. แก้ `src/AccessRights.Web/appsettings.Development.json`
   ```json
   "Database": { "Provider": "SqlServer" },
   "ConnectionStrings": { "Default": "Server=localhost;Database=AccessRights;Trusted_Connection=True;TrustServerCertificate=True" }
   ```

## Business Rules ในโค้ด

| กฎ | ตรวจที่ |
|---|---|
| BR-01 Admin สร้างบัญชีเท่านั้น | `UserService` (ไม่มีหน้าสมัครสมาชิก) |
| BR-02 / BR-03 แก้ไขและยกเลิกได้เฉพาะ Pending | `AccessRequest.Edit` / `Cancel` |
| BR-04 ห้ามขอซ้ำ | `RequestService.EnsureNotDuplicateAsync` + unique index `ux_access_requests_one_pending` |
| BR-05 วันหมดอายุ | `AccessRequest.ValidateEndDate` (ผู้ยื่นระบุ ไม่เกินจำนวนวันที่ Admin กำหนดต่อสิทธิ) |
| BR-06 แจ้งเตือนในระบบ + อีเมล | `NotificationService` |
| BR-07 ผู้อนุมัติ 1 คนต่อคำขอ | ผู้อนุมัติประจำสิทธิ (Admin กำหนด) ถูกบันทึกลงคำขอตอนยื่น |
| BR-08 ห้ามพิจารณาคำขอของตนเอง | `AccessRequest.EnsureCanReview` + CHECK constraint ในฐานข้อมูล |
| BR-09 ปฏิเสธต้องมีเหตุผล | `AccessRequest.Reject` + CHECK constraint |
| BR-10 บันทึกประวัติ | ตาราง `request_histories` + Trigger กันแก้ไข/ลบ |

## สิ่งที่ตัดสินใจไว้ในโค้ด (ปรับได้ตามที่ตกลงกัน)

ประเด็นเหล่านี้เอกสาร SA ยังเปิดไว้ (หัวข้อ 8) โค้ดจึงเลือกแบบที่ง่ายที่สุด:

- 1 คำขอ = 1 สิทธิ และผู้อนุมัติคือผู้อนุมัติประจำสิทธิ
- Admin พิจารณาคำขอใดก็ได้ ถ้าผู้อนุมัติประจำสิทธิเป็นผู้ยื่นเอง ระบบจะแจ้ง Admin คนอื่นแทน
- ต้องมีบทบาท Requester จึงจะยื่นคำขอได้ Admin และ Approver ที่ต้องการขอสิทธิจึงต้องได้รับบทบาท Requester เพิ่ม
- ผู้ยื่นแก้ได้เฉพาะเหตุผลและวันสิ้นสุด ถ้าต้องการเปลี่ยนสิทธิให้ยกเลิกแล้วยื่นใหม่
- ถูกปฏิเสธแล้วยื่นใหม่ได้ทันที
- สิทธิที่เลยวันสิ้นสุดแสดงเป็น "หมดอายุ" อัตโนมัติ ยังไม่มีแจ้งเตือนก่อนหมดอายุ และยังไม่มีการถอนสิทธิ
- ปิดใช้งานบัญชีแล้ว ผู้ใช้จะ Login ใหม่ไม่ได้ แต่ Session ที่เปิดอยู่จะใช้ต่อได้จนหมดอายุ (8 ชั่วโมง) หรือจนกด Logout

## Deploy ด้วย Docker

```bash
cp .env.example .env      # แล้วแก้รหัสผ่าน
docker compose up -d --build
```
เปิด http://<server>:8080 แล้ว Login ด้วย `admin` และรหัสผ่านตามค่า `ADMIN_PASSWORD` PostgreSQL จะสร้างตารางจาก `schema-postgresql.sql` ให้อัตโนมัติตอนเปิดครั้งแรก

ก่อนใช้งานจริง ให้วาง Reverse proxy ที่เปิด HTTPS ไว้ด้านหน้า และตั้งค่า SMTP ขององค์กรในส่วน `Email`
