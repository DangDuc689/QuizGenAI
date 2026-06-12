using System.Collections.Generic;

namespace QuizGenAI.Models
{
    public class AdminDashboardViewModel
    {
        // 4 Thẻ thống kê
        public int TotalUsers { get; set; } = 12450;
        public string TotalUsersChange { get; set; } = "+12%";

        public int NewDocuments { get; set; } = 842;
        public string NewDocumentsChange { get; set; } = "+5%";

        public int QuestionsCreated { get; set; } = 3120;
        public string QuestionsCreatedChange { get; set; } = "Tăng trưởng";

        public double SuccessRate { get; set; } = 98.5;
        public string SuccessRateChange { get; set; } = "Ổn định";

        // Tài liệu mới nhất
        public List<AdminRecentDocumentItemViewModel> RecentDocuments { get; set; } = new();

        // Phân bổ Cấp độ Bloom
        public int BloomRememberPercent { get; set; } = 45;
        public int BloomUnderstandPercent { get; set; } = 32;
        public int BloomApplyPercent { get; set; } = 23;

        // Thông báo Hệ thống (Logs)
        public List<AdminSystemLogItemViewModel> SystemLogs { get; set; } = new();

        // Lọc thời gian hiện tại ("today", "7days", "30days")
        public string SelectedTimeframe { get; set; } = "today";
    }

    public class AdminRecentDocumentItemViewModel
    {
        public string Filename { get; set; } = string.Empty;
        public string UploadedTime { get; set; } = string.Empty;
        public int QuestionsCount { get; set; }
        public string FileSize { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty; // "pdf" hoặc "docx"
    }

    public class AdminSystemLogItemViewModel
    {
        public string LogType { get; set; } = "Info"; // "Info" (Xanh dương), "Success" (Xanh lá), "Warning" (Cam)
        public string Message { get; set; } = string.Empty;
        public string TimeString { get; set; } = string.Empty;
    }
}
