using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuizGenAI.Models;

namespace QuizGenAI.Controllers
{
    public class DocumentsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public DocumentsController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index(string? type)
        {
            ViewData["ActivePage"] = "Documents";
            ViewData["SelectedType"] = type;

            var query = _context.Documents.AsQueryable();

            if (!string.IsNullOrWhiteSpace(type))
            {
                var selectedType = type.ToLower();

                query = selectedType switch
                {
                    "pdf" => query.Where(d => d.SourceType == DocumentSourceType.PDF),
                    "docx" => query.Where(d => d.SourceType == DocumentSourceType.Word),
                    "word" => query.Where(d => d.SourceType == DocumentSourceType.Word),
                    "url" => query.Where(d => d.SourceType == DocumentSourceType.URL),
                    _ => query
                };
            }

            var documents = await query
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync();

            return View(documents);
        }

        public IActionResult Create()
        {
            ViewData["ActivePage"] = "Documents";
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            string Title,
            string? ExtractedText,
            string? SourceUrl,
            string SourceType)
        {
            ViewData["ActivePage"] = "Documents";

            if (string.IsNullOrWhiteSpace(Title))
            {
                ModelState.AddModelError("Title", "Vui lòng nhập tên tài liệu.");
                return View();
            }

            if (SourceType == "PastedText" && string.IsNullOrWhiteSpace(ExtractedText))
            {
                ModelState.AddModelError("ExtractedText", "Vui lòng nhập nội dung tài liệu.");
                return View();
            }

            if (SourceType == "URL" && string.IsNullOrWhiteSpace(SourceUrl))
            {
                ModelState.AddModelError("SourceUrl", "Vui lòng nhập đường dẫn URL.");
                return View();
            }

            var devEmail = "lean@quizgen.local";
            var userId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(userId))
            {
                var existingUser = await _userManager.FindByEmailAsync(devEmail);

                if (existingUser == null)
                {
                    var devUser = new ApplicationUser
                    {
                        UserName = devEmail,
                        Email = devEmail,
                        EmailConfirmed = true
                    };

                    var createUserResult = await _userManager.CreateAsync(devUser, "Dev@123456");

                    if (!createUserResult.Succeeded)
                    {
                        foreach (var error in createUserResult.Errors)
                        {
                            ModelState.AddModelError("", error.Description);
                        }

                        return View();
                    }

                    existingUser = devUser;
                }

                userId = existingUser.Id;
            }

            var documentSourceType = SourceType switch
            {
                "URL" => DocumentSourceType.URL,
                _ => DocumentSourceType.PastedText
            };

            var document = new Document
            {
                Title = Title.Trim(),
                SourceType = documentSourceType,
                ExtractedText = documentSourceType == DocumentSourceType.PastedText
                    ? ExtractedText?.Trim()
                    : SourceUrl?.Trim(),
                SourceUrl = documentSourceType == DocumentSourceType.URL
                    ? SourceUrl?.Trim()
                    : null,
                CreatedAt = DateTime.Now,
                PageCount = 0,
                FileSizeBytes = 0,
                UserId = userId
            };

            _context.Documents.Add(document);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Details(int id)
        {
            ViewData["ActivePage"] = "Documents";

            var document = await _context.Documents
                .Include(d => d.QuizSets)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (document == null)
            {
                return NotFound();
            }

            return View(document);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var document = await _context.Documents
                .Include(d => d.QuizSets)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (document == null)
            {
                return NotFound();
            }

            _context.Documents.Remove(document);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}