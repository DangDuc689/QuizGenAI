using Microsoft.AspNetCore.Mvc;

namespace QuizGenAI.Controllers
{
    public class DocumentsController : Controller
    {
        public IActionResult Index()
        {
            ViewData["ActivePage"] = "Documents";
            return View();
        }

        public IActionResult Create()
        {
            ViewData["ActivePage"] = "Documents";
            return View();
        }

        public IActionResult Details(int id)
        {
            ViewData["ActivePage"] = "Documents";
            ViewBag.DocumentId = id;
            return View();
        }
    }
}