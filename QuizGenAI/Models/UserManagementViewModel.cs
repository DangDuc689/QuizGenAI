using System;
using System.Collections.Generic;

namespace QuizGenAI.Models
{
    public class UserManagementViewModel
    {
        // Thống kê
        public int TotalUsers { get; set; }
        public int ActiveUsers { get; set; }
        public int NewUsersToday { get; set; }

        // Danh sách người dùng trang hiện tại
        public List<UserItemViewModel> Users { get; set; } = new List<UserItemViewModel>();

        // Bộ lọc & Phân trang hiện tại
        public string? SearchQuery { get; set; }
        public string? SelectedRole { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    public class UserItemViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty; // "Học sinh" hoặc "Quản trị viên"
        public DateTime JoinedDate { get; set; }
        public bool IsActive { get; set; }
    }
}
