using System;
using System.Collections.Generic;

namespace QuizGenAI.Models
{
    public class AdvancedAnalysisViewModel
    {
        // 3 Thẻ thống kê & tỉ lệ phần trăm biến động hoặc nhãn đánh giá
        public int TotalExams { get; set; }
        public string TotalExamsChangeText { get; set; } = "+0%";

        public double AverageScore { get; set; }
        public string AverageScoreText { get; set; } = "N/A";

        public double CompletionRate { get; set; }
        public string CompletionStatusText { get; set; } = "Ổn định";

        // Dữ liệu biểu đồ xu hướng (Chart.js) - Số lượt thi tăng trưởng theo từng ngày trong tuần
        public List<string> ChartLabels { get; set; } = new();
        public List<int> ExamsTakenData { get; set; } = new();

        // Danh sách lượt làm bài gần đây
        public List<RecentExamSessionViewModel> RecentSessions { get; set; } = new();

        // Trạng thái tìm kiếm, lọc & phân trang
        public string? SearchQuery { get; set; }
        public string SelectedTimeframe { get; set; } = "thisweek"; // "thisweek", "lastweek"
        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 5;
        public int TotalSessionsCount { get; set; }
        public int TotalPages { get; set; } = 1;
    }

    public class RecentExamSessionViewModel
    {
        public int SessionId { get; set; }
        public string UserFullName { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;
        public string QuizSetTitle { get; set; } = string.Empty;
        public double Score { get; set; } // Điểm số trên thang điểm 10 (ví dụ 8.5)
        public int CorrectAnswers { get; set; }
        public int TotalQuestions { get; set; }
        public string DurationText { get; set; } = string.Empty; // e.g., "15 phút 42 giây"
        public string StatusText { get; set; } = string.Empty; // "HOÀN THÀNH", "BỎ DỞ", "ĐANG THI"
        public string StatusCssClass { get; set; } = string.Empty; // css class cho badge trạng thái
        public DateTime StartedAt { get; set; }
    }
}

