using AccessRights.Domain;
using AccessRights.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AccessRights.Infrastructure.Services;

/// <summary>อ่านและจัดการการแจ้งเตือนภายในระบบของผู้ใช้</summary>
public class InboxService(IDbContextFactory<AppDbContext> factory)
{
    public async Task<List<Notification>> ListAsync(Actor actor, int take = 100)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Notifications.AsNoTracking()
            .Where(n => n.UserId == actor.Id)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .ToListAsync();
    }

    public async Task<int> CountUnreadAsync(Actor actor)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Notifications.CountAsync(n => n.UserId == actor.Id && !n.IsRead);
    }

    public async Task MarkReadAsync(Actor actor, long id)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Notifications.Where(n => n.Id == id && n.UserId == actor.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
    }

    public async Task MarkAllReadAsync(Actor actor)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Notifications.Where(n => n.UserId == actor.Id && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
    }
}
