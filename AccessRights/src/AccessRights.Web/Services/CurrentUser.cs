using System.Globalization;
using System.Security.Claims;
using AccessRights.Domain;
using AccessRights.Infrastructure.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace AccessRights.Web.Services;

/// <summary>อ่านผู้ใช้ที่ Login อยู่จาก Claims แล้วแปลงเป็น Actor ส่งให้ Service</summary>
public class CurrentUser(AuthenticationStateProvider provider)
{
    public const string FullNameClaim = "full_name";

    public async Task<Actor> GetAsync()
    {
        var state = await provider.GetAuthenticationStateAsync();
        var p = state.User;
        if (p.Identity?.IsAuthenticated != true
            || !int.TryParse(p.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            throw new ForbiddenException("กรุณาเข้าสู่ระบบ");

        return new Actor(
            id,
            p.FindFirstValue(FullNameClaim) ?? p.Identity.Name ?? "",
            p.IsInRole(nameof(Role.Admin)),
            p.IsInRole(nameof(Role.Approver)),
            p.IsInRole(nameof(Role.Requester)));
    }
}

/// <summary>รูปแบบการแสดงผลวันที่และข้อความสถานะ</summary>
public static class Fmt
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Day(DateOnly? d) => d?.ToString("dd/MM/yyyy", Invariant) ?? "-";

    public static string Time(DateTime? t) => t?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Invariant) ?? "-";

    public static string InputDate(DateOnly d) => d.ToString("yyyy-MM-dd", Invariant);

    public static string Status(AccessRequest r, DateOnly today) =>
        r.IsExpiredOn(today) ? "หมดอายุ" : Status(r.Status);

    public static string Status(RequestStatus s) => s switch
    {
        RequestStatus.Pending => "รอพิจารณา",
        RequestStatus.Approved => "อนุมัติ",
        RequestStatus.Rejected => "ปฏิเสธ",
        RequestStatus.Cancelled => "ยกเลิก",
        _ => s.ToString()
    };

    public static string Action(HistoryAction a) => a switch
    {
        HistoryAction.Created => "ยื่นคำขอ",
        HistoryAction.Edited => "แก้ไขคำขอ",
        HistoryAction.Cancelled => "ยกเลิกคำขอ",
        HistoryAction.Approved => "อนุมัติ",
        HistoryAction.Rejected => "ปฏิเสธ",
        _ => a.ToString()
    };

    public static string RoleName(Role r) => r switch
    {
        Role.Admin => "Admin",
        Role.Requester => "Requester",
        Role.Approver => "Approver",
        _ => r.ToString()
    };

    public static string RoleDescription(Role r) => r switch
    {
        Role.Admin => "ผู้ดูแลระบบ: สร้างและจัดการบัญชีผู้ใช้ จัดการรายการสิทธิ และพิจารณาคำขอได้ทุกรายการ (ยกเว้นคำขอของตนเอง)",
        Role.Requester => "ผู้ขอสิทธิ: ยื่นคำขอ แก้ไขหรือยกเลิกคำขอที่ยังรอพิจารณา และติดตามสถานะ",
        Role.Approver => "ผู้อนุมัติ: ตรวจสอบ อนุมัติ หรือปฏิเสธคำขอของสิทธิที่ตนเป็นผู้อนุมัติ",
        _ => r.ToString()
    };

    public static string MaxDays(int? days) => days is { } d ? $"{d} วัน" : "ไม่จำกัด";
}
