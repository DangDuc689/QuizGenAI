using Microsoft.AspNetCore.Mvc;

namespace QuizGenAI.Controllers
{
    public class ExamController : Controller
    {
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
    }
}
