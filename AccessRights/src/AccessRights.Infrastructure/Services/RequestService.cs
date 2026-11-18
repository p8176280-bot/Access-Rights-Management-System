using AccessRights.Domain;
using AccessRights.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AccessRights.Infrastructure.Services;

/// <summary>สร้าง แก้ไข ยกเลิก ติดตาม และพิจารณาคำขอสิทธิ</summary>
public class RequestService(IDbContextFactory<AppDbContext> factory, NotificationService notifications)
{
    // ==================================================================
    // ผู้ยื่นคำขอ
    // ==================================================================
    public async Task<int> SubmitAsync(Actor actor, int rightId, string reason, DateOnly? endDate)
    {
        await using var db = await factory.CreateDbContextAsync();

        var requester = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == actor.Id)
                        ?? throw new NotFoundException("ไม่พบผู้ใช้");
        var right = await db.Rights.Include(r => r.Approver).FirstOrDefaultAsync(r => r.Id == rightId)
                    ?? throw new NotFoundException("ไม่พบสิทธิที่เลือก");

        await EnsureNotDuplicateAsync(db, actor.Id, rightId);

        var request = AccessRequest.Submit(requester, right, reason, endDate, Clock.Today, DateTime.UtcNow);
        db.AccessRequests.Add(request);
        await SaveAsync(db);

        // แจ้งผู้อนุมัติ (ถ้าผู้อนุมัติประจำสิทธิคือผู้ยื่นเอง ให้ Admin คนอื่นพิจารณาแทน)
        List<User> recipients = right.ApproverId != actor.Id
            ? [right.Approver]
            : await ActiveAdminsExceptAsync(db, actor.Id);

        var mails = notifications.Add(db, recipients, "มีคำขอสิทธิรอพิจารณา",
            $"{requester.FullName} ขอสิทธิ \"{right.Name}\" ({request.RequestNo})", Link(request));
        await db.SaveChangesAsync();
        await notifications.SendEmailsAsync(mails);

        return request.Id;
    }

    public async Task EditAsync(Actor actor, int requestId, int expectedVersion, string reason, DateOnly? endDate)
    {
        await using var db = await factory.CreateDbContextAsync();
        var request = await LoadForUpdateAsync(db, requestId, expectedVersion);

        request.Edit(actor.Id, request.Right, reason, endDate, Clock.Today, DateTime.UtcNow);

        var mails = notifications.Add(db, [request.Approver], "คำขอสิทธิถูกแก้ไข",
            $"{actor.FullName} แก้ไขคำขอ {request.RequestNo} ({request.Right.Name})", Link(request));
        await SaveAsync(db);
        await notifications.SendEmailsAsync(mails);
    }

    public async Task CancelAsync(Actor actor, int requestId, int expectedVersion)
    {
        await using var db = await factory.CreateDbContextAsync();
        var request = await LoadForUpdateAsync(db, requestId, expectedVersion);

        request.Cancel(actor.Id, DateTime.UtcNow);

        var mails = notifications.Add(db, [request.Approver], "คำขอสิทธิถูกยกเลิก",
            $"{actor.FullName} ยกเลิกคำขอ {request.RequestNo} ({request.Right.Name})", Link(request));
        await SaveAsync(db);
        await notifications.SendEmailsAsync(mails);
    }

    public async Task<List<AccessRequest>> ListMineAsync(Actor actor)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.AccessRequests.AsNoTracking()
            .Include(r => r.Right).Include(r => r.Approver)
            .Where(r => r.RequesterId == actor.Id)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    /// <summary>สิทธิที่ผู้ใช้ถืออยู่และยังไม่หมดอายุ</summary>
    public async Task<List<AccessRequest>> ListMyActiveRightsAsync(Actor actor)
    {
        var today = Clock.Today;
        await using var db = await factory.CreateDbContextAsync();
        return await db.AccessRequests.AsNoTracking()
            .Include(r => r.Right)
            .Where(r => r.RequesterId == actor.Id && r.Status == RequestStatus.Approved
                        && (r.EndDate == null || r.EndDate >= today))
            .OrderBy(r => r.EndDate)
            .ToListAsync();
    }

    // ==================================================================
    // รายละเอียด (ผู้ยื่น / ผู้อนุมัติของคำขอ / Admin)
    // ==================================================================
    public async Task<AccessRequest> GetDetailAsync(Actor actor, int requestId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var request = await db.AccessRequests.AsNoTracking()
                          .Include(r => r.Requester)
                          .Include(r => r.Right)
                          .Include(r => r.Approver)
                          .Include(r => r.DecidedBy)
                          .Include(r => r.Histories).ThenInclude(h => h.Actor)
                          .AsSplitQuery()
                          .FirstOrDefaultAsync(r => r.Id == requestId)
                      ?? throw new NotFoundException("ไม่พบคำขอ");

        var canView = actor.IsAdmin || request.RequesterId == actor.Id || request.ApproverId == actor.Id;
        if (!canView)
            throw new ForbiddenException("คุณไม่มีสิทธิ์ดูคำขอนี้");

        request.Histories = request.Histories.OrderBy(h => h.CreatedAt).ThenBy(h => h.Id).ToList();
        return request;
    }

    // ==================================================================
    // ผู้พิจารณา
    // ==================================================================

    /// <summary>คำขอที่รอผู้ใช้คนนี้พิจารณา (ไม่รวมคำขอของตนเอง — BR-08)</summary>
    public async Task<List<AccessRequest>> ListPendingReviewsAsync(Actor actor)
    {
        if (!actor.IsAdmin && !actor.IsApprover)
            return [];

        await using var db = await factory.CreateDbContextAsync();
        var query = db.AccessRequests.AsNoTracking()
            .Include(r => r.Requester).Include(r => r.Right).Include(r => r.Approver)
            .Where(r => r.Status == RequestStatus.Pending && r.RequesterId != actor.Id);

        if (!actor.IsAdmin)
            query = query.Where(r => r.ApproverId == actor.Id);

        return await query.OrderBy(r => r.CreatedAt).ToListAsync();
    }

    /// <summary>คำขอทั้งหมดในระบบ (Admin) — ล่าสุด 1,000 รายการ</summary>
    public async Task<List<AccessRequest>> ListAllAsync(Actor actor)
    {
        actor.EnsureAdmin();
        await using var db = await factory.CreateDbContextAsync();
        return await db.AccessRequests.AsNoTracking()
            .Include(r => r.Requester).Include(r => r.Right)
            .Include(r => r.Approver).Include(r => r.DecidedBy)
            .OrderByDescending(r => r.CreatedAt)
            .Take(1000)
            .ToListAsync();
    }

    public Task ApproveAsync(Actor actor, int requestId, int expectedVersion, string? comment) =>
        DecideAsync(actor, requestId, expectedVersion, approve: true, comment);

    public Task RejectAsync(Actor actor, int requestId, int expectedVersion, string? comment) =>
        DecideAsync(actor, requestId, expectedVersion, approve: false, comment);

    private async Task DecideAsync(Actor actor, int requestId, int expectedVersion, bool approve, string? comment)
    {
        await using var db = await factory.CreateDbContextAsync();
        var request = await LoadForUpdateAsync(db, requestId, expectedVersion);

        // ยืนยันบทบาทจากฐานข้อมูล (เผื่อ Admin เพิ่งถอดบทบาทระหว่างที่ผู้ใช้ยัง Login อยู่)
        var reviewer = await db.Users.Include(u => u.Roles).AsNoTracking().FirstAsync(u => u.Id == actor.Id);
        if (!reviewer.IsActive) throw new ForbiddenException("บัญชีผู้ใช้นี้ถูกปิดใช้งาน");
        var isAdmin = reviewer.HasRole(Role.Admin);
        var isApprover = reviewer.HasRole(Role.Approver);

        var now = DateTime.UtcNow;
        if (approve)
            request.Approve(actor.Id, isAdmin, isApprover, comment, now);
        else
            request.Reject(actor.Id, isAdmin, isApprover, comment, now);

        var (title, message) = approve
            ? ("คำขอสิทธิได้รับการอนุมัติ",
               $"คำขอ {request.RequestNo} ({request.Right.Name}) ได้รับการอนุมัติโดย {reviewer.FullName}")
            : ("คำขอสิทธิถูกปฏิเสธ",
               $"คำขอ {request.RequestNo} ({request.Right.Name}) ถูกปฏิเสธโดย {reviewer.FullName} เหตุผล: {request.DecisionComment}");

        var mails = notifications.Add(db, [request.Requester], title, message, Link(request));
        await SaveAsync(db);
        await notifications.SendEmailsAsync(mails);
    }

    // ==================================================================
    private static async Task<AccessRequest> LoadForUpdateAsync(AppDbContext db, int requestId, int expectedVersion)
    {
        var request = await db.AccessRequests
                          .Include(r => r.Right)
                          .Include(r => r.Requester)
                          .Include(r => r.Approver)
                          .FirstOrDefaultAsync(r => r.Id == requestId)
                      ?? throw new NotFoundException("ไม่พบคำขอ");

        // ถ้ามีคนเปลี่ยนคำขอไปแล้วหลังจากที่หน้าจอโหลดข้อมูลมา ให้หยุดและให้ผู้ใช้โหลดใหม่
        if (request.Version != expectedVersion)
            throw new DomainException("คำขอนี้ถูกเปลี่ยนแปลงไปแล้ว กรุณาโหลดหน้าใหม่แล้วลองอีกครั้ง");

        return request;
    }

    /// <summary>BR-04: ห้ามขอซ้ำ ถ้ามีคำขอ Pending หรือมีสิทธิที่ยังใช้งานอยู่</summary>
    private static async Task EnsureNotDuplicateAsync(AppDbContext db, int userId, int rightId)
    {
        var today = Clock.Today;
        var existing = await db.AccessRequests
            .Where(r => r.RequesterId == userId && r.RightId == rightId
                        && (r.Status == RequestStatus.Pending
                            || (r.Status == RequestStatus.Approved && (r.EndDate == null || r.EndDate >= today))))
            .Select(r => new { r.Id, r.Status })
            .FirstOrDefaultAsync();

        if (existing is null) return;

        var no = $"REQ-{existing.Id:D6}";
        throw new DomainException(existing.Status == RequestStatus.Pending
            ? $"คุณมีคำขอสิทธินี้ที่รอพิจารณาอยู่แล้ว ({no})"
            : $"คุณมีสิทธินี้และยังใช้งานได้อยู่ ({no})");
    }

    private static async Task<List<User>> ActiveAdminsExceptAsync(AppDbContext db, int userId) =>
        await db.Users.Where(u => u.IsActive && u.Id != userId && u.Roles.Any(r => r.Role == Role.Admin))
            .ToListAsync();

    private static async Task SaveAsync(AppDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException("คำขอนี้ถูกเปลี่ยนแปลงพร้อมกันโดยผู้ใช้อื่น กรุณาโหลดหน้าใหม่");
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // กันกรณีกดส่งพร้อมกัน 2 ครั้ง (unique index ux_access_requests_one_pending)
            throw new DomainException("คุณมีคำขอสิทธินี้ที่รอพิจารณาอยู่แล้ว");
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var msg = ex.InnerException?.Message ?? "";
        return msg.Contains("ux_access_requests_one_pending", StringComparison.OrdinalIgnoreCase);
    }

    private static string Link(AccessRequest r) => $"/requests/{r.Id}";
}
