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
        public string Slogan { get; set; } = "Học tập xuất sắc - Tạo đề bằng AI";

        [Display(Name = "Đường dẫn Logo")]
        public string LogoPath { get; set; } = "/images/logo.png";

        [Required(ErrorMessage = "Ngôn ngữ mặc định không được để trống")]
        [Display(Name = "Ngôn ngữ mặc định")]
        public string DefaultLanguage { get; set; } = "vi"; // "vi" hoặc "en"

        // Bảo mật & Tài khoản


        // Cấu hình thông báo
        [Display(Name = "Cảnh báo hệ thống")]
        public bool EnableSystemAlert { get; set; } = true;

        [Display(Name = "Báo cáo từ người dùng")]
        public bool EnableUserReport { get; set; } = true;

        [Display(Name = "Bản cập nhật AI")]
        public bool EnableAIUpdate { get; set; } = false;
    }
}
