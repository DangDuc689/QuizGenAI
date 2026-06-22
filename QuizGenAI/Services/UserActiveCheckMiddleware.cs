using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QuizGenAI.Models;
using System.Linq;
using System.Threading.Tasks;

namespace QuizGenAI.Services
{
    public class UserActiveCheckMiddleware
    {
        private readonly RequestDelegate _next;

        public UserActiveCheckMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, ApplicationDbContext dbContext, SignInManager<ApplicationUser> signInManager)
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var userId = signInManager.UserManager.GetUserId(context.User);
                if (!string.IsNullOrEmpty(userId))
                {
                    var isActive = await dbContext.Users
                        .Where(u => u.Id == userId)
                        .Select(u => u.IsActive)
                        .FirstOrDefaultAsync();

                    // If user is locked/inactive (or not found), sign them out immediately
                    if (!isActive)
                    {
                        await signInManager.SignOutAsync();
                        context.Response.Redirect("/Identity/Account/Login?locked=true");
                        return;
                    }
                }
            }

            await _next(context);
        }
    }
}
