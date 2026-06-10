using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace QuizGenAI.Controllers
{
    [Authorize]
    public class ExploreController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
