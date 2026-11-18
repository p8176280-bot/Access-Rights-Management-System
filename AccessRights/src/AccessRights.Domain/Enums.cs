namespace AccessRights.Domain;

public enum Role
{
    Admin,
    Requester,
    Approver
}

public enum RequestStatus
{
    Pending,
    Approved,
    Rejected,
    Cancelled
}

public enum HistoryAction
{
    Created,
    Edited,
    Cancelled,
    Approved,
    Rejected
}

/// <summary>ข้อผิดพลาดที่แสดงให้ผู้ใช้เห็นได้</summary>
public class AppException(string message) : Exception(message);

/// <summary>ทำไม่ได้ตามกฎธุรกิจ</summary>
public class DomainException(string message) : AppException(message);

public class NotFoundException(string message) : AppException(message);

public class ForbiddenException(string message) : AppException(message);
