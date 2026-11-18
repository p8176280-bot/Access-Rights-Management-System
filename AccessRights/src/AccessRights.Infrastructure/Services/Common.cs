namespace AccessRights.Infrastructure.Services;

/// <summary>ผู้ใช้ที่กำลังทำรายการ (มาจาก Claims หลัง Login)</summary>
public record Actor(int Id, string FullName, bool IsAdmin, bool IsApprover, bool IsRequester)
{
    public void EnsureAdmin()
    {
        if (!IsAdmin) throw new Domain.ForbiddenException("เฉพาะ Admin เท่านั้น");
    }
}

public static class Clock
{
    /// <summary>วันที่ปัจจุบันตามเวลาเครื่อง Server (ใช้เทียบวันหมดอายุ)</summary>
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}
