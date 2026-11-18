using AccessRights.Domain;
using AccessRights.Infrastructure.Data;
using AccessRights.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AccessRights.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ไม่พบ ConnectionStrings:Default ใน appsettings");

        // เลือกฐานข้อมูล: "PostgreSQL" (ค่าเริ่มต้น) หรือ "SqlServer"
        var provider = config["Database:Provider"] ?? "PostgreSQL";
        services.AddDbContextFactory<AppDbContext>(options =>
        {
            if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                options.UseSqlServer(connectionString);
            else
                options.UseNpgsql(connectionString);
        });

        services.Configure<EmailOptions>(config.GetSection("Email"));
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

        services.AddScoped<NotificationService>();
        services.AddScoped<AuthService>();
        services.AddScoped<UserService>();
        services.AddScoped<RightService>();
        services.AddScoped<RequestService>();
        services.AddScoped<InboxService>();

        return services;
    }
}
