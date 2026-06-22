using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuizGenAI.Models;

namespace QuizGenAI.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = SD.Role_Admin)]
    public class AdminSettingController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ApplicationDbContext _context;

        // Lưu trữ cấu hình hệ thống dạng static (giả lập, có thể chuyển sang DB/file config sau)
        private static readonly AdminSettingViewModel _settings = new()
        {
            AppName = "QuizGen AI",
            Slogan = "Học tập xuất sắc - Tạo đề bằng AI",
            LogoPath = "/images/logo.png",
            DefaultLanguage = "vi",
            EnableSystemAlert = true,
            EnableUserReport = true,
            EnableAIUpdate = false
        };

        public AdminSettingController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ApplicationDbContext context)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _context = context;
        }

        // ==========================================
        // GET: Admin/AdminSetting
        // ==========================================
        [HttpGet]
        public IActionResult Index()
        {
            ViewData["Title"] = "Cài đặt hệ thống";
            ViewData["ActivePage"] = "AdminSetting";
            return View("~/Areas/Admin/Views/AdminSetting/Index.cshtml", _settings);
        }

        // ==========================================
        // POST: Lưu cài đặt chung + bảo mật + thông báo
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveSettings([FromBody] AdminSettingViewModel model)
        {
            if (model == null)
                return Json(new { success = false, message = "Không nhận được dữ liệu cấu hình!" });

            if (!ModelState.IsValid)
            {
                var errors = string.Join("; ", ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage));
                return Json(new { success = false, message = $"Dữ liệu không hợp lệ: {errors}" });
            }

            _settings.AppName = model.AppName;
            _settings.Slogan = model.Slogan;
            _settings.LogoPath = model.LogoPath;
            _settings.DefaultLanguage = model.DefaultLanguage;
            _settings.EnableSystemAlert = model.EnableSystemAlert;
            _settings.EnableUserReport = model.EnableUserReport;
            _settings.EnableAIUpdate = model.EnableAIUpdate;

            return Json(new { success = true, message = "Cấu hình hệ thống đã được lưu thành công!" });
        }

        // ==========================================
        // POST: Đổi mật khẩu quản trị viên
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(
            [FromBody] ChangePasswordRequest request)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.CurrentPassword) ||
                string.IsNullOrWhiteSpace(request.NewPassword) ||
                string.IsNullOrWhiteSpace(request.ConfirmPassword))
            {
                return Json(new { success = false, message = "Vui lòng điền đầy đủ các trường mật khẩu." });
            }

            if (request.NewPassword != request.ConfirmPassword)
                return Json(new { success = false, message = "Mật khẩu mới và xác nhận mật khẩu không khớp." });

            if (request.NewPassword.Length < 6)
                return Json(new { success = false, message = "Mật khẩu mới phải có ít nhất 6 ký tự." });

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Json(new { success = false, message = "Không tìm thấy tài khoản quản trị viên." });

            var result = await _userManager.ChangePasswordAsync(
                user, request.CurrentPassword, request.NewPassword);

            if (result.Succeeded)
            {
                // Refresh cookie để không bị đăng xuất ngay sau khi đổi mật khẩu
                await _signInManager.RefreshSignInAsync(user);
                return Json(new { success = true, message = "Đổi mật khẩu thành công!" });
            }

            var errorMsg = result.Errors.FirstOrDefault()?.Description
                           ?? "Đổi mật khẩu thất bại.";
            // Chuyển lỗi Identity sang tiếng Việt phổ biến
            if (errorMsg.Contains("Incorrect password") || errorMsg.Contains("PasswordMismatch"))
                errorMsg = "Mật khẩu hiện tại không đúng.";

            return Json(new { success = false, message = errorMsg });
        }

        // ==========================================
        // GET: Lấy danh sách phiên quản trị viên đang hoạt động
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> GetActiveSessions()
        {
            var adminUsers = await _userManager.GetUsersInRoleAsync(SD.Role_Admin);

            var sessions = adminUsers.Select(u => new
            {
                userId = u.Id,
                fullName = u.FullName,
                email = u.Email,
                isActive = u.IsActive,
                isCurrent = u.UserName == User.Identity!.Name,
                lastSeen = u.CreatedAt // Hiển thị ngày tạo như một fallback (có thể mở rộng thêm)
            }).ToList();

            return Json(new { success = true, sessions });
        }

        // ==========================================
        // POST: Thu hồi tất cả phiên đăng nhập của một quản trị viên
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RevokeUserSessions([FromBody] RevokeSessionRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.UserId))
                return Json(new { success = false, message = "Không tìm thấy ID người dùng." });

            var user = await _userManager.FindByIdAsync(request.UserId);
            if (user == null)
                return Json(new { success = false, message = "Không tìm thấy tài khoản." });

            // Cập nhật SecurityStamp để vô hiệu hóa tất cả các cookie/token hiện tại
            var result = await _userManager.UpdateSecurityStampAsync(user);

            if (result.Succeeded)
            {
                var isSelf = user.UserName == User.Identity!.Name;
                if (isSelf)
                {
                    // Nếu thu hồi chính mình thì phải refresh cookie
                    await _signInManager.RefreshSignInAsync(user);
                }
                return Json(new
                {
                    success = true,
                    message = $"Đã thu hồi tất cả phiên đăng nhập của {user.FullName}.",
                    isSelf
                });
            }

            return Json(new { success = false, message = "Không thể thu hồi phiên đăng nhập." });
        }

        // ==========================================
        // POST: Lưu riêng cấu hình thông báo (AJAX toggle)
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveNotificationSettings([FromBody] NotificationSettingsRequest request)
        {
            if (request == null)
                return Json(new { success = false, message = "Dữ liệu không hợp lệ." });

            _settings.EnableSystemAlert = request.EnableSystemAlert;
            _settings.EnableUserReport = request.EnableUserReport;
            _settings.EnableAIUpdate = request.EnableAIUpdate;

            return Json(new
            {
                success = true,
                message = "Đã lưu cấu hình thông báo.",
                enableSystemAlert = _settings.EnableSystemAlert,
                enableUserReport = _settings.EnableUserReport,
                enableAIUpdate = _settings.EnableAIUpdate
            });
        }
    }

    // ==========================================
    // Request Models
    // ==========================================
    public class ChangePasswordRequest
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class RevokeSessionRequest
    {
        public string UserId { get; set; } = string.Empty;
    }

    public class NotificationSettingsRequest
    {
        public bool EnableSystemAlert { get; set; }
        public bool EnableUserReport { get; set; }
        public bool EnableAIUpdate { get; set; }
    }
}
