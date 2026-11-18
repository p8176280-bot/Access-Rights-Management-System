using System.Text.RegularExpressions;
using AccessRights.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AccessRights.Infrastructure.Data;

/// <summary>
/// แม็ปกับตารางที่สร้างจาก database/schema-*.sql (ชื่อตาราง/คอลัมน์แบบ snake_case)
/// ใช้ได้ทั้ง PostgreSQL และ SQL Server
/// </summary>
public partial class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Right> Rights => Set<Right>();
    public DbSet<AccessRequest> AccessRequests => Set<AccessRequest>();
    public DbSet<RequestHistory> RequestHistories => Set<RequestHistory>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasMany(x => x.Roles).WithOne().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<UserRole>(e =>
        {
            e.ToTable("user_roles");
            e.HasKey(x => new { x.UserId, x.Role });
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<Right>(e =>
        {
            e.ToTable("rights");
            e.HasOne(x => x.Approver).WithMany().HasForeignKey(x => x.ApproverId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<AccessRequest>(e =>
        {
            e.ToTable("access_requests");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne(x => x.Requester).WithMany().HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Approver).WithMany().HasForeignKey(x => x.ApproverId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DecidedBy).WithMany().HasForeignKey(x => x.DecidedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Right).WithMany().HasForeignKey(x => x.RightId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Histories).WithOne().HasForeignKey(h => h.AccessRequestId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<RequestHistory>(e =>
        {
            // ตารางนี้มี Trigger กันการแก้ไข/ลบ (BR-10) — แจ้ง EF เพื่อให้ SQL Server สร้างคำสั่ง INSERT ที่รองรับ
            e.ToTable("request_histories", t => t.HasTrigger("trg_request_histories_readonly"));
            e.Property(x => x.Action).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Notification>(e =>
        {
            e.ToTable("notifications");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ชื่อคอลัมน์ = snake_case ของชื่อ Property และเวลาทั้งหมดเก็บเป็น UTC
        var utc = new ValueConverter<DateTime, DateTime>(
            v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        foreach (var entity in b.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                    property.SetValueConverter(utc);
            }
        }
    }

    private static string ToSnakeCase(string name) => SnakeRegex().Replace(name, "$1_$2").ToLowerInvariant();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex SnakeRegex();
}
