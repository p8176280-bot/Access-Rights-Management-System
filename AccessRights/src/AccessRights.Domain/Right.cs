namespace AccessRights.Domain;

/// <summary>รายการสิทธิที่ผู้ใช้ขอได้</summary>
public class Right
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }

    /// <summary>ผู้อนุมัติประจำสิทธิ (Admin กำหนด)</summary>
    public int ApproverId { get; set; }
    public User Approver { get; set; } = default!;

    /// <summary>ขอได้นานสุดกี่วัน — null = ไม่บังคับวันหมดอายุ (BR-05)</summary>
    public int? MaxDurationDays { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

public class Notification
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = default!;
    public string Message { get; set; } = default!;
    public string? Link { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
