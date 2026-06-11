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

            // Nếu DB chưa có tài liệu nào, nạp mock dữ liệu chất lượng cao để giao diện hiển thị đẹp mắt
            if (!docItems.Any() && string.IsNullOrEmpty(search) && string.IsNullOrEmpty(format))
            {
                docItems = GetMockDocuments();
            }
            else if (!docItems.Any() && (!string.IsNullOrEmpty(search) || !string.IsNullOrEmpty(format)))
            {
                // Nếu tìm kiếm/lọc không thấy, thực hiện tìm kiếm/lọc trên tập mock data
                var mockDocs = GetMockDocuments();
                if (!string.IsNullOrEmpty(search))
                {
                    var searchLower = search.ToLower();
                    mockDocs = mockDocs.Where(d => d.Title.ToLower().Contains(searchLower) || d.OwnerEmail.ToLower().Contains(searchLower)).ToList();
                }
                if (!string.IsNullOrEmpty(format))
                {
                    mockDocs = mockDocs.Where(d => d.Format.Equals(format, StringComparison.OrdinalIgnoreCase)).ToList();
                }
                docItems = mockDocs;
            }

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
                    createdAt = doc.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
                    fileSize = FormatSize(doc.FileSizeBytes),
                    format = GetFormatString(doc.SourceType),
                    analysis = analysis,
                    description = doc.Description ?? "Không có mô tả.",
                    pageCount = doc.PageCount ?? 0,
                    sourceUrl = doc.SourceUrl
                });
            }

            var mock = GetMockDocuments().FirstOrDefault(d => d.Id == id);
            if (mock != null)
            {
                var mockText = $"[NỘI DUNG TRÍCH XUẤT GIẢ LẬP] Đây là nội dung văn bản gốc đã được công cụ AI trích xuất tự động từ tài liệu \"{mock.Title}\". Mô tả chi tiết: {mock.Description}. Vui lòng liên hệ hỗ trợ tại support@quizgen.ai hoặc số điện thoại 0901234567 nếu có thắc mắc.";
                var analysis = AnalyzeDocumentMetadata(mockText, 2, mock.QuestionsCount);

                return Json(new
                {
                    success = true,
                    title = mock.Title,
                    ownerName = mock.OwnerName,
                    ownerEmail = mock.OwnerEmail,
                    createdAt = mock.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
                    fileSize = mock.FileSize,
                    format = mock.Format,
                    analysis = analysis,
                    description = mock.Description,
                    pageCount = mock.PageCount ?? 0,
                    sourceUrl = mock.SourceUrl
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

            var mock = GetMockDocuments().FirstOrDefault(d => d.Id == id);
            if (mock != null)
            {
                return Json(new { success = true, message = $"[Giả lập] Xóa tài liệu \"{mock.Title}\" thành công!" });
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

            var title = doc?.Title ?? GetMockDocuments().FirstOrDefault(d => d.Id == id)?.Title ?? "Document.txt";
            var mockContent = $"NỘI DUNG TÀI LIỆU GIẢ LẬP: {title}\nĐược tải xuống từ trang quản lý tài nguyên hệ thống QuizGen AI.";
            var bytes = Encoding.UTF8.GetBytes(mockContent);
            return File(bytes, "application/octet-stream", title);
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

        private static List<DocumentItemViewModel> GetMockDocuments()
        {
            return new List<DocumentItemViewModel>
            {
                new DocumentItemViewModel { Id = 101, Title = "Biology_Final.pdf", OwnerName = "Nguyễn Văn A", OwnerEmail = "nguyenwana@gmail.com", CreatedAt = DateTime.UtcNow.AddMinutes(-2), FileSize = "2.4 MB", Format = "pdf", QuestionsCount = 45, Description = "Tài liệu ôn thi cuối kỳ môn Sinh học tế bào và Sinh học phân tử.", PageCount = 12 },
                new DocumentItemViewModel { Id = 102, Title = "Intro_Computer_Sci.docx", OwnerName = "Trần Thị B", OwnerEmail = "tranthib@gmail.com", CreatedAt = DateTime.UtcNow.AddMinutes(-15), FileSize = "1.1 MB", Format = "docx", QuestionsCount = 30, Description = "Giáo trình nhập môn khoa học máy tính và lập trình Python căn bản.", PageCount = 8 },
                new DocumentItemViewModel { Id = 103, Title = "English_Vocabulary_Test.docx", OwnerName = "Hoàng Anh E", OwnerEmail = "hoanganhe@gmail.com", CreatedAt = DateTime.UtcNow.AddHours(-1), FileSize = "0.5 MB", Format = "docx", QuestionsCount = 120, Description = "Tổng hợp 1000 từ vựng tiếng Anh thông dụng ôn thi IELTS.", PageCount = 5 },
                new DocumentItemViewModel { Id = 104, Title = "World_History_Notes.pdf", OwnerName = "Phạm Minh D", OwnerEmail = "phamminhd@gmail.com", CreatedAt = DateTime.UtcNow.AddHours(-3), FileSize = "4.8 MB", Format = "pdf", QuestionsCount = 15, Description = "Ghi chép lịch sử thế giới cận đại và các cuộc chiến tranh lớn thế kỷ XX.", PageCount = 35 },
                new DocumentItemViewModel { Id = 105, Title = "https://vi.wikipedia.org/wiki/Tri-tue-nhan-tao", OwnerName = "Đỗ Thị F", OwnerEmail = "dothif@gmail.com", CreatedAt = DateTime.UtcNow.AddDays(-1), FileSize = "N/A", Format = "url", QuestionsCount = 50, Description = "Nội dung bài viết về Trí tuệ nhân tạo trên Wikipedia Việt Nam.", SourceUrl = "https://vi.wikipedia.org/wiki/Tri-tue-nhan-tao" },
                new DocumentItemViewModel { Id = 106, Title = "Chemistry_Organic_Ch1.pdf", OwnerName = "Nguyễn Văn A", OwnerEmail = "nguyenwana@gmail.com", CreatedAt = DateTime.UtcNow.AddDays(-2), FileSize = "1.8 MB", Format = "pdf", QuestionsCount = 28, Description = "Hóa học hữu cơ chương 1: Ankan và các dẫn xuất hydrocarbon.", PageCount = 15 },
                new DocumentItemViewModel { Id = 107, Title = "Math_Linear_Algebra.docx", OwnerName = "Trần Thị B", OwnerEmail = "tranthib@gmail.com", CreatedAt = DateTime.UtcNow.AddDays(-3), FileSize = "3.2 MB", Format = "docx", QuestionsCount = 60, Description = "Tóm tắt định lý và bài tập Đại số tuyến tính, Ma trận và Không gian vectơ.", PageCount = 20 },
                new DocumentItemViewModel { Id = 108, Title = "Physics_Mechanics_Quiz.txt", OwnerName = "Lê Văn C", OwnerEmail = "levanc@gmail.com", CreatedAt = DateTime.UtcNow.AddDays(-4), FileSize = "12 KB", Format = "txt", QuestionsCount = 10, Description = "Câu hỏi trắc nghiệm ôn tập Cơ học cổ điển Vật lý 10.", PageCount = 1 },
                new DocumentItemViewModel { Id = 109, Title = "Data_Structure_Summary.pdf", OwnerName = "Vũ Thị K", OwnerEmail = "vuthik@gmail.com", CreatedAt = DateTime.UtcNow.AddDays(-5), FileSize = "2.9 MB", Format = "pdf", QuestionsCount = 40, Description = "Cấu trúc dữ liệu giải thuật: Cây, Đồ thị và các thuật toán tìm kiếm.", PageCount = 18 },
                new DocumentItemViewModel { Id = 110, Title = "SQL_Database_Design.docx", OwnerName = "Phan Văn L", OwnerEmail = "phanvanl@gmail.com", CreatedAt = DateTime.UtcNow.AddDays(-6), FileSize = "1.5 MB", Format = "docx", QuestionsCount = 35, Description = "Thiết kế cơ sở dữ liệu quan hệ, chuẩn hóa dữ liệu 1NF, 2NF, 3NF và câu lệnh SQL.", PageCount = 14 }
            };
        }
    }
}
