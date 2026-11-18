namespace AccessRights.Domain;

public class User
{
    public int Id { get; set; }
    /// <summary>เก็บเป็นตัวพิมพ์เล็กเสมอ</summary>
    public string Username { get; set; } = default!;
    public string FullName { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public List<UserRole> Roles { get; set; } = [];

    public bool HasRole(Role role) => Roles.Any(r => r.Role == role);
}

public class UserRole
{
    public int UserId { get; set; }
    public Role Role { get; set; }
}
