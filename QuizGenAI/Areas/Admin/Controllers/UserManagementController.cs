using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
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
    public class UserManagementController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _context;

        public UserManagementController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            ApplicationDbContext context)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _context = context;
        }

        // Action hiển thị trang quản lý người dùng chính
        public async Task<IActionResult> Index(string search, string role, int page = 1)
        {
            ViewData["Title"] = "Quản lý người dùng";
            ViewData["ActivePage"] = "UserManagement";

            // Query users and their roles using EF Core Join
            var query = from user in _context.Users
                        join userRole in _context.UserRoles on user.Id equals userRole.UserId into ur
                        from userRole in ur.DefaultIfEmpty()
                        join identityRole in _context.Roles on userRole.RoleId equals identityRole.Id into r
                        from identityRole in r.DefaultIfEmpty()
                        select new
                        {
                            user.Id,
                            user.FullName,
                            user.Email,
                            user.CreatedAt,
                            user.IsActive,
                            RoleName = identityRole.Name
                        };

            // Lọc theo từ khóa tìm kiếm (họ tên hoặc email)
            if (!string.IsNullOrEmpty(search))
            {
                var lowerSearch = search.ToLower();
                query = query.Where(u => u.FullName.ToLower().Contains(lowerSearch) || (u.Email != null && u.Email.ToLower().Contains(lowerSearch)));
            }

            // Lọc theo vai trò ("Người dùng" -> SD.Role_User, "Quản trị viên" -> SD.Role_Admin)
            if (!string.IsNullOrEmpty(role))
            {
                var dbRoleName = role == "Quản trị viên" ? SD.Role_Admin : SD.Role_User;
                if (dbRoleName == SD.Role_User)
                {
                    query = query.Where(u => u.RoleName == SD.Role_User || string.IsNullOrEmpty(u.RoleName));
                }
                else
                {
                    query = query.Where(u => u.RoleName == dbRoleName);
                }
            }

            // Phân trang
            int pageSize = 10;
            int totalItems = await query.CountAsync();
            int totalPages = (int)Math.Ceiling((double)totalItems / pageSize);
            if (totalPages == 0) totalPages = 1;

            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            var dbUsers = await query
                .OrderByDescending(u => u.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var paginatedUsers = dbUsers.Select(u => new UserItemViewModel
            {
                Id = u.Id,
                FullName = u.FullName,
                Email = u.Email ?? "",
                Role = u.RoleName == SD.Role_Admin ? "Quản trị viên" : "Người dùng",
                JoinedDate = u.CreatedAt,
                IsActive = u.IsActive
            }).ToList();

            // Tổng hợp ViewModel
            var viewModel = new UserManagementViewModel
            {
                TotalUsers = await _context.Users.CountAsync(),
                ActiveUsers = await _context.Users.CountAsync(u => u.IsActive),
                NewUsersToday = await _context.Users.CountAsync(u => u.CreatedAt >= DateTime.UtcNow.Date),
                Users = paginatedUsers,
                SearchQuery = search,
                SelectedRole = role,
                CurrentPage = page,
                PageSize = pageSize,
                TotalPages = totalPages
            };

            return View("~/Areas/Admin/Views/Management/UserManagement.cshtml", viewModel);
        }

        // Action Tìm kiếm (trả về JSON kết quả)
        [HttpGet]
        public async Task<IActionResult> Search(string query)
        {
            var q = from user in _context.Users
                    join userRole in _context.UserRoles on user.Id equals userRole.UserId into ur
                    from userRole in ur.DefaultIfEmpty()
                    join identityRole in _context.Roles on userRole.RoleId equals identityRole.Id into r
                    from identityRole in r.DefaultIfEmpty()
                    select new
                    {
                        user.Id,
                        user.FullName,
                        user.Email,
                        user.CreatedAt,
                        user.IsActive,
                        RoleName = identityRole.Name
                    };

            if (!string.IsNullOrEmpty(query))
            {
                var lowerQuery = query.ToLower();
                q = q.Where(u => u.FullName.ToLower().Contains(lowerQuery) || (u.Email != null && u.Email.ToLower().Contains(lowerQuery)));
            }

            var result = await q.ToListAsync();
            var list = result.Select(u => new UserItemViewModel
            {
                Id = u.Id,
                FullName = u.FullName,
                Email = u.Email ?? "",
                Role = u.RoleName == SD.Role_Admin ? "Quản trị viên" : "Người dùng",
                JoinedDate = u.CreatedAt,
                IsActive = u.IsActive
            }).ToList();

            return Json(list);
        }

        // Action Lọc vai trò (trả về JSON kết quả)
        [HttpGet]
        public async Task<IActionResult> Filter(string role)
        {
            var q = from user in _context.Users
                    join userRole in _context.UserRoles on user.Id equals userRole.UserId into ur
                    from userRole in ur.DefaultIfEmpty()
                    join identityRole in _context.Roles on userRole.RoleId equals identityRole.Id into r
                    from identityRole in r.DefaultIfEmpty()
                    select new
                    {
                        user.Id,
                        user.FullName,
                        user.Email,
                        user.CreatedAt,
                        user.IsActive,
                        RoleName = identityRole.Name
                    };

            if (!string.IsNullOrEmpty(role))
            {
                var dbRoleName = role == "Quản trị viên" ? SD.Role_Admin : SD.Role_User;
                if (dbRoleName == SD.Role_User)
                {
                    q = q.Where(u => u.RoleName == SD.Role_User || string.IsNullOrEmpty(u.RoleName));
                }
                else
                {
                    q = q.Where(u => u.RoleName == dbRoleName);
                }
            }

            var result = await q.ToListAsync();
            var list = result.Select(u => new UserItemViewModel
            {
                Id = u.Id,
                FullName = u.FullName,
                Email = u.Email ?? "",
                Role = u.RoleName == SD.Role_Admin ? "Quản trị viên" : "Người dùng",
                JoinedDate = u.CreatedAt,
                IsActive = u.IsActive
            }).ToList();

            return Json(list);
        }

        // Action Khóa/Mở khóa tài khoản
        [HttpPost]
        public async Task<IActionResult> ToggleLock(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                user.IsActive = !user.IsActive;
                user.LockedAt = user.IsActive ? null : DateTime.UtcNow;
                var result = await _userManager.UpdateAsync(user);
                if (result.Succeeded)
                {
                    return Json(new 
                    { 
                        success = true, 
                        isActive = user.IsActive, 
                        message = $"Đã {(user.IsActive ? "mở khóa" : "khóa")} tài khoản {user.FullName} thành công." 
                    });
                }
                return Json(new { success = false, message = "Lỗi khi cập nhật trạng thái người dùng." });
            }
            return Json(new { success = false, message = "Không tìm thấy người dùng." });
        }

        // Action Sửa thông tin tài khoản
        [HttpPost]
        public async Task<IActionResult> EditUser(string userId, string fullName, string email, string role)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(role))
                {
                    return Json(new { success = false, message = "Vui lòng điền đầy đủ các thông tin." });
                }

                user.FullName = fullName;
                user.Email = email;
                user.NormalizedEmail = email.ToUpperInvariant();
                user.UserName = email;
                user.NormalizedUserName = email.ToUpperInvariant();

                var updateResult = await _userManager.UpdateAsync(user);
                if (!updateResult.Succeeded)
                {
                    return Json(new { success = false, message = "Lỗi khi cập nhật thông tin người dùng." });
                }

                // Cập nhật vai trò (Role)
                var currentRoles = await _userManager.GetRolesAsync(user);
                var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
                if (!removeResult.Succeeded)
                {
                    return Json(new { success = false, message = "Lỗi khi xóa vai trò hiện tại." });
                }

                var targetRole = role == "Quản trị viên" ? SD.Role_Admin : SD.Role_User;
                if (!await _roleManager.RoleExistsAsync(targetRole))
                {
                    await _roleManager.CreateAsync(new IdentityRole(targetRole));
                }

                var addResult = await _userManager.AddToRoleAsync(user, targetRole);
                if (!addResult.Succeeded)
                {
                    return Json(new { success = false, message = "Lỗi khi gán vai trò mới." });
                }

                return Json(new 
                { 
                    success = true, 
                    message = $"Đã cập nhật thông tin người dùng {user.FullName} thành công.",
                    updatedUser = new UserItemViewModel
                    {
                        Id = user.Id,
                        FullName = user.FullName,
                        Email = user.Email,
                        Role = role,
                        JoinedDate = user.CreatedAt,
                        IsActive = user.IsActive
                    }
                });
            }
            return Json(new { success = false, message = "Không tìm thấy người dùng." });
        }
    }
}
