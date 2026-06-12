using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuizGenAI.Models;

namespace QuizGenAI.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = SD.Role_Admin)]
    public class AdminSettingController : Controller
    {
        // Khởi tạo biến static để lưu trữ cấu hình hệ thống (giả lập DB hoặc file config)
        private static readonly AdminSettingViewModel _settings = new()
        {
            AppName = "QuizGen AI",
            Slogan = "Học tập xuất sắc - Tạo đề bằng AI",
            LogoPath = "/images/logo.png",
            DefaultLanguage = "vi",
            Enable2FA = false,
            SessionTimeout = 60,
            EnableSystemAlert = true,
            EnableUserReport = true,
            EnableAIUpdate = false
        };

        // GET: Admin/AdminSetting
        [HttpGet]
        public IActionResult Index()
        {
            ViewData["Title"] = "Cài đặt hệ thống";
            ViewData["ActivePage"] = "AdminSetting";

            return View("~/Areas/Admin/Views/AdminSetting/Index.cshtml", _settings);
        }

        // POST: Admin/AdminSetting/SaveSettings
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveSettings([FromBody] AdminSettingViewModel model)
        {
            if (model == null)
            {
                return Json(new { success = false, message = "Không nhận được dữ liệu cấu hình!" });
            }

            if (!ModelState.IsValid)
            {
                var errors = string.Join("; ", ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage));
                return Json(new { success = false, message = $"Dữ liệu không hợp lệ: {errors}" });
            }

            // Giả lập lưu dữ liệu vào cơ sở dữ liệu hoặc cấu hình hệ thống
            _settings.AppName = model.AppName;
            _settings.Slogan = model.Slogan;
            _settings.LogoPath = model.LogoPath;
            _settings.DefaultLanguage = model.DefaultLanguage;
            _settings.Enable2FA = model.Enable2FA;
            _settings.SessionTimeout = model.SessionTimeout;
            _settings.EnableSystemAlert = model.EnableSystemAlert;
            _settings.EnableUserReport = model.EnableUserReport;
            _settings.EnableAIUpdate = model.EnableAIUpdate;

            return Json(new { success = true, message = "Cấu hình hệ thống đã được lưu thành công!" });
        }
    }
}
