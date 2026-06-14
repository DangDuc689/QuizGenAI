using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuizGenAI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuizGenAI.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = SD.Role_Admin)]
    public class AdvancedAnalysisController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdvancedAnalysisController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Action hiển thị trang phân tích nâng cao
        public async Task<IActionResult> Index(string search, string timeframe = "thisweek", int page = 1)
        {
            ViewData["Title"] = "Phân tích Nâng cao";
            ViewData["ActivePage"] = "AdvancedAnalysis";

            DateTime now = DateTime.UtcNow;
            
            // Tính toán thời gian bắt đầu tuần này (Thứ 2 là ngày đầu tuần)
            int daysToSubtract = (int)now.DayOfWeek - 1;
            if (daysToSubtract < 0) daysToSubtract = 6; // Sunday is index 6 relative to Monday
            DateTime thisWeekStart = now.Date.AddDays(-daysToSubtract);
            DateTime lastWeekStart = thisWeekStart.AddDays(-7);

            // 1. Thẻ 1: Tổng số lượt thi trên hệ thống & Tỷ lệ tăng trưởng so với tuần trước
            int totalExams = await _context.ExamSessions.CountAsync();
            int thisWeekExamsCount = await _context.ExamSessions.CountAsync(es => es.StartedAt >= thisWeekStart);
            int lastWeekExamsCount = await _context.ExamSessions.CountAsync(es => es.StartedAt >= lastWeekStart && es.StartedAt < thisWeekStart);

            string totalExamsChangeText = "+0%";
            if (lastWeekExamsCount > 0)
            {
                double change = (double)(thisWeekExamsCount - lastWeekExamsCount) * 100.0 / lastWeekExamsCount;
                totalExamsChangeText = (change >= 0 ? "+" : "") + change.ToString("0") + "%";
            }
            else if (thisWeekExamsCount > 0)
            {
                totalExamsChangeText = "+" + thisWeekExamsCount + " lượt";
            }

            // 2. Thẻ 2: Điểm trung bình của tất cả học sinh (chỉ tính lượt thi đã hoàn thành)
            var completedSessionsQuery = _context.ExamSessions.Where(es => es.Status == ExamSessionStatus.Completed);
            double averageScore = 0.0;
            if (await completedSessionsQuery.AnyAsync())
            {
                averageScore = await completedSessionsQuery.AverageAsync(es => es.TotalQuestions > 0 ? (double)es.CorrectAnswers * 10.0 / es.TotalQuestions : 0.0);
            }
            averageScore = Math.Round(averageScore, 1);

            string averageScoreText = "Khá";
            if (averageScore >= 8.0) averageScoreText = "Giỏi";
            else if (averageScore >= 6.5) averageScoreText = "Khá";
            else if (averageScore >= 5.0) averageScoreText = "T.Bình";
            else if (averageScore > 0.0) averageScoreText = "Yếu";
            else averageScoreText = "N/A";

            // 3. Thẻ 3: Tỷ lệ hoàn thành bài thi
            int totalSessionsCount = await _context.ExamSessions.CountAsync();
            int completedSessionsCount = await completedSessionsQuery.CountAsync();
            double completionRate = totalSessionsCount > 0 ? (double)completedSessionsCount * 100.0 / totalSessionsCount : 0.0;
            completionRate = Math.Round(completionRate, 1);

            string completionStatusText = "Ổn định";
            if (completionRate >= 85) completionStatusText = "Tốt";
            else if (completionRate >= 70) completionStatusText = "Ổn định";
            else if (totalSessionsCount > 0) completionStatusText = "Cần cải thiện";
            else completionStatusText = "N/A";

            // 4. Biểu đồ: Số lượt thi tăng trưởng theo từng ngày trong tuần
            DateTime chartStartDate = timeframe == "lastweek" ? lastWeekStart : thisWeekStart;
            DateTime chartEndDate = chartStartDate.AddDays(7);

            var chartSessions = await _context.ExamSessions
                .Where(es => es.StartedAt >= chartStartDate && es.StartedAt < chartEndDate)
                .Select(es => new { es.StartedAt })
                .ToListAsync();

            var chartLabels = new List<string> { "Thứ 2", "Thứ 3", "Thứ 4", "Thứ 5", "Thứ 6", "Thứ 7", "Chủ Nhật" };
            var examsTakenData = new List<int> { 0, 0, 0, 0, 0, 0, 0 };

            foreach (var session in chartSessions)
            {
                // Quy đổi sang múi giờ Việt Nam (UTC+7) để nhóm đúng ngày theo lịch làm việc
                var localTime = session.StartedAt.AddHours(7);
                int dayIndex = (int)localTime.DayOfWeek - 1;
                if (dayIndex < 0) dayIndex = 6; // Sunday is 6

                if (dayIndex >= 0 && dayIndex < 7)
                {
                    examsTakenData[dayIndex]++;
                }
            }

            // 5. Bảng danh sách: Giám sát lượt làm bài gần đây (kèm tìm kiếm và phân trang)
            var recentQuery = _context.ExamSessions
                .Include(es => es.User)
                .Include(es => es.QuizSet)
                .AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                var lowerSearch = search.ToLower();
                recentQuery = recentQuery.Where(es =>
                    (es.User != null && es.User.FullName != null && es.User.FullName.ToLower().Contains(lowerSearch)) ||
                    (es.User != null && es.User.Email != null && es.User.Email.ToLower().Contains(lowerSearch)) ||
                    (es.QuizSet != null && es.QuizSet.Title != null && es.QuizSet.Title.ToLower().Contains(lowerSearch))
                );
            }

            int totalFilteredSessions = await recentQuery.CountAsync();
            int pageSize = 3; // Đồng bộ theo thiết kế giao diện (hiển thị 3 hàng mỗi trang)
            int totalPages = (int)Math.Ceiling((double)totalFilteredSessions / pageSize);
            if (totalPages <= 0) totalPages = 1;

            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            var dbSessions = await recentQuery
                .OrderByDescending(es => es.StartedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var recentViewModels = dbSessions.Select(es => new RecentExamSessionViewModel
            {
                SessionId = es.Id,
                UserFullName = es.User?.FullName ?? "Học sinh ẩn danh",
                UserEmail = es.User?.Email ?? "--",
                QuizSetTitle = es.QuizSet?.Title ?? "Bài thi chưa xác định",
                Score = es.TotalQuestions > 0 ? Math.Round((double)es.CorrectAnswers * 10.0 / es.TotalQuestions, 1) : 0.0,
                CorrectAnswers = es.CorrectAnswers,
                TotalQuestions = es.TotalQuestions,
                DurationText = FormatDuration(es.ActualDurationSeconds),
                StatusText = GetStatusText(es.Status),
                StatusCssClass = GetStatusCssClass(es.Status),
                StartedAt = es.StartedAt
            }).ToList();

            var viewModel = new AdvancedAnalysisViewModel
            {
                TotalExams = totalExams,
                TotalExamsChangeText = totalExamsChangeText,
                AverageScore = averageScore,
                AverageScoreText = averageScoreText,
                CompletionRate = completionRate,
                CompletionStatusText = completionStatusText,
                ChartLabels = chartLabels,
                ExamsTakenData = examsTakenData,
                RecentSessions = recentViewModels,
                SearchQuery = search,
                SelectedTimeframe = timeframe,
                CurrentPage = page,
                PageSize = pageSize,
                TotalSessionsCount = totalFilteredSessions,
                TotalPages = totalPages
            };

            return View("~/Areas/Admin/Views/Analysis/AdvancedAnalysis.cshtml", viewModel);
        }

        // Action xem chi tiết lượt thi
        [HttpGet]
        public IActionResult Details(int id)
        {
            // Chuyển hướng sang trang kết quả làm bài gốc
            return RedirectToAction("Result", "Exam", new { area = "", sessionId = id });
        }

        // Action API Xuất báo cáo (Mã hóa CSV UTF-8 kèm BOM giúp tương thích tốt với Microsoft Excel)
        [HttpGet]
        public async Task<IActionResult> ExportReport(string timeframe)
        {
            DateTime now = DateTime.UtcNow;
            int daysToSubtract = (int)now.DayOfWeek - 1;
            if (daysToSubtract < 0) daysToSubtract = 6;
            DateTime thisWeekStart = now.Date.AddDays(-daysToSubtract);
            DateTime startDate = timeframe == "lastweek" ? thisWeekStart.AddDays(-7) : thisWeekStart;
            DateTime endDate = startDate.AddDays(7);

            var sessions = await _context.ExamSessions
                .Include(es => es.User)
                .Include(es => es.QuizSet)
                .Where(es => es.StartedAt >= startDate && es.StartedAt < endDate)
                .OrderByDescending(es => es.StartedAt)
                .ToListAsync();

            var csvBuilder = new StringBuilder();
            // Thiết lập tiêu đề cột tiếng Việt
            csvBuilder.AppendLine("Phiên làm bài,Tên người dùng,Email,Bộ đề đã thi,Điểm số,Thời gian làm bài,Trạng thái,Thời gian bắt đầu");

            foreach (var es in sessions)
            {
                string userName = es.User?.FullName ?? "Học sinh ẩn danh";
                string email = es.User?.Email ?? "--";
                string quizTitle = es.QuizSet?.Title ?? "Bài thi chưa xác định";
                double score = es.TotalQuestions > 0 ? Math.Round((double)es.CorrectAnswers * 10.0 / es.TotalQuestions, 1) : 0.0;
                string duration = FormatDuration(es.ActualDurationSeconds);
                string status = GetStatusText(es.Status);
                string dateString = es.StartedAt.AddHours(7).ToString("dd/MM/yyyy HH:mm");

                // Tránh lỗi phá vỡ cấu trúc file CSV
                userName = EscapeCsvField(userName);
                email = EscapeCsvField(email);
                quizTitle = EscapeCsvField(quizTitle);

                csvBuilder.AppendLine($"{es.Id},{userName},{email},{quizTitle},{score},{duration},{status},{dateString}");
            }

            var fileBytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csvBuilder.ToString())).ToArray();
            string fileName = $"BaoCao_PhanTichNangCao_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            return File(fileBytes, "text/csv; charset=utf-8", fileName);
        }

        private static string FormatDuration(int? durationSeconds)
        {
            if (!durationSeconds.HasValue || durationSeconds.Value <= 0) return "00 phút 00 giây";
            int minutes = durationSeconds.Value / 60;
            int seconds = durationSeconds.Value % 60;
            return $"{minutes:00} phút {seconds:00} giây";
        }

        private static string GetStatusText(ExamSessionStatus status)
        {
            return status switch
            {
                ExamSessionStatus.InProgress => "ĐANG THI",
                ExamSessionStatus.Completed => "HOÀN THÀNH",
                ExamSessionStatus.Abandoned => "BỎ DỞ",
                _ => "CHƯA XÁC ĐỊNH"
            };
        }

        private static string GetStatusCssClass(ExamSessionStatus status)
        {
            return status switch
            {
                ExamSessionStatus.InProgress => "bg-blue-50 text-blue-600 border border-blue-200",
                ExamSessionStatus.Completed => "bg-emerald-50 text-emerald-600 border border-emerald-200",
                ExamSessionStatus.Abandoned => "bg-slate-100 text-slate-600 border border-slate-200",
                _ => "bg-slate-50 text-slate-500"
            };
        }

        private static string EscapeCsvField(string field)
        {
            if (field.Contains("\"") || field.Contains(",") || field.Contains("\n") || field.Contains("\r"))
            {
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            }
            return field;
        }
    }
}
