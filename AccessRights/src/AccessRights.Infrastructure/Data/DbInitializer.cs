using AccessRights.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AccessRights.Infrastructure.Data;

/// <summary>
/// สร้างบัญชี Admin คนแรก (เมื่อยังไม่มีผู้ใช้เลย) และข้อมูลตัวอย่าง (เมื่อ Seed:DemoData = true)
/// ตารางต้องถูกสร้างจาก database/schema-*.sql ก่อน
/// </summary>
public static class DbInitializer
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        await using var db = await factory.CreateDbContextAsync();

        bool hasUsers;
        try
        {
            hasUsers = await db.Users.AnyAsync();
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex,
                "เชื่อมต่อฐานข้อมูลหรืออ่านตาราง users ไม่ได้ — ตรวจ ConnectionStrings:Default และรัน database/schema-*.sql ก่อน");
            throw;
        }
        if (hasUsers) return;

        var adminPassword = config["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            logger.LogWarning("ยังไม่มีผู้ใช้ในระบบ — ตั้งค่า Seed:AdminPassword เพื่อสร้างบัญชี Admin คนแรก");
            return;
        }

        var now = DateTime.UtcNow;
        User NewUser(string username, string fullName, string password, params Role[] roles)
        {
            var u = new User
            {
                Username = username,
                FullName = fullName,
                Email = $"{username}@example.com",
                CreatedAt = now,
                Roles = roles.Select(r => new UserRole { Role = r }).ToList()
            };
            u.PasswordHash = hasher.HashPassword(u, password);
            db.Users.Add(u);
            return u;
        }

        var admin = NewUser(config["Seed:AdminUsername"] ?? "admin", "ผู้ดูแลระบบ", adminPassword, Role.Admin);
        if (config["Seed:AdminEmail"] is { Length: > 0 } adminEmail)
            admin.Email = adminEmail;
        logger.LogInformation("สร้างบัญชี Admin: {Username}", admin.Username);

        if (config.GetValue<bool>("Seed:DemoData"))
        {
            const string pw = "Password123";
            var approver1 = NewUser("approver1", "วิชัย มั่นคง (ผู้อนุมัติ ERP)", pw, Role.Approver);
            var approver2 = NewUser("approver2", "มาลี ศรีสุข (ผู้อนุมัติ HR)", pw, Role.Approver, Role.Requester);
            NewUser("user1", "สมชาย ใจดี", pw, Role.Requester);
            NewUser("user2", "สมหญิง รักงาน", pw, Role.Requester);
            await db.SaveChangesAsync();

            db.Rights.AddRange(
                new Right { Name = "ERP - ดูข้อมูล", Description = "ดูรายงานบัญชีและการเงิน", ApproverId = approver1.Id, CreatedAt = now },
                new Right { Name = "ERP - บันทึกรายการ", Description = "บันทึก/แก้ไขเอกสารบัญชี", ApproverId = approver1.Id, MaxDurationDays = 180, CreatedAt = now },
                new Right { Name = "HR - ข้อมูลพนักงาน", Description = "ดูข้อมูลพนักงานในระบบ HR", ApproverId = approver2.Id, MaxDurationDays = 90, CreatedAt = now },
                new Right { Name = "VPN", Description = "เข้าถึงเครือข่ายบริษัทจากภายนอก", ApproverId = approver2.Id, MaxDurationDays = 365, CreatedAt = now });

            logger.LogInformation("สร้างข้อมูลตัวอย่าง: approver1, approver2, user1, user2 (รหัสผ่าน {Password}) และสิทธิ 4 รายการ", pw);
        }

        await db.SaveChangesAsync();
    }
}
