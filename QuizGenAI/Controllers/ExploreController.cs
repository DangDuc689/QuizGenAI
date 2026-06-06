using Microsoft.AspNetCore.Mvc;

namespace QuizGenAI.Controllers
{
    public class ExploreController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
