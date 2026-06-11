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
    public class UserManagementController : Controller
    {
        // Danh sách người dùng giả lập (Mock Data)
        private static readonly List<UserItemViewModel> _mockUsers = new()
        {
            new UserItemViewModel { Id = "1", FullName = "Nguyễn Văn A", Email = "nguyenwana@gmail.com", Role = "Người dùng", JoinedDate = DateTime.Parse("2026-05-10"), IsActive = true },
            new UserItemViewModel { Id = "2", FullName = "Trần Thị B", Email = "tranthib@gmail.com", Role = "Người dùng", JoinedDate = DateTime.Parse("2026-05-11"), IsActive = true },
            new UserItemViewModel { Id = "3", FullName = "Lê Văn C", Email = "levanc@gmail.com", Role = "Quản trị viên", JoinedDate = DateTime.Parse("2026-01-01"), IsActive = true },
            new UserItemViewModel { Id = "4", FullName = "Phạm Minh D", Email = "phamminhd@gmail.com", Role = "Người dùng", JoinedDate = DateTime.Parse("2026-06-02"), IsActive = false },
            new UserItemViewModel { Id = "5", FullName = "Hoàng Anh E", Email = "hoanganhe@gmail.com", Role = "Người dùng", JoinedDate = DateTime.Parse("2026-06-03"), IsActive = true },
            new UserItemViewModel { Id = "6", FullName = "Đỗ Thị F", Email = "dothif@gmail.com", Role = "Quản trị viên", JoinedDate = DateTime.Parse("2026-02-15"), IsActive = true },
            new UserItemViewModel { Id = "7", FullName = "Bùi Văn G", Email = "buivang@gmail.com", Role = "Người dùng", JoinedDate = DateTime.Parse("2026-04-20"), IsActive = false },
            new UserItemViewModel { Id = "8", FullName = "Ngô Thị H", Email = "ngothih@gmail.com", Role = "Người dùng", JoinedDate = DateTime.Parse("2026-04-25"), IsActive = true },
            new UserItemViewModel { Id = "9", FullName = "Dương Văn I", Email = "duongvani@gmail.com", Role = "Người dùng", JoinedDate = DateTime.Parse("2026-05-01"), IsActive = true },
            new UserItemViewModel { Id = "10", FullName = "Vũ Thị K", Email = "vuthik@gmail.com", Role = "Người dùng", JoinedDate = DateTime.Parse("2026-05-05"), IsActive = true },
            new UserItemViewModel { Id = "11", FullName = "Phan Văn L", Email = "phanvanl@gmail.com", Role = "Người dùng", JoinedDate = DateTime.Parse("2026-06-08"), IsActive = true },
            new UserItemViewModel { Id = "12", FullName = "Lý Thị M", Email = "lythim@gmail.com", Role = "Người dùng", JoinedDate = DateTime.Parse("2026-06-11"), IsActive = true }
        };

        // Action hiển thị trang quản lý người dùng chính
        public IActionResult Index(string search, string role, int page = 1)
        {
            ViewData["Title"] = "Quản lý người dùng";
            ViewData["ActivePage"] = "UserManagement";

            var filtered = _mockUsers.AsQueryable();

            // Lọc theo từ khóa tìm kiếm (họ tên hoặc email)
            if (!string.IsNullOrEmpty(search))
            {
                var lowerSearch = search.ToLower();
                filtered = filtered.Where(u => u.FullName.ToLower().Contains(lowerSearch) || u.Email.ToLower().Contains(lowerSearch));
            }

            // Lọc theo vai trò
            if (!string.IsNullOrEmpty(role))
            {
                filtered = filtered.Where(u => u.Role.Equals(role, StringComparison.OrdinalIgnoreCase));
            }

            // Thực hiện phân trang
            int pageSize = 5;
            int totalItems = filtered.Count();
            int totalPages = (int)Math.Ceiling((double)totalItems / pageSize);
            if (totalPages == 0) totalPages = 1;
            
            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            var paginatedUsers = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            // Tổng hợp ViewModel
            var viewModel = new UserManagementViewModel
            {
                // Thống kê cứng theo yêu cầu
                TotalUsers = 12482,
                ActiveUsers = 842,
                NewUsersToday = 156,
                Users = paginatedUsers,
                SearchQuery = search,
                SelectedRole = role,
                CurrentPage = page,
                PageSize = pageSize,
                TotalPages = totalPages
            };

            return View("~/Areas/Admin/Views/Management/UserManagement.cshtml", viewModel);
        }

        // Action Tìm kiếm giả lập (trả về JSON kết quả)
        [HttpGet]
        public IActionResult Search(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return Json(_mockUsers);
            }
            var lowerQuery = query.ToLower();
            var result = _mockUsers.Where(u => u.FullName.ToLower().Contains(lowerQuery) || u.Email.ToLower().Contains(lowerQuery)).ToList();
            return Json(result);
        }

        // Action Lọc vai trò giả lập (trả về JSON kết quả)
        [HttpGet]
        public IActionResult Filter(string role)
        {
            if (string.IsNullOrEmpty(role))
            {
                return Json(_mockUsers);
            }
            var result = _mockUsers.Where(u => u.Role.Equals(role, StringComparison.OrdinalIgnoreCase)).ToList();
            return Json(result);
        }

        // Action Khóa/Mở khóa tài khoản giả lập
        [HttpPost]
        public IActionResult ToggleLock(string userId)
        {
            var user = _mockUsers.FirstOrDefault(u => u.Id == userId);
            if (user != null)
            {
                user.IsActive = !user.IsActive;
                return Json(new 
                { 
                    success = true, 
                    isActive = user.IsActive, 
                    message = $"Đã {(user.IsActive ? "mở khóa" : "khóa")} tài khoản {user.FullName} thành công." 
                });
            }
            return Json(new { success = false, message = "Không tìm thấy người dùng." });
        }

        // Action Sửa thông tin giả lập
        [HttpPost]
        public IActionResult EditUser(string userId, string fullName, string email, string role)
        {
            var user = _mockUsers.FirstOrDefault(u => u.Id == userId);
            if (user != null)
            {
                if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(role))
                {
                    return Json(new { success = false, message = "Vui lòng điền đầy đủ các thông tin." });
                }

                user.FullName = fullName;
                user.Email = email;
                user.Role = role;

                return Json(new 
                { 
                    success = true, 
                    message = $"Đã cập nhật thông tin người dùng {user.FullName} thành công.",
                    updatedUser = user 
                });
            }
            return Json(new { success = false, message = "Không tìm thấy người dùng." });
        }
    }
}
