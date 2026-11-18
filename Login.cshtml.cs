
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AccessRightsManagement.Pages
{
    public class LoginModel : PageModel
    {
        [BindProperty]
        public string Username { get; set; } = "";

        [BindProperty]
        public string Password { get; set; } = "";

        public string ErrorMessage { get; set; } = "";

        public void OnGet()
        {
        }

        public IActionResult OnPost()
        {
            // ยังไม่เชื่อมต่อฐานข้อมูล
            // จะเพิ่มระบบตรวจสอบบัญชีผู้ใช้ในขั้นตอนถัดไป

            ErrorMessage = "ยังไม่ได้เชื่อมต่อระบบตรวจสอบบัญชีผู้ใช้";

            return Page();
        }
    }
}
