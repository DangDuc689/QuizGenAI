using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuizGenAI.Models;
using QuizGenAI.Services;
using QuizGenAI.Controllers;

namespace QuizGenAI.Controllers
{
    [Authorize]
    public class PracticeController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly PracticeQuizService _practiceService;

        public PracticeController(ApplicationDbContext context, PracticeQuizService practiceService)
        {
            _context = context;
            _practiceService = practiceService;
        }

        /// <summary>
        /// Trang chính hiển thị danh sách các chủ đề còn yếu của người dùng.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            ViewData["ActivePage"] = "Practice";

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account", new { area = "Identity" });
            }

            var weakTopics = await _context.WeakTopics
                .Where(wt => wt.UserId == userId)
                .OrderBy(wt => wt.AccuracyRate)
                .ToListAsync();

            return View(weakTopics);
        }

        /// <summary>
        /// API tạo bộ đề luyện tập ngẫu nhiên từ câu hỏi cũ dựa trên Bloom Level.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreatePracticeQuiz(BloomLevel targetLevel, int questionCount)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "Người dùng chưa đăng nhập hoặc phiên làm việc hết hạn." });
            }

            if (questionCount <= 0)
            {
                return Json(new { success = false, message = "Số lượng câu hỏi phải lớn hơn 0." });
            }

            try
            {
                var quizSet = await _practiceService.GeneratePracticeQuizAsync(userId, targetLevel, questionCount);
                if (quizSet == null)
                {
                    var levelName = targetLevel == BloomLevel.Remember ? "Nhận biết" :
                                    targetLevel == BloomLevel.Understand ? "Thông hiểu" : "Vận dụng";
                    return Json(new { 
                        success = false, 
                        message = $"Không đủ câu hỏi thuộc cấp độ '{levelName}' trong lịch sử thi để tạo đề luyện tập. Hãy làm thêm các đề thi thông thường để tích lũy câu hỏi!" 
                    });
                }

                return Json(new { success = true, quizSetId = quizSet.Id });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Lỗi khi tạo đề luyện tập: {ex.Message}" });
            }
        }

        /// <summary>
        /// API khởi tạo session làm bài luyện tập.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> StartPracticeSession(int quizSetId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "Người dùng chưa đăng nhập hoặc phiên làm việc hết hạn." });
            }

            var quizSet = await _context.QuizSets
                .Include(qs => qs.Questions)
                .FirstOrDefaultAsync(qs => qs.Id == quizSetId && qs.UserId == userId && qs.Status == QuizSetStatus.Practice);

            if (quizSet == null)
            {
                return Json(new { success = false, message = "Không tìm thấy đề luyện tập yêu cầu hoặc bạn không có quyền sở hữu." });
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
                TotalQuestions = quizSet.Questions.Count,
                IsPracticeMode = true
            };

            _context.ExamSessions.Add(session);
            await _context.SaveChangesAsync();

            return Json(new { success = true, sessionId = session.Id });
        }

        /// <summary>
        /// API nộp bài thi luyện tập.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> SubmitPractice([FromBody] ExamSubmissionModel model)
        {
            if (model == null)
            {
                return Json(new { success = false, message = "Dữ liệu nộp bài không hợp lệ." });
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "Người dùng chưa đăng nhập hoặc phiên làm việc hết hạn." });
            }

            var session = await _context.ExamSessions
                .Include(es => es.QuizSet)
                .FirstOrDefaultAsync(es => es.Id == model.SessionId && es.UserId == userId && es.IsPracticeMode);

            if (session == null)
            {
                return Json(new { success = false, message = "Không tìm thấy phiên làm bài luyện tập hợp lệ." });
            }

            if (session.Status == ExamSessionStatus.Completed)
            {
                return Json(new { success = false, message = "Bài thi luyện tập này đã được nộp trước đó." });
            }

            session.Status = ExamSessionStatus.Completed;
            session.FinishedAt = DateTime.UtcNow;
            session.ActualDurationSeconds = model.ActualDurationSeconds;

            // Lấy danh sách câu hỏi và đáp án đúng
            var questions = await _context.Questions
                .Include(q => q.AnswerOptions)
                .Where(q => q.QuizSetId == session.QuizSetId)
                .ToListAsync();

            var savedAnswers = await _context.ExamAnswers
                .Where(ea => ea.ExamSessionId == session.Id)
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

                var examAnswer = savedAnswers.FirstOrDefault(sa => sa.QuestionId == question.Id);
                if (examAnswer == null)
                {
                    examAnswer = new ExamAnswer
                    {
                        ExamSessionId = session.Id,
                        QuestionId = question.Id,
                        SelectedAnswerOptionId = userAns?.SelectedOptionId,
                        IsCorrect = userAns?.SelectedOptionId.HasValue == true ? isCorrect : (bool?)null
                    };
                    _context.ExamAnswers.Add(examAnswer);
                }
                else
                {
                    examAnswer.SelectedAnswerOptionId = userAns?.SelectedOptionId;
                    examAnswer.IsCorrect = userAns?.SelectedOptionId.HasValue == true ? isCorrect : (bool?)null;
                    _context.ExamAnswers.Update(examAnswer);
                }
            }

            session.CorrectAnswers = correctCount;
            session.RememberCorrect = remCorrect;
            session.RememberTotal = remTotal;
            session.UnderstandCorrect = undCorrect;
            session.UnderstandTotal = undTotal;
            session.ApplyCorrect = appCorrect;
            session.ApplyTotal = appTotal;

            // Nghiệp vụ cập nhật điểm yếu
            await _practiceService.UpdateWeakTopicAfterPractice(userId, session);

            await _context.SaveChangesAsync();

            return Json(new {
                success = true,
                correctAnswers = correctCount,
                totalQuestions = session.TotalQuestions,
                scorePercent = session.TotalQuestions > 0 ? (int)Math.Round(correctCount * 100.0 / session.TotalQuestions) : 0,
                sessionId = session.Id
            });
        }

        /// <summary>
        /// Xem kết quả bài luyện tập.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Result(int sessionId)
        {
            ViewData["ActivePage"] = "Practice";

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account", new { area = "Identity" });
            }

            var session = await _context.ExamSessions
                .Include(es => es.QuizSet)
                .Include(es => es.ExamAnswers)
                    .ThenInclude(ea => ea.Question!)
                        .ThenInclude(q => q.AnswerOptions)
                .FirstOrDefaultAsync(es => es.Id == sessionId && es.UserId == userId && es.IsPracticeMode);

            if (session == null)
            {
                return NotFound("Không tìm thấy kết quả phiên luyện tập.");
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
    }
}
