using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuizGenAI.Models;

namespace QuizGenAI.Controllers
{
    public class ExamController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ExamController(ApplicationDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            ViewData["ActivePage"] = "Exam";
            return View();
        }

        /// <summary>Trang kết quả hiển thị sau khi nộp bài.</summary>
        public IActionResult Result()
        {
            return View();
        }

        public IActionResult Review()
        {
            ViewData["ActivePage"] = "Review";
            return View();
        }

        public async Task<IActionResult> History()
        {
            ViewData["ActivePage"] = "History";

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var sessions = new List<ExamSession>();

            if (!string.IsNullOrEmpty(userId))
            {
                sessions = await _context.ExamSessions
                    .Include(es => es.QuizSet)
                    .Where(es => es.UserId == userId && es.Status == ExamSessionStatus.Completed)
                    .OrderByDescending(es => es.FinishedAt ?? es.StartedAt)
                    .ToListAsync();
            }

            var recentAssessments = sessions
                .Take(10)
                .Select(es => new ExamHistoryItem
                {
                    Id = es.Id,
                    Title = es.QuizSet?.Title ?? "Bài thi chưa xác định",
                    Category = es.QuizSet?.Language == OutputLanguage.English ? "English" : "Vietnamese",
                    TakenAt = es.FinishedAt ?? es.StartedAt,
                    ScorePercent = es.TotalQuestions > 0 ? (int)Math.Round(es.CorrectAnswers * 100.0 / es.TotalQuestions) : 0,
                    CorrectAnswers = es.CorrectAnswers,
                    TotalQuestions = es.TotalQuestions
                })
                .ToList();

            var averageScore = recentAssessments.Any()
                ? recentAssessments.Average(a => a.TotalQuestions > 0 ? a.ScorePercent : 0)
                : 0.0;

            var model = new ExamHistoryViewModel
            {
                AverageScorePercent = averageScore,
                PerformanceTrend = recentAssessments.Select(a => a.ScorePercent).Reverse().ToList(),
                RecentAssessments = recentAssessments
            };

            return View(model);
        }
    }
}
