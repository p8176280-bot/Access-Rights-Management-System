namespace AccessRights.Domain;

/// <summary>คำขอสิทธิ — 1 คำขอ = 1 สิทธิ และมีผู้อนุมัติ 1 คน (BR-07)</summary>
public class AccessRequest
{
    public const int MaxTextLength = 1000;

    public int Id { get; set; }

    public int RequesterId { get; set; }
    public User Requester { get; set; } = default!;

    public int RightId { get; set; }
    public Right Right { get; set; } = default!;

    public int ApproverId { get; set; }
    public User Approver { get; set; } = default!;

    public string Reason { get; set; } = "";
    /// <summary>วันสุดท้ายที่ใช้สิทธิได้ (ผู้ยื่นระบุ) — null = ไม่มีวันหมดอายุ</summary>
    public DateOnly? EndDate { get; set; }
    public RequestStatus Status { get; set; }

    public int? DecidedById { get; set; }
    public User? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionComment { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    /// <summary>เพิ่มทุกครั้งที่เปลี่ยน ใช้กันการแก้ไขและพิจารณาชนกัน</summary>
    public int Version { get; set; }

    public List<RequestHistory> Histories { get; set; } = [];

    public string RequestNo => $"REQ-{Id:D6}";

    // ------------------------------------------------------------------
    // สร้างคำขอ
    // ------------------------------------------------------------------
    public static AccessRequest Submit(User requester, Right right, string reason, DateOnly? endDate,
        DateOnly today, DateTime nowUtc)
    {
        if (!requester.IsActive)
            throw new DomainException("บัญชีผู้ใช้นี้ถูกปิดใช้งาน");
        if (!requester.HasRole(Role.Requester))
            throw new DomainException("ต้องมีบทบาท Requester จึงจะยื่นคำขอได้");
        if (!right.IsActive)
            throw new DomainException("สิทธินี้ปิดการใช้งานแล้ว");

        var request = new AccessRequest
        {
            RequesterId = requester.Id,
            RightId = right.Id,
            ApproverId = right.ApproverId,
            Reason = ValidateReason(reason),
            EndDate = ValidateEndDate(right, endDate, today),
            Status = RequestStatus.Pending,
            CreatedAt = nowUtc
        };
        request.AddHistory(HistoryAction.Created, requester.Id, null, nowUtc);
        return request;
    }

    // ------------------------------------------------------------------
    // ผู้ยื่น: แก้ไข / ยกเลิก (BR-02, BR-03)
    // ------------------------------------------------------------------
    public void Edit(int actorId, Right right, string reason, DateOnly? endDate, DateOnly today, DateTime nowUtc)
    {
        EnsureRequesterAndPending(actorId, "แก้ไข");

        var newReason = ValidateReason(reason);
        var newEnd = ValidateEndDate(right, endDate, today);

        var changes = new List<string>();
        if (newReason != Reason) changes.Add("เหตุผล");
        if (newEnd != EndDate) changes.Add($"วันสิ้นสุด {FormatDate(EndDate)} → {FormatDate(newEnd)}");
        if (changes.Count == 0)
            throw new DomainException("ไม่มีข้อมูลที่เปลี่ยนแปลง");

        Reason = newReason;
        EndDate = newEnd;
        Touch(nowUtc);
        AddHistory(HistoryAction.Edited, actorId, "แก้ไข: " + string.Join(", ", changes), nowUtc);
    }

    public void Cancel(int actorId, DateTime nowUtc)
    {
        EnsureRequesterAndPending(actorId, "ยกเลิก");
        Status = RequestStatus.Cancelled;
        Touch(nowUtc);
        AddHistory(HistoryAction.Cancelled, actorId, null, nowUtc);
    }

    // ------------------------------------------------------------------
    // ผู้พิจารณา: อนุมัติ / ปฏิเสธ (BR-07, BR-08, BR-09)
    // ------------------------------------------------------------------

    /// <summary>
    /// ผู้พิจารณาได้: ผู้อนุมัติประจำคำขอ (ต้องมีบทบาท Approver) หรือ Admin
    /// และต้องไม่ใช่ผู้ยื่นคำขอเอง แม้จะมีบทบาท Admin/Approver (BR-08)
    /// </summary>
    public bool CanBeReviewedBy(int reviewerId, bool isAdmin, bool isApprover) =>
        Status == RequestStatus.Pending
        && reviewerId != RequesterId
        && (isAdmin || (isApprover && reviewerId == ApproverId));

    public void Approve(int reviewerId, bool isAdmin, bool isApprover, string? comment, DateTime nowUtc)
    {
        EnsureCanReview(reviewerId, isAdmin, isApprover);
        Decide(RequestStatus.Approved, reviewerId, Clean(comment), nowUtc);
    }

    public void Reject(int reviewerId, bool isAdmin, bool isApprover, string? comment, DateTime nowUtc)
    {
        EnsureCanReview(reviewerId, isAdmin, isApprover);
        var reason = Clean(comment)
                     ?? throw new DomainException("กรุณาระบุเหตุผลที่ปฏิเสธ (BR-09)");
        Decide(RequestStatus.Rejected, reviewerId, reason, nowUtc);
    }

    // ------------------------------------------------------------------
    // สถานะสิทธิ (BR-04, BR-05)
    // ------------------------------------------------------------------
    public bool IsActiveOn(DateOnly today) =>
        Status == RequestStatus.Approved && (EndDate is null || EndDate >= today);

    public bool IsExpiredOn(DateOnly today) =>
        Status == RequestStatus.Approved && EndDate is { } end && end < today;

    /// <summary>กฎวันสิ้นสุด: ไม่ก่อนวันนี้ และไม่เกินจำนวนวันสูงสุดของสิทธิ</summary>
    public static DateOnly? ValidateEndDate(Right right, DateOnly? endDate, DateOnly today)
    {
        if (endDate is null)
        {
            if (right.MaxDurationDays is { } days)
                throw new DomainException($"สิทธินี้ต้องระบุวันสิ้นสุด (ขอได้ไม่เกิน {days} วัน)");
            return null;
        }

        if (endDate < today)
            throw new DomainException("วันสิ้นสุดต้องไม่ก่อนวันนี้");

        if (right.MaxDurationDays is { } max && endDate > today.AddDays(max))
            throw new DomainException($"สิทธินี้ขอได้ไม่เกิน {max} วัน (วันสิ้นสุดได้ถึง {FormatDate(today.AddDays(max))})");

        return endDate;
    }

    // ------------------------------------------------------------------
    private void EnsureRequesterAndPending(int actorId, string action)
    {
        if (actorId != RequesterId)
            throw new ForbiddenException($"{action}ได้เฉพาะคำขอของตนเอง");
        if (Status != RequestStatus.Pending)
            throw new DomainException($"{action}ได้เฉพาะคำขอที่สถานะ Pending");
    }

    private void EnsureCanReview(int reviewerId, bool isAdmin, bool isApprover)
    {
        if (Status != RequestStatus.Pending)
            throw new DomainException("คำขอนี้ได้รับการพิจารณาหรือถูกยกเลิกไปแล้ว");
        if (reviewerId == RequesterId)
            throw new DomainException("ไม่สามารถพิจารณาคำขอของตนเองได้ (BR-08)");
        if (!isAdmin && !(isApprover && reviewerId == ApproverId))
            throw new ForbiddenException("คุณไม่ใช่ผู้อนุมัติของคำขอนี้");
    }

    private void Decide(RequestStatus status, int reviewerId, string? comment, DateTime nowUtc)
    {
        Status = status;
        DecidedById = reviewerId;
        DecidedAt = nowUtc;
        DecisionComment = comment;
        Touch(nowUtc);
        AddHistory(status == RequestStatus.Approved ? HistoryAction.Approved : HistoryAction.Rejected,
            reviewerId, comment, nowUtc);
    }

    private void Touch(DateTime nowUtc)
    {
        UpdatedAt = nowUtc;
        Version++;
    }

    private void AddHistory(HistoryAction action, int actorId, string? comment, DateTime nowUtc) =>
        Histories.Add(new RequestHistory { Action = action, ActorId = actorId, Comment = comment, CreatedAt = nowUtc });

    private static string ValidateReason(string? reason)
    {
        var text = Clean(reason) ?? throw new DomainException("กรุณาระบุเหตุผลในการขอสิทธิ");
        if (text.Length > MaxTextLength)
            throw new DomainException($"เหตุผลยาวได้ไม่เกิน {MaxTextLength} ตัวอักษร");
        return text;
    }

    private static string? Clean(string? text)
    {
        var t = text?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        return t.Length > MaxTextLength ? t[..MaxTextLength] : t;
    }

    private static string FormatDate(DateOnly? d) => d?.ToString("dd/MM/yyyy") ?? "ไม่กำหนด";
}

/// <summary>ประวัติการดำเนินการกับคำขอ (BR-10) — เพิ่มได้อย่างเดียว</summary>
public class RequestHistory
{
    public long Id { get; set; }
    public int AccessRequestId { get; set; }
    public HistoryAction Action { get; set; }
    public int ActorId { get; set; }
    public User Actor { get; set; } = default!;
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
}
