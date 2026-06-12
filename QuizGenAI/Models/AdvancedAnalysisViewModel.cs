using System;
using System.Collections.Generic;

namespace QuizGenAI.Models
{
    public class AdvancedAnalysisViewModel
    {
        // 3 Thẻ thống kê & tỉ lệ phần trăm biến động
        public double CompletionRate { get; set; } = 76.0;
        public double CompletionRateChange { get; set; } = 4.2;

        public double ErrorQuestionRate { get; set; } = 2.4;
        public double ErrorQuestionRateChange { get; set; } = -0.5;

        public double AiQualityScore { get; set; } = 8.8;
        public double AiQualityScoreChange { get; set; } = 0.3;

        // Dữ liệu biểu đồ xu hướng (Chart.js)
        public List<string> ChartLabels { get; set; } = new();
        public List<int> QuestionsCreatedData { get; set; } = new();
        public List<int> ExamsTakenData { get; set; } = new();

        // Danh sách câu hỏi cần phân tích
        public List<AnalysisQuestionItemViewModel> Questions { get; set; } = new();

        // Số lượng câu hỏi cảnh báo nghiêm trọng
        public int TotalWarningsCount { get; set; } = 12;

        // Trạng thái tìm kiếm, lọc & phân trang
        public string? SearchQuery { get; set; }
        public string? SelectedSubject { get; set; }
        public string? SelectedWarningType { get; set; }
        public string SelectedTimeframe { get; set; } = "7days";
        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 5;
        public int TotalPages { get; set; } = 1;
    }

    public class AnalysisQuestionItemViewModel
    {
        public string QuestionId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string BloomLevel { get; set; } = string.Empty; // "Ghi nhớ" hoặc "Vận dụng"
        public string WarningType { get; set; } = string.Empty; // "Độ khó không khớp", "Lỗi logic AI", "Nội dung trùng lặp"
        public double LogicScore { get; set; }
    }
}
