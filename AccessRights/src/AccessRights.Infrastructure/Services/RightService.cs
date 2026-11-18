using AccessRights.Domain;
using AccessRights.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AccessRights.Infrastructure.Services;

public class RightInput
{
    public int? Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int? ApproverId { get; set; }
    public int? MaxDurationDays { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>จัดการรายการสิทธิ</summary>
public class RightService(IDbContextFactory<AppDbContext> factory)
{
    /// <summary>สิทธิที่เปิดให้ขอ (ทุกคนดูได้)</summary>
    public async Task<List<Right>> ListActiveAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Rights.Include(r => r.Approver).AsNoTracking()
            .Where(r => r.IsActive).OrderBy(r => r.Name).ToListAsync();
    }

    public async Task<List<Right>> ListAllAsync(Actor actor)
    {
        actor.EnsureAdmin();
        await using var db = await factory.CreateDbContextAsync();
        return await db.Rights.Include(r => r.Approver).AsNoTracking().OrderBy(r => r.Name).ToListAsync();
    }

    /// <summary>ผู้ที่ตั้งเป็นผู้อนุมัติได้: ผู้ใช้ที่ยังใช้งานอยู่และมีบทบาท Approver หรือ Admin</summary>
    public async Task<List<User>> ListApproverCandidatesAsync(Actor actor)
    {
        actor.EnsureAdmin();
        await using var db = await factory.CreateDbContextAsync();
        return await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Roles.Any(r => r.Role == Role.Approver || r.Role == Role.Admin))
            .OrderBy(u => u.FullName).ToListAsync();
    }

    public async Task<int> SaveAsync(Actor actor, RightInput input)
    {
        actor.EnsureAdmin();

        var name = input.Name.Trim();
        if (name.Length is 0 or > 150)
            throw new DomainException("กรุณาระบุชื่อสิทธิ (ไม่เกิน 150 ตัวอักษร)");
        if (input.Description?.Length > 1000)
            throw new DomainException("คำอธิบายยาวได้ไม่เกิน 1000 ตัวอักษร");
        if (input.MaxDurationDays is < 1 or > 3650)
            throw new DomainException("จำนวนวันสูงสุดต้องอยู่ระหว่าง 1 - 3650 วัน (หรือเว้นว่างถ้าไม่จำกัด)");
        if (input.ApproverId is null)
            throw new DomainException("กรุณาเลือกผู้อนุมัติประจำสิทธิ");

        await using var db = await factory.CreateDbContextAsync();

        var approverOk = await db.Users.AnyAsync(u => u.Id == input.ApproverId && u.IsActive
            && u.Roles.Any(r => r.Role == Role.Approver || r.Role == Role.Admin));
        if (!approverOk)
            throw new DomainException("ผู้อนุมัติต้องเป็นผู้ใช้ที่ใช้งานอยู่และมีบทบาท Approver หรือ Admin");

        if (await db.Rights.AnyAsync(r => r.Name == name && r.Id != (input.Id ?? 0)))
            throw new DomainException("มีสิทธิชื่อนี้อยู่แล้ว");

        Right right;
        if (input.Id is null)
        {
            right = new Right { CreatedAt = DateTime.UtcNow };
            db.Rights.Add(right);
        }
        else
        {
            right = await db.Rights.FindAsync(input.Id.Value) ?? throw new NotFoundException("ไม่พบสิทธิ");
        }

        right.Name = name;
        right.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        right.ApproverId = input.ApproverId.Value;
        right.MaxDurationDays = input.MaxDurationDays;
        right.IsActive = input.IsActive;

        // หมายเหตุ: คำขอที่ Pending อยู่แล้วยังคงใช้ผู้อนุมัติเดิมที่บันทึกไว้ตอนยื่น
        await db.SaveChangesAsync();
        return right.Id;
    }
}
