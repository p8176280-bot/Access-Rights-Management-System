using AccessRights.Domain;

namespace AccessRights.Tests;

/// <summary>ทดสอบ Business Rules ตามเอกสาร System Analysis (BR-02 ถึง BR-10)</summary>
public class BusinessRuleTests
{
    private static readonly DateOnly Today = new(2026, 11, 18);
    private static readonly DateTime Now = new(2026, 11, 18, 3, 0, 0, DateTimeKind.Utc);

    private const int RequesterId = 1, ApproverId = 2, AdminId = 3, OtherApproverId = 4;

    private static User Requester(params Role[] roles) => new()
    {
        Id = RequesterId,
        Username = "user1",
        FullName = "User",
        Email = "u@x.com",
        Roles = (roles.Length == 0 ? [Role.Requester] : roles).Select(r => new UserRole { Role = r }).ToList()
    };

    private static Right MakeRight(int? maxDays = 90, int approverId = ApproverId) => new()
    {
        Id = 10,
        Name = "ERP",
        ApproverId = approverId,
        MaxDurationDays = maxDays
    };

    private static AccessRequest NewRequest(int? maxDays = 90) =>
        AccessRequest.Submit(Requester(), MakeRight(maxDays), "ต้องใช้งาน", Today.AddDays(30), Today, Now);

    // ---------------- สร้างคำขอ ----------------

    [Fact]
    public void Submit_creates_pending_request_with_right_approver_and_history()
    {
        var r = NewRequest();

        Assert.Equal(RequestStatus.Pending, r.Status);
        Assert.Equal(ApproverId, r.ApproverId);
        Assert.Single(r.Histories);
        Assert.Equal(HistoryAction.Created, r.Histories[0].Action);
    }

    [Fact]
    public void Submit_requires_requester_role()
    {
        Assert.Throws<DomainException>(() =>
            AccessRequest.Submit(Requester(Role.Approver), MakeRight(), "x", Today.AddDays(1), Today, Now));
    }

    [Fact]
    public void Submit_requires_reason()
    {
        Assert.Throws<DomainException>(() =>
            AccessRequest.Submit(Requester(), MakeRight(), "   ", Today.AddDays(1), Today, Now));
    }

    [Fact]
    public void Submit_rejects_inactive_right()
    {
        var right = MakeRight();
        right.IsActive = false;
        Assert.Throws<DomainException>(() =>
            AccessRequest.Submit(Requester(), right, "x", Today.AddDays(1), Today, Now));
    }

    // ---------------- BR-05: วันหมดอายุ ----------------

    [Fact]
    public void End_date_cannot_exceed_right_max_duration()
    {
        Assert.Throws<DomainException>(() =>
            AccessRequest.Submit(Requester(), MakeRight(maxDays: 90), "x", Today.AddDays(91), Today, Now));
    }

    [Fact]
    public void End_date_is_required_when_right_has_max_duration()
    {
        Assert.Throws<DomainException>(() =>
            AccessRequest.Submit(Requester(), MakeRight(maxDays: 90), "x", null, Today, Now));
    }

    [Fact]
    public void End_date_is_optional_when_right_has_no_max_duration()
    {
        var r = AccessRequest.Submit(Requester(), MakeRight(maxDays: null), "x", null, Today, Now);
        Assert.Null(r.EndDate);
    }

    [Fact]
    public void End_date_cannot_be_in_the_past()
    {
        Assert.Throws<DomainException>(() =>
            AccessRequest.Submit(Requester(), MakeRight(), "x", Today.AddDays(-1), Today, Now));
    }

    [Fact]
    public void Approved_request_is_active_until_end_date_then_expired()
    {
        var r = NewRequest();
        r.Approve(ApproverId, isAdmin: false, isApprover: true, null, Now);

        Assert.True(r.IsActiveOn(r.EndDate!.Value));
        Assert.False(r.IsExpiredOn(r.EndDate.Value));
        Assert.True(r.IsExpiredOn(r.EndDate.Value.AddDays(1)));
        Assert.False(r.IsActiveOn(r.EndDate.Value.AddDays(1)));
    }

    // ---------------- BR-02 / BR-03: แก้ไข / ยกเลิก ----------------

    [Fact]
    public void Requester_can_edit_pending_request_and_history_is_recorded()
    {
        var r = NewRequest();
        r.Edit(RequesterId, MakeRight(), "เหตุผลใหม่", Today.AddDays(60), Today, Now);

        Assert.Equal("เหตุผลใหม่", r.Reason);
        Assert.Equal(1, r.Version);
        Assert.Equal(HistoryAction.Edited, r.Histories.Last().Action);
    }

    [Fact]
    public void Cannot_edit_after_decision()
    {
        var r = NewRequest();
        r.Approve(ApproverId, false, true, null, Now);
        Assert.Throws<DomainException>(() => r.Edit(RequesterId, MakeRight(), "ใหม่", Today.AddDays(5), Today, Now));
    }

    [Fact]
    public void Other_user_cannot_edit_or_cancel()
    {
        var r = NewRequest();
        Assert.Throws<ForbiddenException>(() => r.Edit(ApproverId, MakeRight(), "ใหม่", Today.AddDays(5), Today, Now));
        Assert.Throws<ForbiddenException>(() => r.Cancel(ApproverId, Now));
    }

    [Fact]
    public void Requester_can_cancel_only_pending()
    {
        var r = NewRequest();
        r.Cancel(RequesterId, Now);
        Assert.Equal(RequestStatus.Cancelled, r.Status);
        Assert.Throws<DomainException>(() => r.Cancel(RequesterId, Now));
    }

    // ---------------- BR-07 / BR-08: ผู้พิจารณา ----------------

    [Fact]
    public void Assigned_approver_can_approve()
    {
        var r = NewRequest();
        r.Approve(ApproverId, false, true, "ok", Now);

        Assert.Equal(RequestStatus.Approved, r.Status);
        Assert.Equal(ApproverId, r.DecidedById);
        Assert.Equal(HistoryAction.Approved, r.Histories.Last().Action);
    }

    [Fact]
    public void Other_approver_cannot_approve()
    {
        var r = NewRequest();
        Assert.Throws<ForbiddenException>(() => r.Approve(OtherApproverId, false, true, null, Now));
    }

    [Fact]
    public void Admin_can_approve_any_request()
    {
        var r = NewRequest();
        r.Approve(AdminId, isAdmin: true, isApprover: false, null, Now);
        Assert.Equal(RequestStatus.Approved, r.Status);
    }

    [Fact]
    public void Requester_cannot_review_own_request_even_as_admin_or_approver()
    {
        // ผู้อนุมัติประจำสิทธิคือผู้ยื่นเอง
        var r = AccessRequest.Submit(Requester(Role.Requester, Role.Approver, Role.Admin),
            MakeRight(approverId: RequesterId), "x", Today.AddDays(1), Today, Now);

        Assert.False(r.CanBeReviewedBy(RequesterId, isAdmin: true, isApprover: true));
        Assert.Throws<DomainException>(() => r.Approve(RequesterId, true, true, null, Now));
        Assert.Throws<DomainException>(() => r.Reject(RequesterId, true, true, "no", Now));
    }

    [Fact]
    public void Request_can_be_decided_only_once()
    {
        var r = NewRequest();
        r.Approve(ApproverId, false, true, null, Now);
        Assert.Throws<DomainException>(() => r.Reject(AdminId, true, false, "เปลี่ยนใจ", Now));
    }

    // ---------------- BR-09: ปฏิเสธต้องมีเหตุผล ----------------

    [Fact]
    public void Reject_requires_reason()
    {
        var r = NewRequest();
        Assert.Throws<DomainException>(() => r.Reject(ApproverId, false, true, "  ", Now));
        Assert.Equal(RequestStatus.Pending, r.Status);
    }

    [Fact]
    public void Reject_with_reason_records_history()
    {
        var r = NewRequest();
        r.Reject(ApproverId, false, true, "ไม่เกี่ยวข้องกับงาน", Now);

        Assert.Equal(RequestStatus.Rejected, r.Status);
        Assert.Equal("ไม่เกี่ยวข้องกับงาน", r.DecisionComment);
        Assert.Equal(HistoryAction.Rejected, r.Histories.Last().Action);
        Assert.Equal("ไม่เกี่ยวข้องกับงาน", r.Histories.Last().Comment);
    }
}
