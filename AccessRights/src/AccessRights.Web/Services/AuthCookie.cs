using System.Security.Claims;
using AccessRights.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace AccessRights.Web.Services;

/// <summary>เขียน Cookie การเข้าสู่ระบบ (ใช้ร่วมกันระหว่างหน้า Login ผู้ใช้และหน้า Login ผู้ดูแลระบบ)</summary>
public static class AuthCookie
{
    public static Task SignInAsync(HttpContext http, User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(CurrentUser.FullNameClaim, user.FullName)
        };
        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r.Role.ToString())));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }

    /// <summary>กันการพาไปเว็บอื่นหลัง Login (Open Redirect)</summary>
    public static bool IsLocalUrl(string? url) =>
        !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\")
        && !url.Contains("/login", StringComparison.OrdinalIgnoreCase);
}
