using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuizGenAI.Models;
using QuizGenAI.Services;
using UglyToad.PdfPig;
using DocumentFormat.OpenXml.Packaging;

namespace QuizGenAI.Controllers
{
    [Authorize]
    public class ExamController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly GeminiService _geminiService;

        public ExamController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            GeminiService geminiService)
        {
            _context = context;
            _userManager = userManager;
            _geminiService = geminiService;
        }

        [HttpGet("Exam/Start/{id}")]
        public IActionResult Start(int id)
        {
            return RedirectToAction("Index", new { quizSetId = id });
        }

        /// <summary>Trang thi thử chính.</summary>
        public async Task<IActionResult> Index(int quizSetId)
        {
            ViewData["ActivePage"] = "Exam";

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account");
            }

            var quizSet = await _context.QuizSets
                .Include(qs => qs.Questions)
                    .ThenInclude(q => q.AnswerOptions)
                .FirstOrDefaultAsync(qs => qs.Id == quizSetId && (qs.UserId == userId || qs.IsPublic));

            if (quizSet == null)
            {
                return NotFound("Không tìm thấy bộ đề thi yêu cầu.");
            }

            // Sắp xếp câu hỏi và các đáp án theo thứ tự nhãn A, B, C, D
            foreach (var question in quizSet.Questions)
            {
                question.AnswerOptions = question.AnswerOptions.OrderBy(ao => ao.Label).ToList();
            }
            quizSet.Questions = quizSet.Questions.OrderBy(q => q.OrderIndex).ToList();

            return View(quizSet);
        }

        /// <summary>Trang kết quả hiển thị sau khi nộp bài.</summary>
        public async Task<IActionResult> Result(int sessionId)
        {
            ViewData["ActivePage"] = "History";

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account");
            }

            var session = await _context.ExamSessions
                .Include(es => es.QuizSet)
                .Include(es => es.ExamAnswers)
                    .ThenInclude(ea => ea.Question!)
                        .ThenInclude(q => q.AnswerOptions)
                .FirstOrDefaultAsync(es => es.Id == sessionId && es.UserId == userId);

            if (session == null)
            {
                return NotFound("Không tìm thấy kết quả phiên làm bài.");
            }

            // Sắp xếp câu hỏi theo thứ tự để hiển thị đúng
            session.ExamAnswers = session.ExamAnswers.OrderBy(ea => ea.Question?.OrderIndex ?? 0).ToList();
            foreach (var ans in session.ExamAnswers)
            {
                if (ans.Question != null)
                {
                    ans.Question.AnswerOptions = ans.Question.AnswerOptions.OrderBy(ao => ao.Label).ToList();
                }
            }

            return View(session);
        }

        /// <summary>Trang tạo đề ôn tập.</summary>
        public async Task<IActionResult> Review()
        {
            ViewData["ActivePage"] = "Review";

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account");
            }

            var documents = await _context.Documents
                .Where(d => d.UserId == userId)
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync();

            // Tính toán thống kê nhanh
            var totalDocuments = documents.Count;
            var totalQuizSets = await _context.QuizSets.CountAsync(qs => qs.UserId == userId);
            
            var completedSessions = await _context.ExamSessions
                .Where(es => es.UserId == userId && es.Status == ExamSessionStatus.Completed)
                .ToListAsync();
                
            var totalSessions = completedSessions.Count;
            double averageScore = 0;
            if (totalSessions > 0)
            {
                averageScore = completedSessions.Average(es => es.TotalQuestions > 0 
                    ? (double)es.CorrectAnswers * 100.0 / es.TotalQuestions 
                    : 0.0);
            }

            ViewBag.TotalDocuments = totalDocuments;
            ViewBag.TotalQuizSets = totalQuizSets;
            ViewBag.TotalSessions = totalSessions;
            ViewBag.AverageScore = averageScore;

            return View(documents);
        }

        /// <summary>Lịch sử các lần thi thử.</summary>
        public async Task<IActionResult> History()
        {
            ViewData["ActivePage"] = "History";

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var sessions = new List<ExamSession>();

            if (!string.IsNullOrEmpty(userId))
            {
                sessions = await _context.ExamSessions
                    .Include(es => es.QuizSet)
                    .Where(es => es.UserId == userId && es.Status == ExamSessionStatus.Completed)
                    .OrderByDescending(es => es.FinishedAt ?? es.StartedAt)
                    .ToListAsync();
            }

            var recentAssessments = sessions
                .Take(10)
                .Select(es => new ExamHistoryItem
                {
                    Id = es.Id,
                    Title = es.QuizSet?.Title ?? "Bài thi chưa xác định",
                    Category = es.QuizSet?.Language == OutputLanguage.English ? "English" : "Vietnamese",
                    TakenAt = es.FinishedAt ?? es.StartedAt,
                    ScorePercent = es.TotalQuestions > 0 ? (int)Math.Round(es.CorrectAnswers * 100.0 / es.TotalQuestions) : 0,
                    CorrectAnswers = es.CorrectAnswers,
                    TotalQuestions = es.TotalQuestions
                })
                .ToList();

            var averageScore = recentAssessments.Any()
                ? recentAssessments.Average(a => a.TotalQuestions > 0 ? a.ScorePercent : 0)
                : 0.0;

            var model = new ExamHistoryViewModel
            {
                AverageScorePercent = averageScore,
                PerformanceTrend = recentAssessments.Select(a => a.ScorePercent).Reverse().ToList(),
                RecentAssessments = recentAssessments
            };

            return View(model);
        }

        /// <summary>AJAX: Tải nhanh tài liệu mới tại màn hình ôn tập.</summary>
        [HttpPost]
        public async Task<IActionResult> QuickUpload(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return Json(new { success = false, message = "Không nhận được file." });
            }

            var allowedExtensions = new[] { ".pdf", ".docx", ".txt" };
            var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(fileExtension))
            {
                return Json(new { success = false, message = "Chỉ hỗ trợ file PDF, DOCX hoặc TXT." });
            }

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "Người dùng chưa đăng nhập hoặc phiên làm việc hết hạn." });
            }

            try
            {
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "documents");
                Directory.CreateDirectory(uploadsFolder);

                var safeFileName = $"{Guid.NewGuid()}{fileExtension}";
                var fullPath = Path.Combine(uploadsFolder, safeFileName);

                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                string? extractedText = null;
                int pageCount = 1;

                if (fileExtension == ".pdf")
                {
                    using var pdfDocument = PdfDocument.Open(fullPath);
                    pageCount = pdfDocument.NumberOfPages;
                    var textBuilder = new StringBuilder();
                    foreach (var page in pdfDocument.GetPages())
                    {
                        textBuilder.AppendLine(page.Text);
                    }
                    extractedText = textBuilder.ToString().Trim();
                }
                else if (fileExtension == ".docx")
                {
                    extractedText = ExtractTextFromWord(fullPath);
                    pageCount = GetWordPageCountFromMetadata(fullPath);
                }
                else if (fileExtension == ".txt")
                {
                    extractedText = await System.IO.File.ReadAllTextAsync(fullPath, Encoding.UTF8);
                    pageCount = 1;
                }

                var document = new Document
                {
                    Title = Path.GetFileNameWithoutExtension(file.FileName),
                    Description = $"Tải nhanh qua trang ôn tập ({fileExtension.ToUpper().TrimStart('.')})",
                    SourceType = fileExtension == ".pdf" ? DocumentSourceType.PDF :
                                 fileExtension == ".docx" ? DocumentSourceType.Word :
                                 DocumentSourceType.PastedText,
                    ExtractedText = extractedText,
                    FilePath = $"/uploads/documents/{safeFileName}",
                    CreatedAt = DateTime.Now,
                    PageCount = pageCount,
                    FileSizeBytes = file.Length,
                    UserId = userId
                };

                _context.Documents.Add(document);
                await _context.SaveChangesAsync();

                return Json(new { 
                    success = true, 
                    documentId = document.Id,
                    title = document.Title,
                    description = document.Description,
                    createdAt = document.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
                    sourceType = document.SourceType.ToString(),
                    pageCount = document.PageCount,
                    fileSize = FormatFileSize(document.FileSizeBytes)
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Lỗi khi xử lý file: {ex.Message}" });
            }
        }

        /// <summary>AJAX: Gọi AI sinh đề thi trắc nghiệm theo thang Bloom.</summary>
        [HttpPost]
        public async Task<IActionResult> CreateQuizSet([FromBody] CreateQuizSetModel model)
        {
            if (model == null || model.DocumentIds == null || model.DocumentIds.Count != 1)
            {
                return Json(new { success = false, message = "Vui lòng chọn duy nhất một tài liệu." });
            }

            if (string.IsNullOrWhiteSpace(model.Title))
            {
                return Json(new { success = false, message = "Vui lòng nhập tên bộ đề ôn tập." });
            }

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "Người dùng chưa đăng nhập hoặc phiên làm việc hết hạn." });
            }

            // Lấy nội dung các tài liệu
            var documents = await _context.Documents
                .Where(d => model.DocumentIds.Contains(d.Id) && d.UserId == userId)
                .ToListAsync();

            if (documents.Count == 0)
            {
                return Json(new { success = false, message = "Không tìm thấy các tài liệu đã chọn hoặc tài liệu không thuộc quyền sở hữu của bạn." });
            }

            // Gộp nội dung văn bản
            var textBuilder = new StringBuilder();
            foreach (var doc in documents)
            {
                if (!string.IsNullOrWhiteSpace(doc.ExtractedText))
                {
                    textBuilder.AppendLine(doc.ExtractedText);
                    textBuilder.AppendLine();
                }
            }

            var mergedText = textBuilder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(mergedText))
            {
                return Json(new { success = false, message = "Nội dung của các tài liệu đã chọn trống. Không thể tạo câu hỏi." });
            }

            // Giới hạn ký tự để tránh quá tải token của free API
            if (mergedText.Length > 50000)
            {
                mergedText = mergedText.Substring(0, 50000) + "... [Nội dung bị cắt bớt do quá dài]";
            }

            try
            {
                // Gọi AI sinh câu hỏi
                var generatedQuestions = await _geminiService.GenerateQuestionsAsync(
                    mergedText,
                    model.TotalQuestions,
                    model.BloomRememberPercent,
                    model.BloomUnderstandPercent,
                    model.BloomApplyPercent,
                    model.Language,
                    model.Difficulty);

                if (generatedQuestions == null || generatedQuestions.Count == 0)
                {
                    return Json(new { success = false, message = "AI không thể sinh câu hỏi cho tài liệu này. Vui lòng thử lại hoặc chọn tài liệu khác." });
                }

                // Tạo QuizSet mới
                var title = model.Title.Trim();

                var quizSet = new QuizSet
                {
                    Title = title,
                    TotalQuestions = generatedQuestions.Count,
                    BloomRememberPercent = model.BloomRememberPercent,
                    BloomUnderstandPercent = model.BloomUnderstandPercent,
                    BloomApplyPercent = model.BloomApplyPercent,
                    TimeLimitMinutes = model.TimeLimitMinutes,
                    Language = model.Language,
                    Difficulty = model.Difficulty,
                    Status = QuizSetStatus.Ready,
                    CreatedAt = DateTime.UtcNow,
                    UserId = userId,
                    DocumentId = documents.Count == 1 ? documents[0].Id : null
                };

                _context.QuizSets.Add(quizSet);
                await _context.SaveChangesAsync();

                // Lưu các Question và AnswerOption
                int orderIndex = 1;
                foreach (var gq in generatedQuestions)
                {
                    var question = new Question
                    {
                        QuizSetId = quizSet.Id,
                        Content = gq.Content,
                        BloomLevel = gq.BloomLevel == 0 ? BloomLevel.Remember :
                                     gq.BloomLevel == 1 ? BloomLevel.Understand :
                                     BloomLevel.Apply,
                        Explanation = gq.Explanation,
                        OrderIndex = orderIndex++,
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.Questions.Add(question);
                    await _context.SaveChangesAsync();

                    foreach (var go in gq.Options)
                    {
                        var option = new AnswerOption
                        {
                            QuestionId = question.Id,
                            Label = go.Label,
                            Content = go.Content,
                            IsCorrect = go.IsCorrect
                        };
                        _context.AnswerOptions.Add(option);
                    }
                }

                await _context.SaveChangesAsync();

                return Json(new { success = true, quizSetId = quizSet.Id });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Lỗi khi sinh câu hỏi từ AI: {ex.Message}" });
            }
        }

        /// <summary>AJAX: Khởi tạo phiên thi thử mới.</summary>
        [HttpPost]
        public async Task<IActionResult> StartSession(int quizSetId)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "Người dùng chưa đăng nhập hoặc phiên làm việc hết hạn." });
            }

            var quizSet = await _context.QuizSets
                .FirstOrDefaultAsync(qs => qs.Id == quizSetId && (qs.UserId == userId || qs.IsPublic));

            if (quizSet == null)
            {
                return Json(new { success = false, message = "Không tìm thấy bộ đề thi này." });
            }

            // Hủy bỏ các session InProgress cũ của bộ đề này nếu có
            var oldSessions = await _context.ExamSessions
                .Where(es => es.UserId == userId && es.QuizSetId == quizSetId && es.Status == ExamSessionStatus.InProgress)
                .ToListAsync();
            foreach (var old in oldSessions)
            {
                old.Status = ExamSessionStatus.Abandoned;
                old.FinishedAt = DateTime.UtcNow;
            }

            var session = new ExamSession
            {
                QuizSetId = quizSetId,
                UserId = userId,
                StartedAt = DateTime.UtcNow,
                Status = ExamSessionStatus.InProgress,
                TotalQuestions = await _context.Questions.CountAsync(q => q.QuizSetId == quizSetId)
            };

            _context.ExamSessions.Add(session);
            await _context.SaveChangesAsync();

            return Json(new { success = true, sessionId = session.Id });
        }

        /// <summary>AJAX: Cập nhật thời gian làm bài thực tế định kỳ.</summary>
        [HttpPost]
        public async Task<IActionResult> UpdateSessionDuration(int sessionId, int durationSeconds)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "Chưa đăng nhập." });
            }

            var session = await _context.ExamSessions
                .FirstOrDefaultAsync(es => es.Id == sessionId && es.UserId == userId);

            if (session == null)
            {
                return Json(new { success = false, message = "Không tìm thấy phiên làm bài." });
            }

            if (session.Status != ExamSessionStatus.InProgress)
            {
                return Json(new { success = false, message = "Phiên làm bài đã kết thúc hoặc không ở trạng thái làm bài." });
            }

            session.ActualDurationSeconds = durationSeconds;
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        /// <summary>AJAX: Nộp bài thi thử.</summary>
        [HttpPost]
        public async Task<IActionResult> SubmitExam([FromBody] ExamSubmissionModel model)
        {
            if (model == null)
            {
                return Json(new { success = false, message = "Dữ liệu nộp bài không hợp lệ." });
            }

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "Người dùng chưa đăng nhập hoặc phiên làm việc hết hạn." });
            }

            var session = await _context.ExamSessions
                .Include(es => es.QuizSet)
                .FirstOrDefaultAsync(es => es.Id == model.SessionId && es.UserId == userId);

            if (session == null)
            {
                return Json(new { success = false, message = "Không tìm thấy phiên làm bài này." });
            }

            if (session.Status == ExamSessionStatus.Completed)
            {
                return Json(new { success = false, message = "Bài thi này đã được nộp trước đó." });
            }

            session.Status = ExamSessionStatus.Completed;
            session.FinishedAt = DateTime.UtcNow;
            session.ActualDurationSeconds = model.ActualDurationSeconds;

            // Lấy danh sách câu hỏi và các đáp án đúng để chấm điểm
            var questions = await _context.Questions
                .Include(q => q.AnswerOptions)
                .Where(q => q.QuizSetId == session.QuizSetId)
                .ToListAsync();

            int correctCount = 0;
            int remCorrect = 0, remTotal = 0;
            int undCorrect = 0, undTotal = 0;
            int appCorrect = 0, appTotal = 0;

            foreach (var question in questions)
            {
                var userAns = model.Answers.FirstOrDefault(a => a.QuestionId == question.Id);
                var correctOption = question.AnswerOptions.FirstOrDefault(ao => ao.IsCorrect);
                
                bool isCorrect = false;
                if (userAns != null && userAns.SelectedOptionId.HasValue && correctOption != null)
                {
                    isCorrect = userAns.SelectedOptionId.Value == correctOption.Id;
                }

                // Cập nhật số liệu theo Bloom
                if (question.BloomLevel == BloomLevel.Remember)
                {
                    remTotal++;
                    if (isCorrect) remCorrect++;
                }
                else if (question.BloomLevel == BloomLevel.Understand)
                {
                    undTotal++;
                    if (isCorrect) undCorrect++;
                }
                else if (question.BloomLevel == BloomLevel.Apply)
                {
                    appTotal++;
                    if (isCorrect) appCorrect++;
                }

                if (isCorrect)
                {
                    correctCount++;
                }

                // Lưu ExamAnswer
                var examAnswer = new ExamAnswer
                {
                    ExamSessionId = session.Id,
                    QuestionId = question.Id,
                    SelectedAnswerOptionId = userAns?.SelectedOptionId,
                    IsCorrect = userAns?.SelectedOptionId.HasValue == true ? isCorrect : (bool?)null
                };
                _context.ExamAnswers.Add(examAnswer);
            }

            session.CorrectAnswers = correctCount;
            session.RememberCorrect = remCorrect;
            session.RememberTotal = remTotal;
            session.UnderstandCorrect = undCorrect;
            session.UnderstandTotal = undTotal;
            session.ApplyCorrect = appCorrect;
            session.ApplyTotal = appTotal;

            // Phân tích và cập nhật điểm yếu (WeakTopic) dựa trên kết quả thi này
            await UpdateWeakTopicsAsync(userId, session);

            await _context.SaveChangesAsync();

            return Json(new { 
                success = true, 
                correctAnswers = correctCount, 
                totalQuestions = session.TotalQuestions,
                scorePercent = session.TotalQuestions > 0 ? (int)Math.Round(correctCount * 100.0 / session.TotalQuestions) : 0
            });
        }

        private async Task UpdateWeakTopicsAsync(string userId, ExamSession session)
        {
            // Phân tích xem user sai nhiều nhất ở mức Bloom nào
            double remAcc = session.RememberTotal > 0 ? (double)session.RememberCorrect / session.RememberTotal : 1.0;
            double undAcc = session.UnderstandTotal > 0 ? (double)session.UnderstandCorrect / session.UnderstandTotal : 1.0;
            double appAcc = session.ApplyTotal > 0 ? (double)session.ApplyCorrect / session.ApplyTotal : 1.0;

            // Xác định mức Bloom yếu nhất có độ chính xác dưới 80%
            var levels = new List<(BloomLevel Level, double Accuracy, int Correct, int Total)>
            {
                (BloomLevel.Remember, remAcc, session.RememberCorrect, session.RememberTotal),
                (BloomLevel.Understand, undAcc, session.UnderstandCorrect, session.UnderstandTotal),
                (BloomLevel.Apply, appAcc, session.ApplyCorrect, session.ApplyTotal)
            };

            // Tìm mức Bloom có làm bài và yếu nhất
            var activeLevels = levels.Where(l => l.Total > 0).ToList();
            if (!activeLevels.Any()) return;

            var weakest = activeLevels.OrderBy(l => l.Accuracy).FirstOrDefault();

            if (weakest.Accuracy < 0.80)
            {
                var levelName = weakest.Level == BloomLevel.Remember ? "Nhận biết (Remembering)" :
                                 weakest.Level == BloomLevel.Understand ? "Thông hiểu (Understanding)" :
                                 "Vận dụng (Applying)";

                var existingWeakTopic = await _context.WeakTopics
                    .FirstOrDefaultAsync(wt => wt.UserId == userId && wt.BloomLevel == weakest.Level);

                if (existingWeakTopic == null)
                {
                    var weakTopic = new WeakTopic
                    {
                        UserId = userId,
                        TopicName = $"Kỹ năng: {levelName}",
                        BloomLevel = weakest.Level,
                        TotalAttempts = weakest.Total,
                        CorrectAttempts = weakest.Correct,
                        AccuracyRate = (decimal)(weakest.Accuracy * 100),
                        LastUpdated = DateTime.UtcNow
                    };
                    _context.WeakTopics.Add(weakTopic);
                }
                else
                {
                    existingWeakTopic.TotalAttempts += weakest.Total;
                    existingWeakTopic.CorrectAttempts += weakest.Correct;
                    existingWeakTopic.AccuracyRate = existingWeakTopic.TotalAttempts > 0 
                        ? (decimal)(existingWeakTopic.CorrectAttempts * 100.0 / existingWeakTopic.TotalAttempts)
                        : 0;
                    existingWeakTopic.LastUpdated = DateTime.UtcNow;
                    _context.WeakTopics.Update(existingWeakTopic);
                }
            }
        }

        private static string? ExtractTextFromWord(string filePath)
        {
            try
            {
                using var wordDocument = WordprocessingDocument.Open(filePath, false);
                var mainPart = wordDocument.MainDocumentPart;
                if (mainPart?.Document?.Body == null) return null;

                var textBuilder = new StringBuilder();
                foreach (var text in mainPart.Document.Body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>())
                {
                    if (!string.IsNullOrWhiteSpace(text.Text))
                    {
                        textBuilder.Append(text.Text).Append(' ');
                    }
                }
                return textBuilder.ToString().Trim();
            }
            catch
            {
                return null;
            }
        }

        private static int GetWordPageCountFromMetadata(string filePath)
        {
            try
            {
                using var wordDocument = WordprocessingDocument.Open(filePath, false);
                var pagesText = wordDocument.ExtendedFilePropertiesPart?.Properties?.Pages?.InnerText;
                if (int.TryParse(pagesText, out var pageCount) && pageCount > 0)
                {
                    return pageCount;
                }
            }
            catch { }
            return 1;
        }

        private static string FormatFileSize(long? bytes)
        {
            if (bytes == null) return "0 Bytes";
            string[] suffix = { "B", "KB", "MB", "GB" };
            double dblSvc = bytes.Value;
            int i;
            for (i = 0; i < suffix.Length && bytes >= 1024; i++, bytes /= 1024)
            {
                dblSvc = bytes.Value;
            }
            return $"{dblSvc:##.##} {suffix[i]}";
        }
    }

    // Các class truyền dữ liệu (DTO) cho AJAX requests
    public class CreateQuizSetModel
    {
        public List<int> DocumentIds { get; set; } = new List<int>();
        public string? Title { get; set; }
        public int TotalQuestions { get; set; } = 20;
        public int BloomRememberPercent { get; set; } = 40;
        public int BloomUnderstandPercent { get; set; } = 40;
        public int BloomApplyPercent { get; set; } = 20;
        public int TimeLimitMinutes { get; set; } = 30;
        public OutputLanguage Language { get; set; } = OutputLanguage.Vietnamese;
        public DifficultyLevel Difficulty { get; set; } = DifficultyLevel.Medium;
    }

    public class ExamSubmissionModel
    {
        public int SessionId { get; set; }
        public int ActualDurationSeconds { get; set; }
        public List<AnswerSubmissionModel> Answers { get; set; } = new List<AnswerSubmissionModel>();
    }

    public class AnswerSubmissionModel
    {
        public int QuestionId { get; set; }
        public int? SelectedOptionId { get; set; }
    }
}
