using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuizGenAI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QuizGenAI.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = SD.Role_Admin)]
    public class AdminDashboardController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminDashboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Action hiển thị trang tổng quan chính
        public async Task<IActionResult> Index(string timeframe = "today")
        {
            ViewData["Title"] = "Tổng quan Hệ thống";
            ViewData["ActivePage"] = "AdminDashboard";

            var stats = await GetDashboardStatsAsync(timeframe);

            // Dữ liệu tài liệu mới nhất từ Db
            var recentDbDocs = await _context.Documents
                .Include(d => d.User)
                .Include(d => d.QuizSets)
                .ThenInclude(qs => qs.Questions)
                .OrderByDescending(d => d.CreatedAt)
                .Take(5)
                .ToListAsync();

            var recentDocuments = recentDbDocs.Select(d => new AdminRecentDocumentItemViewModel
            {
                Filename = d.Title,
                UploadedTime = GetRelativeTime(d.CreatedAt),
                QuestionsCount = d.QuizSets.Sum(qs => qs.Questions.Count),
                FileSize = FormatSize(d.FileSizeBytes),
                FileType = d.SourceType == DocumentSourceType.PDF ? "pdf" : "docx"
            }).ToList();

            // Phân bổ Cấp độ Bloom từ Db
            var totalQCount = await _context.Questions.CountAsync();
            int rememberPercent = 40;
            int understandPercent = 40;
            int applyPercent = 20;

            if (totalQCount > 0)
            {
                var rememberCount = await _context.Questions.CountAsync(q => q.BloomLevel == BloomLevel.Remember);
                var understandCount = await _context.Questions.CountAsync(q => q.BloomLevel == BloomLevel.Understand);
                var applyCount = await _context.Questions.CountAsync(q => q.BloomLevel == BloomLevel.Apply);

                rememberPercent = (int)Math.Round((double)rememberCount * 100 / totalQCount);
                understandPercent = (int)Math.Round((double)understandCount * 100 / totalQCount);
                applyPercent = 100 - rememberPercent - understandPercent;
            }

            // Dữ liệu Log hệ thống động dựa trên dữ liệu thực tế
            var systemLogs = new List<AdminSystemLogItemViewModel>();

            var latestUser = await _context.Users.OrderByDescending(u => u.CreatedAt).FirstOrDefaultAsync();
            if (latestUser != null)
            {
                systemLogs.Add(new AdminSystemLogItemViewModel
                {
                    LogType = "Success",
                    Message = $"Người dùng mới \"{latestUser.FullName}\" đã đăng ký tài khoản thành công.",
                    TimeString = GetRelativeTime(latestUser.CreatedAt)
                });
            }

            var latestDoc = await _context.Documents.Include(d => d.User).OrderByDescending(d => d.CreatedAt).FirstOrDefaultAsync();
            if (latestDoc != null)
            {
                systemLogs.Add(new AdminSystemLogItemViewModel
                {
                    LogType = "Info",
                    Message = $"Tài liệu mới \"{latestDoc.Title}\" đã được tải lên bởi {latestDoc.User?.FullName ?? "Người dùng ẩn danh"}.",
                    TimeString = GetRelativeTime(latestDoc.CreatedAt)
                });
            }

            var lockedUser = await _context.Users
                .Where(u => !u.IsActive && u.LockedAt != null)
                .OrderByDescending(u => u.LockedAt)
                .FirstOrDefaultAsync();

            if (lockedUser == null)
            {
                // Fallback to CreatedAt if no users have LockedAt set yet (e.g. existing database)
                lockedUser = await _context.Users
                    .Where(u => !u.IsActive)
                    .OrderByDescending(u => u.CreatedAt)
                    .FirstOrDefaultAsync();
            }

            if (lockedUser != null)
            {
                systemLogs.Add(new AdminSystemLogItemViewModel
                {
                    LogType = "Warning",
                    Message = $"Tài khoản của người dùng \"{lockedUser.FullName}\" hiện đang bị khóa.",
                    TimeString = GetRelativeTime(lockedUser.LockedAt ?? lockedUser.CreatedAt)
                });
            }
            else
            {
                systemLogs.Add(new AdminSystemLogItemViewModel
                {
                    LogType = "Warning",
                    Message = "Hệ thống hoạt động bình thường, không phát hiện sự cố bảo mật.",
                    TimeString = "Vừa xong"
                });
            }

            var viewModel = new AdminDashboardViewModel
            {
                TotalUsers = stats.totalUsers,
                NewDocuments = stats.newDocuments,
                QuestionsCreated = stats.questionsCreated,
                SuccessRate = stats.successRate,
                RecentDocuments = recentDocuments,
                BloomRememberPercent = rememberPercent,
                BloomUnderstandPercent = understandPercent,
                BloomApplyPercent = applyPercent,
                SystemLogs = systemLogs,
                SelectedTimeframe = timeframe
            };

            return View("~/Areas/Admin/Views/AdminDashboard/Index.cshtml", viewModel);
        }

        // AJAX API lọc thời gian (trả về Json)
        [HttpGet]
        public async Task<IActionResult> FilterTimeframe(string timeframe)
        {
            var stats = await GetDashboardStatsAsync(timeframe);

            return Json(new
            {
                success = true,
                totalUsers = stats.totalUsers,
                newDocuments = stats.newDocuments,
                questionsCreated = stats.questionsCreated,
                successRate = stats.successRate
            });
        }

        // Action API Xem tất cả tài liệu
        [HttpGet]
        public IActionResult ViewAllDocuments()
        {
            return Json(new { success = true, redirectUrl = "/Admin/ResourceManager", message = "Đang chuyển hướng sang Quản lý tài nguyên..." });
        }

        private async Task<(int totalUsers, int newDocuments, int questionsCreated, double successRate)> GetDashboardStatsAsync(string timeframe)
        {
            // Lấy thời gian hiện tại theo múi giờ Việt Nam (UTC+7)
            var localNow = DateTime.UtcNow.AddHours(7);
            DateTime localStartDate;

            switch (timeframe)
            {
                case "7days":
                    localStartDate = localNow.Date.AddDays(-7);
                    break;
                case "30days":
                    localStartDate = localNow.Date.AddDays(-30);
                    break;
                default:
                    localStartDate = localNow.Date; // "today"
                    break;
            }

            // Quy đổi ngược lại UTC để truy vấn cơ sở dữ liệu
            DateTime startDate = localStartDate.AddHours(-7);

            int totalUsers = await _context.Users.CountAsync();
            int newDocuments = await _context.Documents.CountAsync(d => d.CreatedAt >= startDate);
            int questionsCreated = await _context.Questions.CountAsync(q => q.CreatedAt >= startDate);

            var finishedSessions = await _context.ExamSessions
                .Where(es => es.Status == ExamSessionStatus.Completed && es.StartedAt >= startDate)
                .ToListAsync();

            double successRate = 100.0;
            if (finishedSessions.Any())
            {
                successRate = finishedSessions.Average(es => es.TotalQuestions > 0 ? (double)es.CorrectAnswers * 100.0 / es.TotalQuestions : 100.0);
            }
            else
            {
                var overallSessions = await _context.ExamSessions
                    .Where(es => es.Status == ExamSessionStatus.Completed)
                    .ToListAsync();
                if (overallSessions.Any())
                {
                    successRate = overallSessions.Average(es => es.TotalQuestions > 0 ? (double)es.CorrectAnswers * 100.0 / es.TotalQuestions : 100.0);
                }
            }
            successRate = Math.Round(successRate, 1);

            return (totalUsers, newDocuments, questionsCreated, successRate);
        }

        // Helper tính toán thời gian tương đối
        private static string GetRelativeTime(DateTime utcTime)
        {
            var elapsed = DateTime.UtcNow - utcTime;
            
            // Nếu thời gian chênh lệch bị âm nhiều (do dữ liệu cũ lưu dưới dạng Giờ địa phương - Local Time),
            // ta điều chỉnh lại khoảng thời gian so với DateTime.Now
            if (elapsed.TotalMinutes < -5)
            {
                elapsed = DateTime.Now - utcTime;
            }

            if (elapsed.TotalMinutes < 1)
                return "Vừa xong";
            if (elapsed.TotalMinutes < 60)
                return $"{(int)elapsed.TotalMinutes} phút trước";
            if (elapsed.TotalHours < 24)
                return $"{(int)elapsed.TotalHours} giờ trước";
            return $"{(int)elapsed.TotalDays} ngày trước";
        }

        // Helper định dạng kích thước file
        private static string FormatSize(long? bytes)
        {
            if (bytes == null) return "N/A";
            if (bytes.Value == 0) return "0 B";
            string[] suffixes = { "B", "KB", "MB", "GB" };
            double doubleBytes = bytes.Value;
            int i = 0;
            while (doubleBytes >= 1024 && i < suffixes.Length - 1)
            {
                doubleBytes /= 1024;
                i++;
            }
            return i == 0 
                ? $"{doubleBytes:0} B" 
                : $"{doubleBytes:0.0} {suffixes[i]}";
        }
    }
}
