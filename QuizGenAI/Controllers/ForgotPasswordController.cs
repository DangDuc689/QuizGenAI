using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using QuizGenAI.Models;
using QuizGenAI.Services;

namespace QuizGenAI.Controllers
{
    public class ForgotPasswordController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<ForgotPasswordController> _logger;

        public ForgotPasswordController(
            UserManager<ApplicationUser> userManager,
            IEmailSender emailSender,
            ILogger<ForgotPasswordController> logger)
        {
            _userManager = userManager;
            _emailSender = emailSender;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View(new InputModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(InputModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var email = model.Email.Trim();
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null || !user.IsActive)
            {
                return View("Confirmation");
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var resetLink = Url.Action(
                nameof(ResetPassword),
                "ForgotPassword",
                new { email = user.Email, token = encodedToken },
                Request.Scheme);

            if (string.IsNullOrWhiteSpace(resetLink))
            {
                ModelState.AddModelError(string.Empty, "Không tạo được liên kết đặt lại mật khẩu.");
                return View(model);
            }

            try
            {
                var subject = "Đặt lại mật khẩu QuizGen AI";
                var htmlMessage = BuildResetPasswordEmail(user.FullName, resetLink);

                await _emailSender.SendEmailAsync(email, subject, htmlMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send reset password email to {Email}", email);
                ModelState.AddModelError(string.Empty, "Không gửi được email đặt lại mật khẩu. Vui lòng kiểm tra cấu hình email.");
                return View(model);
            }

            return View("Confirmation");
        }

        [HttpGet]
        public IActionResult ResetPassword(string? email = null, string? token = null)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            {
                return RedirectToAction(nameof(Index));
            }

            return View(new ResetInputModel
            {
                Email = email,
                Token = token
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetInputModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.FindByEmailAsync(model.Email.Trim());
            if (user == null)
            {
                return RedirectToAction(nameof(ResetPasswordConfirmation));
            }

            string decodedToken;
            try
            {
                decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(model.Token));
            }
            catch (FormatException)
            {
                ModelState.AddModelError(string.Empty, "Liên kết đặt lại mật khẩu không hợp lệ.");
                return View(model);
            }

            var result = await _userManager.ResetPasswordAsync(user, decodedToken, model.Password);
            if (result.Succeeded)
            {
                return RedirectToAction(nameof(ResetPasswordConfirmation));
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        [HttpGet]
        public IActionResult ResetPasswordConfirmation()
        {
            return View();
        }

        private static string BuildResetPasswordEmail(string fullName, string resetLink)
        {
            var safeName = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(fullName) ? "bạn" : fullName);
            var safeLink = WebUtility.HtmlEncode(resetLink);

            return $"""
                <div style="font-family:Arial,sans-serif;line-height:1.6;color:#0f172a">
                    <h2 style="color:#0037b0;margin-bottom:8px">QuizGen AI</h2>
                    <p>Xin chào {safeName},</p>
                    <p>Bạn vừa yêu cầu đặt lại mật khẩu cho tài khoản QuizGen AI.</p>
                    <p>
                        <a href="{safeLink}" style="display:inline-block;background:#0037b0;color:#ffffff;padding:12px 18px;border-radius:8px;text-decoration:none;font-weight:bold">
                            Đặt lại mật khẩu
                        </a>
                    </p>
                    <p>Nếu nút phía trên không hoạt động, hãy sao chép liên kết này vào trình duyệt:</p>
                    <p style="word-break:break-all;color:#0037b0">{safeLink}</p>
                    <p>Nếu bạn không yêu cầu đặt lại mật khẩu, hãy bỏ qua email này.</p>
                </div>
                """;
        }

        public class InputModel
        {
            [Required(ErrorMessage = "Vui lòng nhập địa chỉ email.")]
            [EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ.")]
            [Display(Name = "Địa chỉ Email")]
            public string Email { get; set; } = string.Empty;
        }

        public class ResetInputModel
        {
            [Required]
            public string Token { get; set; } = string.Empty;

            [Required(ErrorMessage = "Vui lòng nhập địa chỉ email.")]
            [EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ.")]
            [Display(Name = "Địa chỉ Email")]
            public string Email { get; set; } = string.Empty;

            [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới.")]
            [StringLength(100, ErrorMessage = "Mật khẩu phải có ít nhất {2} ký tự.", MinimumLength = 6)]
            [DataType(DataType.Password)]
            [Display(Name = "Mật khẩu mới")]
            public string Password { get; set; } = string.Empty;

            [Required(ErrorMessage = "Vui lòng nhập lại mật khẩu mới.")]
            [DataType(DataType.Password)]
            [Compare(nameof(Password), ErrorMessage = "Mật khẩu xác nhận không khớp.")]
            [Display(Name = "Xác nhận mật khẩu mới")]
            public string ConfirmPassword { get; set; } = string.Empty;
        }
    }
}
