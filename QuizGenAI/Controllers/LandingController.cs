using Microsoft.AspNetCore.Mvc;

namespace QuizGenAI.Controllers
{
    public class LandingController : Controller
    {
        public IActionResult Index()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Dashboard");
            }
            return View();
        }
    }
}
