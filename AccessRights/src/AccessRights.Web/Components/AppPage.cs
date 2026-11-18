using AccessRights.Domain;
using AccessRights.Infrastructure.Services;
using AccessRights.Web.Services;
using Microsoft.AspNetCore.Components;

namespace AccessRights.Web.Components;

/// <summary>
/// คลาสฐานของทุกหน้า: โหลดผู้ใช้ปัจจุบัน และแสดงข้อผิดพลาดตามกฎธุรกิจเป็นข้อความบนหน้าจอ
/// </summary>
public abstract class AppPage : ComponentBase
{
    [Inject] protected CurrentUser CurrentUser { get; set; } = default!;

    protected Actor? Actor { get; private set; }
    protected string? Error { get; set; }
    protected string? Success { get; set; }
    protected bool Busy { get; private set; }
    protected bool Loaded { get; private set; }

    protected static DateOnly Today => Clock.Today;

    protected override async Task OnInitializedAsync()
    {
        await RunAsync(async () =>
        {
            Actor = await CurrentUser.GetAsync();
            await LoadAsync();
        });
        Loaded = true;
    }

    /// <summary>โหลดข้อมูลของหน้า (Actor พร้อมใช้แล้ว)</summary>
    protected virtual Task LoadAsync() => Task.CompletedTask;

    /// <summary>รันงาน แสดงข้อความสำเร็จ หรือข้อความผิดพลาดที่ผู้ใช้อ่านเข้าใจ</summary>
    protected async Task<bool> RunAsync(Func<Task> action, string? successMessage = null)
    {
        if (Busy) return false;
        Busy = true;
        Error = null;
        Success = null;
        try
        {
            await action();
            Success = successMessage;
            return true;
        }
        catch (AppException ex)
        {
            Error = ex.Message;
            return false;
        }
        finally
        {
            Busy = false;
        }
    }
}
