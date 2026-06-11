using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
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
        private const int MinimumUsefulWordsForQuiz = 80;
        private const int MinimumMathSignalsForBasicQuiz = 3;
        private const int MinimumMathContentLengthForQuiz = 12;
        private const int MaxQuizContentCharacters = 12000;
        private const int MaxUrlQuizContentCharacters = 8000;
        private const string AiTimeoutMessage = "AI xử lý quá lâu, vui lòng thử lại với số câu ít hơn hoặc tài liệu ngắn hơn.";
        private const string AiRateLimitMessage = "AI đang quá tải hoặc đã chạm giới hạn gọi API. Vui lòng thử lại sau ít phút hoặc giảm số lượng câu hỏi.";
        private const string AiInvalidQuizMessage = "AI chưa tạo đủ câu hỏi hợp lệ. Vui lòng thử lại với số câu ít hơn.";
        private const string AiBadRequestMessage = "AI không nhận được yêu cầu hợp lệ. Vui lòng thử lại với tài liệu ngắn hơn hoặc số câu ít hơn.";
        private const string EmptyAiContentMessage = "Nội dung gửi sang AI chưa đủ để tạo câu hỏi.";

        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly GeminiService _geminiService;
        private readonly DocxExtractionService _docxExtractionService;
        private readonly ILogger<ExamController> _logger;

        public ExamController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            GeminiService geminiService,
            DocxExtractionService docxExtractionService,
            ILogger<ExamController> logger)
        {
            _context = context;
            _userManager = userManager;
            _geminiService = geminiService;
            _docxExtractionService = docxExtractionService;
            _logger = logger;
        }

        /// <summary>Dev test: gọi Gemini với prompt cực nhỏ để kiểm tra quota/rate limit.</summary>
        [HttpGet]
        public async Task<IActionResult> PingGemini()
        {
            var (success, statusCode, message) = await _geminiService.PingAsync();
            return Json(new { success, statusCode, message });
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
                .FirstOrDefaultAsync(qs => qs.Id == quizSetId && qs.UserId == userId);

            if (quizSet == null)
            {
                return NotFound("Không tìm thấy bộ đề thi yêu cầu.");
            }

            if (!HasCompleteQuizData(quizSet))
            {
                return BadRequest("Bộ đề này chưa có đủ câu hỏi hợp lệ. Vui lòng tạo lại bộ đề với số câu ít hơn.");
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
                    var docxExtraction = await _docxExtractionService.ExtractTextFromDocxAsync(
                        fullPath,
                        HttpContext.RequestAborted);

                    extractedText = docxExtraction.ExtractedText;
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
            catch (GeminiServiceUnavailableException ex)
            {
                return Json(new { success = false, message = ex.Message });
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
            if (model == null || model.DocumentIds == null || model.DocumentIds.Count == 0)
            {
                return Json(new { success = false, message = "Vui lòng chọn ít nhất một tài liệu." });
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

            var containsUrlDocument = documents.Any(d => d.SourceType == DocumentSourceType.URL);

            // Gộp nội dung văn bản
            var textBuilder = new StringBuilder();
            foreach (var doc in documents)
            {
                if (!string.IsNullOrWhiteSpace(doc.ExtractedText))
                {
                    var preparedDocumentText = PrepareContentForQuizGeneration(
                        doc.ExtractedText,
                        doc.SourceType);

                    if (!string.IsNullOrWhiteSpace(preparedDocumentText))
                    {
                        textBuilder.AppendLine(preparedDocumentText);
                    }

                    textBuilder.AppendLine();
                }
            }

            var mergedText = TrimContentForAi(
                textBuilder.ToString().Trim(),
                containsUrlDocument ? MaxUrlQuizContentCharacters : MaxQuizContentCharacters);
            // Làm sạch warning text/heading markers trước khi check và gửi AI
            var usefulTextForQuiz = RemoveDocxImageWarningText(mergedText);
            if (string.IsNullOrWhiteSpace(usefulTextForQuiz))
            {
                return Json(new { success = false, message = EmptyAiContentMessage });
            }

            // Giới hạn và làm sạch nội dung trước khi gửi AI để tránh prompt quá nặng/timeout.
            if (!HasEnoughQuizContent(usefulTextForQuiz))
            {
                return Json(new
                {
                    success = false,
                    message = "Tài liệu này chưa đủ nội dung học tập để tạo đề. Nếu tài liệu chủ yếu là hình ảnh, vui lòng thử ảnh rõ hơn hoặc bổ sung thêm nội dung bằng Paste Text."
                });
            }

            var requestId = Guid.NewGuid().ToString("N")[..8];

            _logger.LogInformation(
                "[CREATE_QUIZ] RequestId={RequestId}, UserId={UserId}, Documents={DocumentCount}, TotalQuestions={TotalQuestions}, ContentLength={ContentLength}, ContainsUrl={ContainsUrl}",
                requestId,
                userId,
                documents.Count,
                model.TotalQuestions,
                usefulTextForQuiz.Length,
                containsUrlDocument);

            try
            {
                // Gọi AI sinh câu hỏi - dùng usefulTextForQuiz (đã cleaned) thay vì mergedText
                var generatedQuestions = await _geminiService.GenerateQuestionsAsync(
                    usefulTextForQuiz,
                    model.TotalQuestions,
                    model.BloomRememberPercent,
                    model.BloomUnderstandPercent,
                    model.BloomApplyPercent,
                    model.Language);

                var validQuestions = ValidateGeneratedQuestions(
                    generatedQuestions,
                    model.TotalQuestions,
                    _logger,
                    out var validationMessage);

                if (validQuestions == null)
                {
                    _logger.LogWarning(
                        "CreateQuizSet: Validate thất bại. Message={ValidationMessage}, GeneratedCount={GeneratedCount}, RequestedCount={RequestedCount}",
                        validationMessage,
                        generatedQuestions?.Count ?? 0,
                        model.TotalQuestions);
                    return Json(new { success = false, message = validationMessage });
                }

                // Tạo QuizSet mới
                var title = documents.Count == 1 
                    ? $"Đề ôn tập: {documents[0].Title}" 
                    : $"Đề ôn tập tổng hợp ({documents.Count} tài liệu)";

                await using var transaction = await _context.Database.BeginTransactionAsync();

                var quizSet = new QuizSet
                {
                    Title = title,
                    TotalQuestions = validQuestions.Count,
                    BloomRememberPercent = model.BloomRememberPercent,
                    BloomUnderstandPercent = model.BloomUnderstandPercent,
                    BloomApplyPercent = model.BloomApplyPercent,
                    TimeLimitMinutes = model.TimeLimitMinutes,
                    Language = model.Language,
                    Status = QuizSetStatus.Ready,
                    CreatedAt = DateTime.UtcNow,
                    UserId = userId,
                    DocumentId = documents.Count == 1 ? documents[0].Id : null
                };

                // Lưu các Question và AnswerOption
                int orderIndex = 1;
                foreach (var gq in validQuestions)
                {
                    var question = new Question
                    {
                        Content = gq.Content,
                        BloomLevel = gq.BloomLevel == 0 ? BloomLevel.Remember :
                                     gq.BloomLevel == 1 ? BloomLevel.Understand :
                                     BloomLevel.Apply,
                        Explanation = gq.Explanation,
                        OrderIndex = orderIndex++,
                        CreatedAt = DateTime.UtcNow
                    };

                    foreach (var go in gq.Options)
                    {
                        var option = new AnswerOption
                        {
                            Label = go.Label,
                            Content = go.Content,
                            IsCorrect = go.IsCorrect
                        };
                        question.AnswerOptions.Add(option);
                    }

                    quizSet.Questions.Add(question);
                }

                _context.QuizSets.Add(quizSet);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation(
                    "[CREATE_QUIZ] RequestId={RequestId}, Thành công. QuizSetId={QuizSetId}, ValidQuestions={ValidQuestions}",
                    requestId, quizSet.Id, validQuestions.Count);

                return Json(new { success = true, quizSetId = quizSet.Id });
            }
            catch (GeminiRateLimitException)
            {
                return Json(new { success = false, message = AiRateLimitMessage });
            }
            catch (GeminiBadRequestException)
            {
                return Json(new { success = false, message = AiBadRequestMessage });
            }
            catch (GeminiTruncatedResponseException ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
            catch (GeminiServiceUnavailableException ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
            catch (TaskCanceledException)
            {
                return Json(new { success = false, message = AiTimeoutMessage });
            }
            catch (Exception ex)
            {
                if (IsAiRateLimitException(ex))
                {
                    return Json(new { success = false, message = AiRateLimitMessage });
                }

                if (IsAiBadRequestException(ex))
                {
                    return Json(new { success = false, message = AiBadRequestMessage });
                }

                if (IsAiTimeoutException(ex))
                {
                    return Json(new { success = false, message = AiTimeoutMessage });
                }

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
                .Include(qs => qs.Questions)
                    .ThenInclude(q => q.AnswerOptions)
                .FirstOrDefaultAsync(qs => qs.Id == quizSetId && qs.UserId == userId);

            if (quizSet == null)
            {
                return Json(new { success = false, message = "Không tìm thấy bộ đề thi này." });
            }

            if (!HasCompleteQuizData(quizSet))
            {
                return Json(new { success = false, message = "Bộ đề này chưa có đủ câu hỏi hợp lệ. Vui lòng tạo lại bộ đề với số câu ít hơn." });
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
                TotalQuestions = quizSet.Questions.Count
            };

            _context.ExamSessions.Add(session);
            await _context.SaveChangesAsync();

            return Json(new { success = true, sessionId = session.Id });
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

        private static string RemoveDocxImageWarningText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var cleaned = text
                .Replace(DocxExtractionService.NormalTextSectionHeading, " ", StringComparison.OrdinalIgnoreCase)
                .Replace(DocxExtractionService.ImageTextSectionHeading, " ", StringComparison.OrdinalIgnoreCase)
                .Replace(DocxExtractionService.ImageVisionTroubleshootingMessage, " ", StringComparison.OrdinalIgnoreCase)
                .Replace("KHONG_DOC_DUOC_NOI_DUNG_ANH", " ", StringComparison.OrdinalIgnoreCase);

            cleaned = Regex.Replace(
                cleaned,
                @"Tài liệu có chứa hình ảnh,\s*nhưng hệ thống chưa nhận diện được đủ nội dung học tập từ các ảnh này\.[^\r\n]*",
                " ",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            cleaned = Regex.Replace(
                cleaned,
                @"\(Không có nội dung văn bản thường được trích xuất từ Word\.\)",
                " ",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            cleaned = Regex.Replace(
                cleaned,
                @"(?:^|\r?\n)\s*Ghi chú(?: kỹ thuật)?:[^\r\n]*",
                " ",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            cleaned = Regex.Replace(
                cleaned,
                @"(?:^|\r?\n)\s*Ảnh\s+\d+\s*:",
                " ",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            return cleaned;
        }

        private static int CountUsefulWords(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            return Regex.Matches(text, @"\b[\p{L}\p{N}]{2,}\b").Count;
        }

        private static string PrepareContentForQuizGeneration(string? text, DocumentSourceType sourceType)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var preparedText = NormalizeQuizText(text);

            if (sourceType == DocumentSourceType.URL)
            {
                preparedText = RemoveWebNoiseLines(preparedText);
            }

            return TrimContentForAi(
                preparedText,
                sourceType == DocumentSourceType.URL
                    ? MaxUrlQuizContentCharacters
                    : MaxQuizContentCharacters);
        }

        private static List<GeneratedQuestionDto>? ValidateGeneratedQuestions(
            List<GeneratedQuestionDto>? generatedQuestions,
            int requestedQuestionCount,
            ILogger<ExamController> logger,
            out string message)
        {
            message = AiInvalidQuizMessage;

            if (generatedQuestions == null || generatedQuestions.Count < requestedQuestionCount)
            {
                logger.LogWarning(
                    "ValidateGeneratedQuestions: Không đủ câu. GeneratedCount={GeneratedCount}, RequestedCount={RequestedCount}",
                    generatedQuestions?.Count ?? 0,
                    requestedQuestionCount);
                return null;
            }

            var normalizedQuestions = new List<GeneratedQuestionDto>();
            var optionLabels = new[] { "A", "B", "C", "D" };
            var questionIndex = 0;

            foreach (var generatedQuestion in generatedQuestions.Take(requestedQuestionCount))
            {
                questionIndex++;

                if (string.IsNullOrWhiteSpace(generatedQuestion.Content))
                {
                    logger.LogWarning(
                        "ValidateGeneratedQuestions: Câu {Index}/{Total} có content trống.",
                        questionIndex, requestedQuestionCount);
                    return null;
                }

                var validOptions = (generatedQuestion.Options ?? new List<GeneratedOptionDto>())
                    .Where(option => !string.IsNullOrWhiteSpace(option.Content))
                    .Take(4)
                    .ToList();

                var correctCount = validOptions.Count(option => option.IsCorrect);
                if (validOptions.Count != 4 || correctCount != 1)
                {
                    logger.LogWarning(
                        "ValidateGeneratedQuestions: Câu {Index}/{Total} fail. OptionCount={OptionCount}, CorrectCount={CorrectCount}. Content={ContentPreview}",
                        questionIndex, requestedQuestionCount,
                        validOptions.Count, correctCount,
                        generatedQuestion.Content.Length > 80
                            ? generatedQuestion.Content[..80] + "..."
                            : generatedQuestion.Content);
                    return null;
                }

                var normalizedOptions = validOptions
                    .Select((option, index) => new GeneratedOptionDto
                    {
                        Label = optionLabels[index],
                        Content = option.Content.Trim(),
                        IsCorrect = option.IsCorrect
                    })
                    .ToList();

                normalizedQuestions.Add(new GeneratedQuestionDto
                {
                    Content = generatedQuestion.Content.Trim(),
                    BloomLevel = generatedQuestion.BloomLevel is >= 0 and <= 2
                        ? generatedQuestion.BloomLevel
                        : 1,
                    Explanation = generatedQuestion.Explanation?.Trim() ?? string.Empty,
                    Options = normalizedOptions
                });
            }

            message = string.Empty;
            return normalizedQuestions;
        }

        private static bool HasCompleteQuizData(QuizSet quizSet)
        {
            if (quizSet.TotalQuestions <= 0 || quizSet.Questions.Count < quizSet.TotalQuestions)
            {
                return false;
            }

            return quizSet.Questions.All(question =>
                !string.IsNullOrWhiteSpace(question.Content)
                && question.AnswerOptions.Count >= 4
                && question.AnswerOptions.Count(option => option.IsCorrect) == 1
                && question.AnswerOptions.All(option =>
                    !string.IsNullOrWhiteSpace(option.Label)
                    && !string.IsNullOrWhiteSpace(option.Content)));
        }

        private static string RemoveWebNoiseLines(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var noisePatterns = new[]
            {
                @"^table of contents$",
                @"^exit editor mode$",
                @"^ask learn$",
                @"^reading mode$",
                @"^read in english$",
                @"^add to plan$",
                @"^copy markdown$",
                @"^print$",
                @"^feedback$",
                @"^summarize this article for me$",
                @"^access to this page requires authorization$"
            };

            var builder = new StringBuilder();
            var lines = Regex.Split(text, @"\r?\n");

            foreach (var line in lines)
            {
                var normalizedLine = Regex.Replace(line.Trim(), @"\s+", " ");
                if (string.IsNullOrWhiteSpace(normalizedLine))
                {
                    continue;
                }

                if (noisePatterns.Any(pattern => Regex.IsMatch(
                    normalizedLine,
                    pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
                {
                    continue;
                }

                builder.AppendLine(normalizedLine);
            }

            return NormalizeQuizText(builder.ToString());
        }

        private static string TrimContentForAi(string text, int maxCharacters)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var normalized = NormalizeQuizText(text);

            if (normalized.Length <= maxCharacters)
            {
                return normalized;
            }

            var trimmed = normalized[..maxCharacters];
            var lastParagraphBreak = trimmed.LastIndexOf("\n\n", StringComparison.Ordinal);
            var lastSentenceBreak = Math.Max(
                trimmed.LastIndexOf(". ", StringComparison.Ordinal),
                Math.Max(
                    trimmed.LastIndexOf("? ", StringComparison.Ordinal),
                    trimmed.LastIndexOf("! ", StringComparison.Ordinal)));

            var cutIndex = lastParagraphBreak > maxCharacters * 0.55
                ? lastParagraphBreak
                : lastSentenceBreak > maxCharacters * 0.55
                    ? lastSentenceBreak + 1
                    : maxCharacters;

            return trimmed[..cutIndex].Trim() + "\n\n[Nội dung đã được rút gọn trước khi gửi AI để tránh quá tải.]";
        }

        private static string NormalizeQuizText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var normalized = Regex.Replace(text, @"[ \t\f\v]+", " ");
            normalized = Regex.Replace(normalized, @"\s*\r?\n\s*", "\n");
            normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");

            return normalized.Trim();
        }

        private static bool IsAiTimeoutException(Exception ex)
        {
            return ex is TaskCanceledException
                || ex is TimeoutException
                || ex.Message.Contains("HttpClient.Timeout", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("request was canceled", StringComparison.OrdinalIgnoreCase)
                || (ex.InnerException != null && IsAiTimeoutException(ex.InnerException));
        }

        private static bool IsAiRateLimitException(Exception ex)
        {
            return ex is GeminiRateLimitException
                || ex.Message.Contains("TooManyRequests", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("429", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("quota", StringComparison.OrdinalIgnoreCase)
                || (ex.InnerException != null && IsAiRateLimitException(ex.InnerException));
        }

        private static bool IsAiBadRequestException(Exception ex)
        {
            return ex is GeminiBadRequestException
                || ex.Message.Contains("BadRequest", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("400", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("INVALID_ARGUMENT", StringComparison.OrdinalIgnoreCase)
                || (ex.InnerException != null && IsAiBadRequestException(ex.InnerException));
        }

        private static bool HasEnoughQuizContent(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (CountUsefulWords(text) >= MinimumUsefulWordsForQuiz)
            {
                return true;
            }

            return HasEnoughMathLearningSignals(text);
        }

        private static bool HasEnoughMathLearningSignals(string text)
        {
            var normalized = Regex.Replace(text, @"\s+", " ").Trim();
            var compactLength = Regex.Replace(normalized, @"\s+", string.Empty).Length;

            if (compactLength < MinimumMathContentLengthForQuiz)
            {
                return false;
            }

            var signalCount = 0;

            signalCount += Regex.Matches(
                normalized,
                @"[\p{L}\p{N}]\s*[=+\-*/÷×]\s*[\p{L}\p{N}]",
                RegexOptions.CultureInvariant).Count;

            signalCount += Regex.Matches(
                normalized,
                @"\b\d+\s*/\s*\d+\b|\\frac\s*\{|[¼½¾⅓⅔⅕⅖⅗⅘⅙⅚⅛⅜⅝⅞]",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count;

            signalCount += Regex.Matches(
                normalized,
                @"\b(?:[\p{L}\p{N}]\s*\^\s*\d+|\d+\s*\^\s*[\p{L}\p{N}])|[⁰¹²³⁴⁵⁶⁷⁸⁹]",
                RegexOptions.CultureInvariant).Count;

            signalCount += Regex.Matches(
                normalized,
                @"√|\\sqrt|π|∞|≤|≥|≠|≈|∑|∫|∆|Δ|α|β|γ|θ|\b(?:sin|cos|tan|log|ln)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count;

            return signalCount >= MinimumMathSignalsForBasicQuiz;
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
        public int TotalQuestions { get; set; } = 20;
        public int BloomRememberPercent { get; set; } = 40;
        public int BloomUnderstandPercent { get; set; } = 40;
        public int BloomApplyPercent { get; set; } = 20;
        public int TimeLimitMinutes { get; set; } = 30;
        public OutputLanguage Language { get; set; } = OutputLanguage.Vietnamese;
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
