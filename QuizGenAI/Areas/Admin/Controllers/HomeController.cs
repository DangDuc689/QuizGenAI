using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuizGenAI.Models;

namespace QuizGenAI.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = SD.Role_Admin)]
    public class HomeController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public HomeController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        public async Task<IActionResult> Index()
        {
            ViewData["Title"] = "Admin";
            ViewData["ActivePage"] = "Admin";
            ViewData["UserCount"] = await _userManager.Users.CountAsync();
            ViewData["RoleCount"] = await _roleManager.Roles.CountAsync();

            var latestUsers = await _userManager.Users
                .OrderByDescending(user => user.CreatedAt)
                .Take(6)
                .ToListAsync();

            return View(latestUsers);
        }
    }
}
