using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuizGenAI.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace QuizGenAI.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = SD.Role_Admin)]
    public class ResourceManagerController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ResourceManagerController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Action hiển thị danh sách tài liệu
        public async Task<IActionResult> Index(string search, string format, string sortBy = "newest", int page = 1)
        {
            ViewData["Title"] = "Quản lý tài liệu";
            ViewData["ActivePage"] = "ResourceManager";

            // Fetch documents from Db
            var query = _context.Documents
                .Include(d => d.User)
                .Include(d => d.QuizSets)
                .ThenInclude(qs => qs.Questions)
                .AsQueryable();

            // Lọc theo từ khóa tìm kiếm (Tên tài liệu hoặc Email người dùng)
            if (!string.IsNullOrEmpty(search))
            {
                var searchLower = search.ToLower();
                query = query.Where(d => d.Title.ToLower().Contains(searchLower) || 
                                         (d.User != null && d.User.Email != null && d.User.Email.ToLower().Contains(searchLower)));
            }

            // Lọc theo định dạng file
            if (!string.IsNullOrEmpty(format))
            {
                if (format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(d => d.SourceType == DocumentSourceType.PDF);
                }
                else if (format.Equals("docx", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(d => d.SourceType == DocumentSourceType.Word);
                }
                else if (format.Equals("excel", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(d => d.SourceType == DocumentSourceType.Excel);
                }
                else if (format.Equals("url", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(d => d.SourceType == DocumentSourceType.URL);
                }
                else if (format.Equals("txt", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(d => d.SourceType == DocumentSourceType.PastedText);
                }
            }

            var dbDocs = await query.ToListAsync();

            // Chuyển sang ViewModel Items
            var docItems = dbDocs.Select(d => new DocumentItemViewModel
            {
                Id = d.Id,
                Title = d.Title,
                OwnerName = d.User?.FullName ?? "Người dùng ẩn danh",
                OwnerEmail = d.User?.Email ?? "no-email@quizgen.ai",
                CreatedAt = d.CreatedAt,
                FileSize = FormatSize(d.FileSizeBytes),
                Format = GetFormatString(d.SourceType),
                QuestionsCount = d.QuizSets.Sum(qs => qs.Questions.Count),
                Description = d.Description ?? "",
                SourceUrl = d.SourceUrl,
                PageCount = d.PageCount
            }).ToList();



            // Sắp xếp
            if (sortBy == "oldest")
            {
                docItems = docItems.OrderBy(d => d.CreatedAt).ToList();
            }
            else if (sortBy == "size_desc")
            {
                docItems = docItems.OrderByDescending(d => ParseSizeToBytes(d.FileSize)).ToList();
            }
            else if (sortBy == "questions_desc")
            {
                docItems = docItems.OrderByDescending(d => d.QuestionsCount).ToList();
            }
            else // newest
            {
                docItems = docItems.OrderByDescending(d => d.CreatedAt).ToList();
            }

            // Phân trang
            int pageSize = 8;
            int totalItems = docItems.Count;
            int totalPages = (int)Math.Ceiling((double)totalItems / pageSize);
            if (totalPages == 0) totalPages = 1;

            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            var pagedDocs = docItems.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var viewModel = new ResourceManagerViewModel
            {
                SearchQuery = search,
                SelectedFormat = format,
                SelectedSort = sortBy,
                CurrentPage = page,
                TotalPages = totalPages,
                PageSize = pageSize,
                TotalItems = totalItems,
                Documents = pagedDocs
            };

            return View("~/Areas/Admin/Views/ResourceManager/Index.cshtml", viewModel);
        }

        // API chi tiết tài liệu (GET)
        [HttpGet]
        public async Task<IActionResult> GetDocumentDetail(int id)
        {
            var doc = await _context.Documents
                .Include(d => d.User)
                .Include(d => d.QuizSets)
                    .ThenInclude(qs => qs.Questions)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (doc != null)
            {
                int quizSetCount = doc.QuizSets?.Count ?? 0;
                int questionCount = doc.QuizSets?.Sum(qs => qs.Questions?.Count ?? 0) ?? 0;
                var analysis = AnalyzeDocumentMetadata(doc.ExtractedText, quizSetCount, questionCount);

                return Json(new
                {
                    success = true,
                    title = doc.Title,
                    ownerName = doc.User?.FullName ?? "Người dùng ẩn danh",
                    ownerEmail = doc.User?.Email ?? "no-email@quizgen.ai",
                    createdAt = doc.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                    fileSize = FormatSize(doc.FileSizeBytes),
                    format = GetFormatString(doc.SourceType),
                    analysis = analysis,
                    description = doc.Description ?? "Không có mô tả.",
                    pageCount = doc.PageCount ?? 0,
                    sourceUrl = doc.SourceUrl
                });
            }

            return Json(new { success = false, message = "Không tìm thấy tài liệu tương ứng." });
        }

        private static DocumentMetadataAnalysis AnalyzeDocumentMetadata(string? text, int quizSetCount, int questionCount)
        {
            var analysis = new DocumentMetadataAnalysis
            {
                QuizSetCount = quizSetCount,
                QuestionCount = questionCount,
                PrivacyNote = "Nội dung gốc của tài liệu không được hiển thị tại trang Admin để bảo vệ quyền riêng tư của người dùng. Admin chỉ xem các chỉ số tổng quan phục vụ quản trị hệ thống."
            };

            if (string.IsNullOrWhiteSpace(text))
            {
                analysis.CharacterCount = 0;
                analysis.WordCount = 0;
                analysis.ParagraphCount = 0;
                analysis.DetectedLanguage = "Không xác định";
                analysis.LengthCategory = "Không có nội dung";
                analysis.HasExternalLinks = false;
                analysis.HasEmailLikeText = false;
                analysis.HasPhoneLikeText = false;
                analysis.ReadinessStatus = "Chưa sẵn sàng (Không có nội dung)";
                return analysis;
            }

            analysis.CharacterCount = text.Length;
            
            // Đếm từ
            var words = text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            analysis.WordCount = words.Length;

            // Đếm đoạn văn
            var paragraphs = text.Split(new[] { "\r\n\r\n", "\n\n", "\r\r" }, StringSplitOptions.RemoveEmptyEntries);
            analysis.ParagraphCount = paragraphs.Length;
            if (analysis.ParagraphCount == 1 && text.Contains('\n'))
            {
                analysis.ParagraphCount = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
            }

            // Ngôn ngữ ước tính qua tiếng Việt có dấu
            string vietnameseChars = "áàảãạăắằẳẵặâấầẩẫậéèẻẽẹêếềểễệíìỉĩịóòỏõọôốồổỗộơớờởỡợúùủũụưứừửữựýỳỷỹỵđÁÀẢÃẠĂẮẰẲẴẶÂẤẦẨẪẬÉÈẺẼẸÊẾỀỂỄỆÍÌỈĨỊÓÒỎÕỌÔỐỒỔỖỘƠỚỜỞỠỢÚÙỦŨỤƯỨỪỬỮỰÝỲỶỸỴĐ";
            int vnCharCount = text.Count(c => vietnameseChars.Contains(c));
            if (vnCharCount > 10)
            {
                analysis.DetectedLanguage = "Tiếng Việt";
            }
            else
            {
                analysis.DetectedLanguage = "Tiếng Anh hoặc ngôn ngữ khác";
            }

            // Phân loại độ dài
            if (analysis.WordCount < 300)
            {
                analysis.LengthCategory = "Ngắn (< 300 từ)";
            }
            else if (analysis.WordCount <= 1500)
            {
                analysis.LengthCategory = "Vừa (300 - 1500 từ)";
            }
            else if (analysis.WordCount <= 5000)
            {
                analysis.LengthCategory = "Dài (1500 - 5000 từ)";
            }
            else
            {
                analysis.LengthCategory = "Rất dài (> 5000 từ)";
            }

            // Link ngoài
            analysis.HasExternalLinks = text.Contains("http://", StringComparison.OrdinalIgnoreCase) || 
                                         text.Contains("https://", StringComparison.OrdinalIgnoreCase) || 
                                         text.Contains("www.", StringComparison.OrdinalIgnoreCase);

            // Email
            analysis.HasEmailLikeText = Regex.IsMatch(text, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");

            // Số điện thoại
            analysis.HasPhoneLikeText = Regex.IsMatch(text, @"(?:\+?\d{1,3}[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}|\b\d{9,11}\b");

            // Mức độ sẵn sàng
            if (analysis.WordCount < 50)
            {
                analysis.ReadinessStatus = "Chưa sẵn sàng (Nội dung quá ngắn)";
            }
            else if (analysis.HasPhoneLikeText || analysis.HasEmailLikeText)
            {
                analysis.ReadinessStatus = "Cần lưu ý (Chứa thông tin cá nhân)";
            }
            else
            {
                analysis.ReadinessStatus = "Sẵn sàng";
            }

            return analysis;
        }

        // API xóa tài liệu (POST)
        [HttpPost]
        public async Task<IActionResult> DeleteDocument(int id)
        {
            var doc = await _context.Documents.FindAsync(id);
            if (doc != null)
            {
                _context.Documents.Remove(doc);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = $"Xóa tài liệu \"{doc.Title}\" thành công!" });
            }

            return Json(new { success = false, message = "Không tìm thấy tài liệu để xóa." });
        }

        // API tải xuống tài liệu (GET)
        [HttpGet]
        public async Task<IActionResult> DownloadDocument(int id)
        {
            var doc = await _context.Documents.FindAsync(id);
            if (doc != null && !string.IsNullOrEmpty(doc.FilePath))
            {
                string filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", doc.FilePath.TrimStart('/'));
                if (System.IO.File.Exists(filePath))
                {
                    byte[] fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
                    return File(fileBytes, "application/octet-stream", doc.Title);
                }
            }

            return NotFound();
        }

        // Helpers
        private static string FormatSize(long? bytes)
        {
            if (bytes == null) return "N/A";
            if (bytes.Value == 0) return "0 B";
            string[] suffixes = { "B", "KB", "MB", "GB" };
            double doubleBytes = bytes.Value;
            int i = 0;
            while (doubleBytes >= 1024 && i < suffixes.Length - 1)
            {
                doubleBytes /= 1024;
                i++;
            }
            return i == 0 
                ? $"{doubleBytes:0} B" 
                : $"{doubleBytes:0.0} {suffixes[i]}";
        }

        private static string GetFormatString(DocumentSourceType type)
        {
            return type switch
            {
                DocumentSourceType.PDF => "pdf",
                DocumentSourceType.Word => "docx",
                DocumentSourceType.Excel => "xlsx",
                DocumentSourceType.URL => "url",
                _ => "txt"
            };
        }

        private static long ParseSizeToBytes(string sizeStr)
        {
            if (string.IsNullOrEmpty(sizeStr) || sizeStr == "N/A") return 0;
            try
            {
                var parts = sizeStr.Split(' ');
                if (parts.Length < 2) return 0;
                double val = double.Parse(parts[0]);
                string unit = parts[1].ToUpper();
                return unit switch
                {
                    "KB" => (long)(val * 1024),
                    "MB" => (long)(val * 1024 * 1024),
                    "GB" => (long)(val * 1024 * 1024 * 1024),
                    _ => (long)val
                };
            }
            catch
            {
                return 0;
            }
        }


    }
}
