using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuizGenAI.Models;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.AspNetCore.Authorization;
using QuizGenAI.Services;

namespace QuizGenAI.Controllers
{
    [Authorize]
    public class DocumentsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly DocxExtractionService _docxExtractionService;
        private readonly PdfExtractionService _pdfExtractionService;
        private readonly UrlExtractionService _urlExtractionService;

        public DocumentsController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            DocxExtractionService docxExtractionService,
            PdfExtractionService pdfExtractionService,
            UrlExtractionService urlExtractionService)
        {
            _context = context;
            _userManager = userManager;
            _docxExtractionService = docxExtractionService;
            _pdfExtractionService = pdfExtractionService;
            _urlExtractionService = urlExtractionService;
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
            string? Description,
            string? ExtractedText,
            string? SourceUrl,
            string SourceType,
            IFormFile? UploadedFile)
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

            if (SourceType == "URL" &&
                (!Uri.TryCreate(SourceUrl?.Trim(), UriKind.Absolute, out var sourceUri) ||
                 (sourceUri.Scheme != Uri.UriSchemeHttp && sourceUri.Scheme != Uri.UriSchemeHttps)))
            {
                ModelState.AddModelError("SourceUrl", "URL không hợp lệ. Vui lòng nhập liên kết đầy đủ bắt đầu bằng http:// hoặc https://.");
                return View();
            }

            if (SourceType == "File" && (UploadedFile == null || UploadedFile.Length == 0))
            {
                ModelState.AddModelError("UploadedFile", "Vui lòng chọn file tài liệu.");
                return View();
            }

            if (SourceType == "File" && UploadedFile != null)
            {
                var allowedExtensions = new[] { ".pdf", ".docx", ".xlsx" };
                var fileExtension = Path.GetExtension(UploadedFile.FileName).ToLowerInvariant();

                if (!allowedExtensions.Contains(fileExtension))
                {
                    ModelState.AddModelError("UploadedFile", "Chỉ hỗ trợ file PDF, DOCX hoặc XLSX.");
                    return View();
                }
            }

            var userId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account");
            }

            var documentSourceType = SourceType switch
            {
                "URL" => DocumentSourceType.URL,
                "File" when UploadedFile != null
                    && Path.GetExtension(UploadedFile.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                    => DocumentSourceType.PDF,
                "File" when UploadedFile != null
                    && Path.GetExtension(UploadedFile.FileName).Equals(".docx", StringComparison.OrdinalIgnoreCase)
                    => DocumentSourceType.Word,
                "File" when UploadedFile != null
                    && Path.GetExtension(UploadedFile.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                    => DocumentSourceType.Excel,
                _ => DocumentSourceType.PastedText
            };

            string? filePath = null;
            long fileSizeBytes = 0;
            int pageCount = 0;
            string? extractedTextFromFile = null;
            string? extractedTextFromUrl = null;

            if (SourceType == "File" && UploadedFile != null)
            {
                var uploadsFolder = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    "uploads",
                    "documents");

                Directory.CreateDirectory(uploadsFolder);

                var fileExtension = Path.GetExtension(UploadedFile.FileName);
                var safeFileName = $"{Guid.NewGuid()}{fileExtension}";
                var fullPath = Path.Combine(uploadsFolder, safeFileName);

                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await UploadedFile.CopyToAsync(stream);
                }

                filePath = $"/uploads/documents/{safeFileName}";
                fileSizeBytes = UploadedFile.Length;

                if (documentSourceType == DocumentSourceType.PDF)
                {
                    var pdfExtraction = await _pdfExtractionService.ExtractAsync(
                        fullPath,
                        HttpContext.RequestAborted);

                    pageCount = pdfExtraction.PageCount;
                    extractedTextFromFile = pdfExtraction.Text;
                }

                if (documentSourceType == DocumentSourceType.Word)
                {
                    var docxExtraction = await _docxExtractionService.ExtractTextFromDocxAsync(
                        fullPath,
                        HttpContext.RequestAborted);

                    extractedTextFromFile = docxExtraction.ExtractedText;
                    pageCount = GetWordPageCountFromMetadata(fullPath);     
                }
            }

            if (documentSourceType == DocumentSourceType.URL)
            {
                var urlExtraction = await _urlExtractionService.ExtractAsync(
                    SourceUrl,
                    HttpContext.RequestAborted);

                if (urlExtraction.Success)
                {
                    extractedTextFromUrl = urlExtraction.ExtractedText?.Trim();
                }
            }

            var document = new Document
            {
                Title = Title.Trim(),
                Description = Description?.Trim(),
                SourceType = documentSourceType,
                ExtractedText = documentSourceType == DocumentSourceType.PastedText
                    ? ExtractedText?.Trim()
                    : documentSourceType == DocumentSourceType.URL
                        ? extractedTextFromUrl
                        : extractedTextFromFile,
                SourceUrl = documentSourceType == DocumentSourceType.URL
                    ? SourceUrl?.Trim()
                    : null,
                FilePath = filePath,
                CreatedAt = DateTime.UtcNow,
                PageCount = pageCount > 0 ? pageCount : (SourceType == "File" ? 1 : 0),
                FileSizeBytes = fileSizeBytes,
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TogglePublic(int quizSetId)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "Bạn chưa đăng nhập." });
            }

            var quizSet = await _context.QuizSets.FirstOrDefaultAsync(qs => qs.Id == quizSetId);
            if (quizSet == null)
            {
                return Json(new { success = false, message = "Không tìm thấy bộ đề." });
            }

            if (quizSet.UserId != userId)
            {
                return Json(new { success = false, message = "Bạn không có quyền chỉnh sửa bộ đề này." });
            }

            quizSet.IsPublic = !quizSet.IsPublic;
            quizSet.UpdatedAt = DateTime.UtcNow;

            _context.QuizSets.Update(quizSet);
            await _context.SaveChangesAsync();

            return Json(new { success = true, isPublic = quizSet.IsPublic, message = "Cập nhật trạng thái thành công." });
        }

        private static string? ExtractTextFromWord(string filePath)
        {
            using var wordDocument = WordprocessingDocument.Open(filePath, false);

            var mainPart = wordDocument.MainDocumentPart;

            if (mainPart?.Document?.Body == null)
            {
                return null;
            }

            var body = mainPart.Document.Body;

            var textBuilder = new StringBuilder();

            foreach (var text in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>())
            {
                if (!string.IsNullOrWhiteSpace(text.Text))
                {
                    textBuilder.Append(text.Text);
                    textBuilder.Append(' ');
                }
            }

            var extractedText = textBuilder.ToString().Trim();

            return string.IsNullOrWhiteSpace(extractedText)
                ? null
                : extractedText;
        }

        private static int GetWordPageCountFromMetadata(string filePath)
        {
            using var wordDocument = WordprocessingDocument.Open(filePath, false);

            var pagesText = wordDocument.ExtendedFilePropertiesPart?
                .Properties?
                .Pages?
                .InnerText;

            if (int.TryParse(pagesText, out var pageCount) && pageCount > 0)
            {
                return pageCount;
            }

            return 1;
        }
    }
}
