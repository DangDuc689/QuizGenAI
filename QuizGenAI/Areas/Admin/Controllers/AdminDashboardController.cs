using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuizGenAI.Models;
using System.Collections.Generic;

namespace QuizGenAI.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = SD.Role_Admin)]
    public class AdminDashboardController : Controller
    {
        // Action hiển thị trang tổng quan chính
        public IActionResult Index(string timeframe = "today")
        {
            ViewData["Title"] = "Tổng quan Hệ thống";
            ViewData["ActivePage"] = "AdminDashboard";

            // Thay đổi số liệu giả lập dựa trên timeframe được chọn để tạo tính năng tương tác thực tế
            int totalUsers = 12450;
            string totalUsersChange = "+12%";
            int newDocuments = 842;
            string newDocumentsChange = "+5%";
            int questionsCreated = 3120;
            string questionsCreatedChange = "Tăng trưởng";
            double successRate = 98.5;
            string successRateChange = "Ổn định";

            if (timeframe == "7days")
            {
                totalUsers = 12780;
                totalUsersChange = "+15%";
                newDocuments = 1240;
                newDocumentsChange = "+8%";
                questionsCreated = 4890;
                questionsCreatedChange = "Tăng nhanh";
                successRate = 98.8;
                successRateChange = "Cải thiện";
            }
            else if (timeframe == "30days")
            {
                totalUsers = 13900;
                totalUsersChange = "+22%";
                newDocuments = 3840;
                newDocumentsChange = "+18%";
                questionsCreated = 12450;
                questionsCreatedChange = "Bùng nổ";
                successRate = 99.1;
                successRateChange = "Tối ưu";
            }

            // Dữ liệu tài liệu mới nhất
            var recentDocuments = new List<AdminRecentDocumentItemViewModel>
            {
                new AdminRecentDocumentItemViewModel { Filename = "Biology_Final.pdf", UploadedTime = "2 phút trước", QuestionsCount = 45, FileSize = "2.4 MB", FileType = "pdf" },
                new AdminRecentDocumentItemViewModel { Filename = "Intro_Computer_Sci.docx", UploadedTime = "15 phút trước", QuestionsCount = 30, FileSize = "1.1 MB", FileType = "docx" },
                new AdminRecentDocumentItemViewModel { Filename = "English_Vocabulary_Test.docx", UploadedTime = "1 giờ trước", QuestionsCount = 120, FileSize = "0.5 MB", FileType = "docx" },
                new AdminRecentDocumentItemViewModel { Filename = "World_History_Notes.pdf", UploadedTime = "3 giờ trước", QuestionsCount = 15, FileSize = "4.8 MB", FileType = "pdf" }
            };

            // Dữ liệu Log hệ thống
            var systemLogs = new List<AdminSystemLogItemViewModel>
            {
                new AdminSystemLogItemViewModel { LogType = "Info", Message = "Quản trị viên mới \"Hoàng Nam\" đã được chỉ định vào hệ thống.", TimeString = "Hôm nay, 09:45 AM" },
                new AdminSystemLogItemViewModel { LogType = "Success", Message = "Cập nhật hệ thống phiên bản v2.4.1 hoàn tất thành công.", TimeString = "Hôm qua, 06:20 PM" },
                new AdminSystemLogItemViewModel { LogType = "Warning", Message = "Phát hiện lưu lượng truy cập cao bất thường từ IP: 192.168.1.1", TimeString = "28 Th05, 02:15 PM" }
            };

            var viewModel = new AdminDashboardViewModel
            {
                TotalUsers = totalUsers,
                TotalUsersChange = totalUsersChange,
                NewDocuments = newDocuments,
                NewDocumentsChange = newDocumentsChange,
                QuestionsCreated = questionsCreated,
                QuestionsCreatedChange = questionsCreatedChange,
                SuccessRate = successRate,
                SuccessRateChange = successRateChange,
                RecentDocuments = recentDocuments,
                BloomRememberPercent = 45,
                BloomUnderstandPercent = 32,
                BloomApplyPercent = 23,
                SystemLogs = systemLogs,
                SelectedTimeframe = timeframe
            };

            return View("~/Areas/Admin/Views/AdminDashboard/Index.cshtml", viewModel);
        }

        // AJAX API lọc thời gian (trả về Json)
        [HttpGet]
        public IActionResult FilterTimeframe(string timeframe)
        {
            int totalUsers = 12450;
            string totalUsersChange = "+12%";
            int newDocuments = 842;
            string newDocumentsChange = "+5%";
            int questionsCreated = 3120;
            string questionsCreatedChange = "Tăng trưởng";
            double successRate = 98.5;
            string successRateChange = "Ổn định";

            if (timeframe == "7days")
            {
                totalUsers = 12780;
                totalUsersChange = "+15%";
                newDocuments = 1240;
                newDocumentsChange = "+8%";
                questionsCreated = 4890;
                questionsCreatedChange = "Tăng nhanh";
                successRate = 98.8;
                successRateChange = "Cải thiện";
            }
            else if (timeframe == "30days")
            {
                totalUsers = 13900;
                totalUsersChange = "+22%";
                newDocuments = 3840;
                newDocumentsChange = "+18%";
                questionsCreated = 12450;
                questionsCreatedChange = "Bùng nổ";
                successRate = 99.1;
                successRateChange = "Tối ưu";
            }

            return Json(new
            {
                success = true,
                totalUsers,
                totalUsersChange,
                newDocuments,
                newDocumentsChange,
                questionsCreated,
                questionsCreatedChange,
                successRate,
                successRateChange
            });
        }

        // Action API Xem tất cả tài liệu (giả lập)
        [HttpGet]
        public IActionResult ViewAllDocuments()
        {
            return Json(new { success = true, redirectUrl = "/Admin/ResourceManager", message = "Đang chuyển hướng sang Quản lý tài nguyên..." });
        }
    }
}
