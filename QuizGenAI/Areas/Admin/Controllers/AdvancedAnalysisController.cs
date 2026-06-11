using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuizGenAI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QuizGenAI.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = SD.Role_Admin)]
    public class AdvancedAnalysisController : Controller
    {
        // Danh sách câu hỏi cảnh báo chất lượng giả lập (Mock Data)
        private static readonly List<AnalysisQuestionItemViewModel> _mockQuestions = new()
        {
            new AnalysisQuestionItemViewModel { QuestionId = "Q-1042", Content = "Phương trình bậc hai ax^2 + bx + c = 0 có nghiệm kép khi nào?", Subject = "Toán học", BloomLevel = "Ghi nhớ", WarningType = "Độ khó không khớp", LogicScore = 4.8 },
            new AnalysisQuestionItemViewModel { QuestionId = "Q-2081", Content = "Hiện tượng khúc xạ ánh sáng xảy ra khi nào và có đặc điểm gì?", Subject = "Vật lý", BloomLevel = "Ghi nhớ", WarningType = "Nội dung trùng lặp", LogicScore = 8.5 },
            new AnalysisQuestionItemViewModel { QuestionId = "Q-3092", Content = "Viết chương trình Python tính giai thừa của một số nguyên dương n dùng đệ quy.", Subject = "Tin học", BloomLevel = "Vận dụng", WarningType = "Lỗi logic AI", LogicScore = 3.2 },
            new AnalysisQuestionItemViewModel { QuestionId = "Q-4051", Content = "Thì hiện tại hoàn thành tiếp diễn được sử dụng trong trường hợp nào?", Subject = "Tiếng Anh", BloomLevel = "Vận dụng", WarningType = "Độ khó không khớp", LogicScore = 9.2 },
            new AnalysisQuestionItemViewModel { QuestionId = "Q-5012", Content = "Định luật bảo toàn khối lượng được phát biểu như thế nào và ai tìm ra?", Subject = "Hóa học", BloomLevel = "Ghi nhớ", WarningType = "Lỗi logic AI", LogicScore = 4.5 },
            new AnalysisQuestionItemViewModel { QuestionId = "Q-6023", Content = "Tính thể tích của hình chóp tứ giác đều có cạnh đáy là a, chiều cao là h.", Subject = "Toán học", BloomLevel = "Vận dụng", WarningType = "Nội dung trùng lặp", LogicScore = 8.8 },
            new AnalysisQuestionItemViewModel { QuestionId = "Q-7084", Content = "Tại sao nước biển lại có vị mặn và vai trò của muối đối với đại dương?", Subject = "Địa lý", BloomLevel = "Ghi nhớ", WarningType = "Độ khó không khớp", LogicScore = 7.9 },
            new AnalysisQuestionItemViewModel { QuestionId = "Q-8095", Content = "Phân tích đặc điểm nghệ thuật nổi bật của bài thơ 'Sóng' - Xuân Quỳnh.", Subject = "Ngữ văn", BloomLevel = "Vận dụng", WarningType = "Lỗi logic AI", LogicScore = 2.8 }
        };

        // Action hiển thị trang phân tích nâng cao
        public IActionResult Index(string search, string subject, string warningType, string timeframe = "7days", int page = 1)
        {
            ViewData["Title"] = "Phân tích Nâng cao";
            ViewData["ActivePage"] = "AdvancedAnalysis";

            var filtered = _mockQuestions.AsQueryable();

            // Tìm kiếm câu hỏi
            if (!string.IsNullOrEmpty(search))
            {
                var lowerSearch = search.ToLower();
                filtered = filtered.Where(q => q.QuestionId.ToLower().Contains(lowerSearch) || q.Content.ToLower().Contains(lowerSearch));
            }

            // Lọc môn học
            if (!string.IsNullOrEmpty(subject))
            {
                filtered = filtered.Where(q => q.Subject.Equals(subject, StringComparison.OrdinalIgnoreCase));
            }

            // Lọc loại cảnh báo
            if (!string.IsNullOrEmpty(warningType))
            {
                filtered = filtered.Where(q => q.WarningType.Equals(warningType, StringComparison.OrdinalIgnoreCase));
            }

            // Phân trang
            int pageSize = 5;
            int totalItems = filtered.Count();
            int totalPages = (int)Math.Ceiling((double)totalItems / pageSize);
            if (totalPages == 0) totalPages = 1;

            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            var paginatedQuestions = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            // Tạo dữ liệu biểu đồ
            List<string> chartLabels;
            List<int> questionsCreatedData;
            List<int> examsTakenData;

            if (timeframe == "30days")
            {
                chartLabels = new List<string> { "Tuần 1", "Tuần 2", "Tuần 3", "Tuần 4" };
                questionsCreatedData = new List<int> { 600, 750, 800, 950 };
                examsTakenData = new List<int> { 1800, 2200, 2400, 2900 };
            }
            else // Mặc định 7 ngày qua
            {
                chartLabels = new List<string> { "Thứ 2", "Thứ 3", "Thứ 4", "Thứ 5", "Thứ 6", "Thứ 7", "Chủ Nhật" };
                questionsCreatedData = new List<int> { 120, 150, 180, 140, 210, 190, 240 };
                examsTakenData = new List<int> { 320, 380, 450, 410, 520, 490, 610 };
            }

            var viewModel = new AdvancedAnalysisViewModel
            {
                CompletionRate = 76.0,
                CompletionRateChange = 4.2,
                ErrorQuestionRate = 2.4,
                ErrorQuestionRateChange = -0.5,
                AiQualityScore = 8.8,
                AiQualityScoreChange = 0.3,
                ChartLabels = chartLabels,
                QuestionsCreatedData = questionsCreatedData,
                ExamsTakenData = examsTakenData,
                Questions = paginatedQuestions,
                TotalWarningsCount = 12,
                SearchQuery = search,
                SelectedSubject = subject,
                SelectedWarningType = warningType,
                SelectedTimeframe = timeframe,
                CurrentPage = page,
                PageSize = pageSize,
                TotalPages = totalPages
            };

            return View("~/Areas/Admin/Views/Analysis/AdvancedAnalysis.cshtml", viewModel);
        }

        // Action API Xuất báo cáo (Export Report) giả lập
        [HttpGet]
        public IActionResult ExportReport(string timeframe)
        {
            // Trả về file báo cáo giả lập dưới dạng JSON hoặc link tải
            var reportData = new
            {
                ReportTitle = $"Báo cáo Phân tích Nâng cao - {timeframe}",
                GeneratedAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"),
                Stats = new { CompletionRate = "76%", ErrorQuestionRate = "2.4%", AiQualityScore = "8.8/10" },
                TotalWarnings = 12,
                Message = "Báo cáo của bạn đang được tải về..."
            };

            return Json(new { success = true, data = reportData, downloadUrl = "#", message = "Xuất báo cáo phân tích thành công!" });
        }

        // Action API Xem lại câu hỏi (Review) giả lập
        [HttpPost]
        public IActionResult ReviewQuestion(string questionId)
        {
            var question = _mockQuestions.FirstOrDefault(q => q.QuestionId == questionId);
            if (question != null)
            {
                return Json(new { success = true, message = $"Đã duyệt xem lại thành công câu hỏi {questionId}." });
            }
            return Json(new { success = false, message = "Không tìm thấy câu hỏi." });
        }

        // Action API Sửa Logic AI giả lập
        [HttpPost]
        public IActionResult FixAiLogic(string questionId)
        {
            var question = _mockQuestions.FirstOrDefault(q => q.QuestionId == questionId);
            if (question != null)
            {
                // Mô phỏng việc sửa logic AI nâng cao điểm chất lượng
                question.LogicScore = 9.0;
                question.WarningType = "Đã sửa logic";
                return Json(new { success = true, message = $"Đã sửa lỗi logic AI cho câu hỏi {questionId} thành công. Điểm logic mới: 9.0" });
            }
            return Json(new { success = false, message = "Không tìm thấy câu hỏi." });
        }

        // Action API Gộp lại (Merge) giả lập
        [HttpPost]
        public IActionResult MergeQuestions(string questionId)
        {
            var question = _mockQuestions.FirstOrDefault(q => q.QuestionId == questionId);
            if (question != null)
            {
                return Json(new { success = true, message = $"Đã thực hiện gộp câu hỏi {questionId} bị trùng lặp thành công." });
            }
            return Json(new { success = false, message = "Không tìm thấy câu hỏi." });
        }
    }
}
