using AccessRights.Infrastructure;
using AccessRights.Infrastructure.Data;
using AccessRights.Web.Components;
using AccessRights.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddInfrastructure(builder.Configuration);

// Login ด้วยบัญชีที่ Admin สร้าง (BR-01) เก็บสถานะใน Cookie
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.Name = "AccessRights.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // หน้าในส่วนผู้ดูแลระบบ → ส่งไปหน้า Login ของผู้ดูแล
        options.Events.OnRedirectToLogin = ctx =>
        {
            var target = ctx.Request.Path.StartsWithSegments("/admin")
                ? ctx.RedirectUri.Replace("/login?", "/admin/login?")
                : ctx.RedirectUri;
            ctx.Response.Redirect(target);
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<CurrentUser>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

// สร้าง Admin คนแรก / ข้อมูลตัวอย่าง (ถ้ายังไม่มีผู้ใช้)
await DbInitializer.SeedAsync(app.Services);

app.Run();
