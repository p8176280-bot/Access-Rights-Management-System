using AccessRights.Domain;
using AccessRights.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AccessRights.Infrastructure.Services;

public class AuthService(IDbContextFactory<AppDbContext> factory, IPasswordHasher<User> hasher)
{
    /// <summary>ตรวจชื่อผู้ใช้/รหัสผ่าน คืนผู้ใช้พร้อมบทบาท หรือ null ถ้าไม่ผ่าน</summary>
    public async Task<User?> ValidateAsync(string? username, string? password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return null;

        await using var db = await factory.CreateDbContextAsync();
        var key = username.Trim().ToLowerInvariant();
        var user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Username == key);
        if (user is null || !user.IsActive)
            return null;

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
            return null;

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, password);
            await db.SaveChangesAsync();
        }
        return user;
    }
}
