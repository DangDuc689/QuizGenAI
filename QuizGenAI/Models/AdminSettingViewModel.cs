using System.ComponentModel.DataAnnotations;

namespace QuizGenAI.Models
{
    public class AdminSettingViewModel
    {
        // Cấu hình chung
        [Required(ErrorMessage = "Tên ứng dụng không được để trống")]
        [Display(Name = "Tên ứng dụng")]
        public string AppName { get; set; } = "QuizGen AI";

        [Display(Name = "Slogan / Mô tả")]
        public string Slogan { get; set; } = "Academic Excellence & AI-powered Quiz Generation";

        [Display(Name = "Đường dẫn Logo")]
        public string LogoPath { get; set; } = "/images/logo.png";

        [Required(ErrorMessage = "Ngôn ngữ mặc định không được để trống")]
        [Display(Name = "Ngôn ngữ mặc định")]
        public string DefaultLanguage { get; set; } = "vi"; // "vi" hoặc "en"

        // Bảo mật & Tài khoản
        [Display(Name = "Xác thực 2 yếu tố (2FA)")]
        public bool Enable2FA { get; set; } = false;

        [Required(ErrorMessage = "Thời gian hết hạn phiên không được để trống")]
        [Range(5, 1440, ErrorMessage = "Thời gian hết hạn phiên phải từ 5 đến 1440 phút")]
        [Display(Name = "Thời gian hết hạn phiên (phút)")]
        public int SessionTimeout { get; set; } = 60;

        // Cấu hình thông báo
        [Display(Name = "Cảnh báo hệ thống")]
        public bool EnableSystemAlert { get; set; } = true;

        [Display(Name = "Báo cáo từ người dùng")]
        public bool EnableUserReport { get; set; } = true;

        [Display(Name = "Bản cập nhật AI")]
        public bool EnableAIUpdate { get; set; } = false;
    }
}
