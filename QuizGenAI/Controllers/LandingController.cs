using Microsoft.AspNetCore.Mvc;

namespace QuizGenAI.Controllers
{
    public class LandingController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
