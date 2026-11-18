using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AccessRights.Infrastructure.Services;

public class EmailOptions
{
    public bool Enabled { get; set; } = true;
    public string From { get; set; } = "no-reply@example.com";
    /// <summary>ตั้งค่านี้ตอนพัฒนา: อีเมลจะถูกเขียนเป็นไฟล์ .eml ในโฟลเดอร์นี้แทนการส่งจริง</summary>
    public string? PickupDirectory { get; set; }
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody);
}

public class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        var o = options.Value;
        if (!o.Enabled) return;

        using var message = new MailMessage(o.From, to, subject, htmlBody)
        {
            IsBodyHtml = true,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8
        };

        using var client = new SmtpClient();
        if (!string.IsNullOrWhiteSpace(o.PickupDirectory))
        {
            var dir = Path.GetFullPath(o.PickupDirectory);
            Directory.CreateDirectory(dir);
            client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
            client.PickupDirectoryLocation = dir;
        }
        else
        {
            client.Host = o.Host ?? throw new InvalidOperationException("ยังไม่ได้ตั้งค่า Email:Host");
            client.Port = o.Port;
            client.EnableSsl = o.EnableSsl;
            if (!string.IsNullOrEmpty(o.Username))
                client.Credentials = new NetworkCredential(o.Username, o.Password);
        }

        await client.SendMailAsync(message);
    }
}

/// <summary>
/// แจ้งเตือนในระบบ (บันทึกพร้อมงานหลักใน SaveChanges เดียวกัน) + อีเมล (ส่งหลังบันทึกสำเร็จ) — BR-06
/// </summary>
public class NotificationService(IEmailSender email, ILogger<NotificationService> logger)
{
    public record EmailJob(string To, string Subject, string Body);

    public List<EmailJob> Add(Data.AppDbContext db, IEnumerable<Domain.User> recipients,
        string title, string message, string link)
    {
        var jobs = new List<EmailJob>();
        foreach (var user in recipients.DistinctBy(u => u.Id))
        {
            db.Notifications.Add(new Domain.Notification
            {
                UserId = user.Id,
                Title = title,
                Message = message,
                Link = link,
                CreatedAt = DateTime.UtcNow
            });

            var body = $"""
                <p>เรียน คุณ{WebUtility.HtmlEncode(user.FullName)}</p>
                <p>{WebUtility.HtmlEncode(message)}</p>
                <p>ดูรายละเอียดในระบบขอสิทธิใช้งานโปรแกรม บริษัท โตโยต้า: <b>{WebUtility.HtmlEncode(link)}</b></p>
                """;
            jobs.Add(new EmailJob(user.Email, $"[ระบบขอสิทธิ โตโยต้า] {title}", body));
        }
        return jobs;
    }

    public async Task SendEmailsAsync(IEnumerable<EmailJob> jobs)
    {
        foreach (var job in jobs)
        {
            try
            {
                await email.SendAsync(job.To, job.Subject, job.Body);
            }
            catch (Exception ex)
            {
                // อีเมลล้มเหลวไม่ทำให้รายการหลักล้มเหลว (แจ้งเตือนในระบบยังอยู่)
                logger.LogWarning(ex, "ส่งอีเมลถึง {To} ไม่สำเร็จ", job.To);
            }
        }
    }
}
