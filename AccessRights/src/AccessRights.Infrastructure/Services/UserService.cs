using System.Net.Mail;
using System.Text.RegularExpressions;
using AccessRights.Domain;
using AccessRights.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AccessRights.Infrastructure.Services;

public class UserInput
{
    public int? Id { get; set; }
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    /// <summary>บังคับตอนสร้าง / ว่างได้ตอนแก้ไข (ไม่เปลี่ยนรหัสผ่าน)</summary>
    public string? Password { get; set; }
    public bool IsActive { get; set; } = true;
    public HashSet<Role> Roles { get; set; } = [];
}

/// <summary>จัดการบัญชีผู้ใช้ — Admin เท่านั้น (BR-01)</summary>
public partial class UserService(IDbContextFactory<AppDbContext> factory, IPasswordHasher<User> hasher)
{
    public const int MinPasswordLength = 8;

    public async Task<List<User>> ListAsync(Actor actor)
    {
        actor.EnsureAdmin();
        await using var db = await factory.CreateDbContextAsync();
        return await db.Users.Include(u => u.Roles).AsNoTracking().OrderBy(u => u.Username).ToListAsync();
    }

    /// <summary>ข้อมูลบัญชีของผู้ใช้เอง (ดูได้อย่างเดียว)</summary>
    public async Task<User> GetSelfAsync(Actor actor)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Users.Include(u => u.Roles).AsNoTracking().FirstOrDefaultAsync(u => u.Id == actor.Id)
               ?? throw new NotFoundException("ไม่พบผู้ใช้");
    }

    public async Task<int> SaveAsync(Actor actor, UserInput input)
    {
        actor.EnsureAdmin();

        var username = input.Username.Trim().ToLowerInvariant();
        var fullName = input.FullName.Trim();
        var email = input.Email.Trim().ToLowerInvariant();

        if (!UsernameRegex().IsMatch(username))
            throw new DomainException("ชื่อผู้ใช้ต้องเป็น a-z, 0-9, . _ - ยาว 3-50 ตัวอักษร");
        if (fullName.Length is 0 or > 150)
            throw new DomainException("กรุณาระบุชื่อ-นามสกุล (ไม่เกิน 150 ตัวอักษร)");
        if (!MailAddress.TryCreate(email, out _) || email.Length > 150)
            throw new DomainException("รูปแบบอีเมลไม่ถูกต้อง");
        if (input.Roles.Count == 0)
            throw new DomainException("กรุณาเลือกบทบาทอย่างน้อย 1 บทบาท");

        await using var db = await factory.CreateDbContextAsync();

        if (await db.Users.AnyAsync(u => u.Username == username && u.Id != (input.Id ?? 0)))
            throw new DomainException("ชื่อผู้ใช้นี้มีอยู่แล้ว");
        if (await db.Users.AnyAsync(u => u.Email == email && u.Id != (input.Id ?? 0)))
            throw new DomainException("อีเมลนี้มีผู้ใช้อยู่แล้ว");

        User user;
        if (input.Id is null)
        {
            ValidatePassword(input.Password);
            user = new User { CreatedAt = DateTime.UtcNow };
            db.Users.Add(user);
        }
        else
        {
            user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == input.Id)
                   ?? throw new NotFoundException("ไม่พบผู้ใช้");

            if (user.Id == actor.Id)
            {
                if (!input.IsActive)
                    throw new DomainException("ไม่สามารถปิดใช้งานบัญชีของตนเองได้");
                if (!input.Roles.Contains(Role.Admin))
                    throw new DomainException("ไม่สามารถถอดบทบาท Admin ของตนเองได้");
            }
            if (!string.IsNullOrEmpty(input.Password))
                ValidatePassword(input.Password);
        }

        user.Username = username;
        user.FullName = fullName;
        user.Email = email;
        user.IsActive = input.IsActive;
        if (!string.IsNullOrEmpty(input.Password))
            user.PasswordHash = hasher.HashPassword(user, input.Password);

        // ปรับบทบาทเฉพาะส่วนที่เปลี่ยน
        user.Roles.RemoveAll(r => !input.Roles.Contains(r.Role));
        foreach (var role in input.Roles.Where(r => !user.HasRole(r)))
            user.Roles.Add(new UserRole { Role = role });

        await db.SaveChangesAsync();
        return user.Id;
    }

    private static void ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            throw new DomainException($"รหัสผ่านต้องยาวอย่างน้อย {MinPasswordLength} ตัวอักษร");
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            throw new DomainException("รหัสผ่านต้องมีทั้งตัวอักษรและตัวเลข");
    }

    [GeneratedRegex("^[a-z0-9._-]{3,50}$")]
    private static partial Regex UsernameRegex();
}
