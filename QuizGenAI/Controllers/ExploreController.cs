using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuizGenAI.Models;

namespace QuizGenAI.Controllers
{
    [Authorize]
    public class ExploreController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ExploreController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string? search)
        {
            ViewData["ActivePage"] = "Explore";
            ViewBag.SearchTerm = search;

            // Query cho danh sách các bộ đề công khai
            var query = _context.QuizSets
                .Include(qs => qs.User)
                .Include(qs => qs.Questions)
                .Include(qs => qs.Document)
                .Where(qs => qs.IsPublic);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchLower = search.Trim().ToLower();
                query = query.Where(qs => qs.Title.ToLower().Contains(searchLower));
            }

            var allPublic = await query
                .OrderByDescending(qs => qs.CreatedAt)
                .ToListAsync();

            // Featured (Nổi bật): Top 3 bộ đề công khai có lượt xem nhiều nhất
            var featured = await _context.QuizSets
                .Include(qs => qs.User)
                .Include(qs => qs.Questions)
                .Include(qs => qs.Document)
                .Where(qs => qs.IsPublic)
                .OrderByDescending(qs => qs.ViewCount)
                .Take(3)
                .ToListAsync();

            ViewBag.FeaturedQuizSets = featured;
            ViewBag.AllPublicQuizSets = allPublic;

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> IncrementView(int quizSetId)
        {
            var quizSet = await _context.QuizSets.FirstOrDefaultAsync(qs => qs.Id == quizSetId);
            if (quizSet != null && quizSet.IsPublic)
            {
                quizSet.ViewCount++;
                await _context.SaveChangesAsync();
                return Json(new { success = true, viewCount = quizSet.ViewCount });
            }
            return Json(new { success = false, message = "Không tìm thấy bộ đề công khai." });
        }
    }
}
