using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuizGenAI.Models;

namespace QuizGenAI.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _context;

        public DashboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            if (User.IsInRole(SD.Role_Admin))
            {
                return RedirectToAction("Index", "AdminDashboard", new { area = "Admin" });
            }

            ViewData["ActivePage"] = "Dashboard";

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var viewModel = new DashboardViewModel();

            // 1. Query completed sessions
            var completedSessions = await _context.ExamSessions
                .Include(es => es.QuizSet)
                .Where(es => es.UserId == userId && es.Status == ExamSessionStatus.Completed)
                .OrderBy(es => es.FinishedAt ?? es.StartedAt)
                .ToListAsync();

            viewModel.TotalExams = completedSessions.Count;
            viewModel.AverageScorePercent = completedSessions.Any() 
                ? completedSessions.Average(es => es.TotalQuestions > 0 ? (es.CorrectAnswers * 100.0 / es.TotalQuestions) : 0) 
                : 0;

            // 2. Query saved quiz sets
            viewModel.SavedQuizSetsCount = await _context.QuizSets
                .CountAsync(qs => qs.UserId == userId && qs.Status == QuizSetStatus.Ready);

            // 3. Calculate study hours
            var totalStudySeconds = await _context.ExamSessions
                .Where(es => es.UserId == userId && (es.Status == ExamSessionStatus.Completed || es.Status == ExamSessionStatus.Abandoned))
                .SumAsync(es => es.ActualDurationSeconds ?? 0);

            if (totalStudySeconds < 60)
            {
                viewModel.StudyTimeText = "Dưới 1 phút";
            }
            else if (totalStudySeconds < 3600)
            {
                viewModel.StudyTimeText = $"{totalStudySeconds / 60} phút";
            }
            else
            {
                int hours = totalStudySeconds / 3600;
                int minutes = (totalStudySeconds % 3600) / 60;
                viewModel.StudyTimeText = minutes > 0 ? $"{hours} giờ {minutes} phút" : $"{hours} giờ";
            }

            // If user has zero data, populate clean defaults but let them know
            if (completedSessions.Count == 0)
            {
                viewModel.TrendLabels = new List<string> { "Bắt đầu" };
                viewModel.TrendScores = new List<int> { 0 };

                viewModel.BloomRememberPercent = 0;
                viewModel.BloomUnderstandPercent = 0;
                viewModel.BloomApplyPercent = 0;

                viewModel.AiFeedback = "Chào mừng bạn đến với QuizGen AI! Hãy tải lên tài liệu học tập đầu tiên để tạo bộ đề trắc nghiệm và bắt đầu ôn luyện.";
                viewModel.AiFeedbackTargetBloom = "None";
            }
            else
            {
                // 4. Trend Scores (Last 7 exams)
                var lastExams = completedSessions.TakeLast(7).ToList();
                viewModel.TrendLabels = lastExams.Select((es, index) => $"Lần {index + 1}").ToList();
                viewModel.TrendScores = lastExams.Select(es => es.TotalQuestions > 0 ? (int)Math.Round(es.CorrectAnswers * 100.0 / es.TotalQuestions) : 0).ToList();

                // 5. Bloom Distribution
                int remCorrect = completedSessions.Sum(es => es.RememberCorrect);
                int remTotal = completedSessions.Sum(es => es.RememberTotal);
                int undCorrect = completedSessions.Sum(es => es.UnderstandCorrect);
                int undTotal = completedSessions.Sum(es => es.UnderstandTotal);
                int appCorrect = completedSessions.Sum(es => es.ApplyCorrect);
                int appTotal = completedSessions.Sum(es => es.ApplyTotal);

                viewModel.BloomRememberPercent = remTotal > 0 ? (int)Math.Round(remCorrect * 100.0 / remTotal) : 0;
                viewModel.BloomUnderstandPercent = undTotal > 0 ? (int)Math.Round(undCorrect * 100.0 / undTotal) : 0;
                viewModel.BloomApplyPercent = appTotal > 0 ? (int)Math.Round(appCorrect * 100.0 / appTotal) : 0;

                // 6. AI Feedback logic
                int minScore = Math.Min(viewModel.BloomRememberPercent, Math.Min(viewModel.BloomUnderstandPercent, viewModel.BloomApplyPercent));
                if (minScore == viewModel.BloomApplyPercent && viewModel.BloomApplyPercent < 70)
                {
                    viewModel.AiFeedback = "Dựa trên lịch sử làm bài, bạn đang nắm rất chắc lý thuyết (Remembering & Understanding) nhưng gặp khó khăn khi áp dụng vào bài toán thực tế. Hãy tập trung vào mức độ Vận dụng (Applying) để cải thiện điểm số tổng thể.";
                    viewModel.AiFeedbackTargetBloom = "Apply";
                }
                else if (minScore == viewModel.BloomUnderstandPercent && viewModel.BloomUnderstandPercent < 70)
                {
                    viewModel.AiFeedback = "Bạn cần chú ý cải thiện mức độ Thông hiểu (Understanding) kiến thức bản chất, thay vị chỉ học vẹt các khái niệm cơ bản.";
                    viewModel.AiFeedbackTargetBloom = "Understand";
                }
                else if (minScore == viewModel.BloomRememberPercent && viewModel.BloomRememberPercent < 70)
                {
                    viewModel.AiFeedback = "Bạn đang thiếu sót ở các định nghĩa hoặc chi tiết nhỏ ở mức độ Nhận biết (Remembering). Hãy ôn lại lý thuyết nền tảng!";
                    viewModel.AiFeedbackTargetBloom = "Remember";
                }
                else
                {
                    viewModel.AiFeedback = "Tuyệt vời! Kết quả học tập của bạn rất đồng đều ở cả 3 mức độ Nhận biết, Thông hiểu và Vận dụng. Hãy tiếp tục phát huy!";
                    viewModel.AiFeedbackTargetBloom = "None";
                }
            }

            // 7. Query Weak Topics from Db
            var weakTopicsDb = await _context.WeakTopics
                .Where(wt => wt.UserId == userId)
                .OrderBy(wt => wt.AccuracyRate)
                .Take(3)
                .ToListAsync();

            if (weakTopicsDb.Any())
            {
                viewModel.WeakTopics = weakTopicsDb.Select(wt => new WeakTopicItemViewModel
                {
                    TopicName = wt.TopicName,
                    AccuracyRate = (int)Math.Round(wt.AccuracyRate),
                    Recommendation = wt.BloomLevel == BloomLevel.Apply ? "Cần cải thiện: Vận dụng công thức và lý thuyết vào bài tập" :
                                     wt.BloomLevel == BloomLevel.Understand ? "Cần cải thiện: Xem lại bản chất và giải thích định lý" :
                                     "Cần cải thiện: Học thuộc lòng các định nghĩa cơ bản"
                }).ToList();
            }
            else if (completedSessions.Any())
            {
                viewModel.WeakTopics = new List<WeakTopicItemViewModel>();
                if (viewModel.BloomApplyPercent < 75)
                {
                    viewModel.WeakTopics.Add(new WeakTopicItemViewModel { TopicName = "Tầng Vận dụng (Apply)", AccuracyRate = viewModel.BloomApplyPercent, Recommendation = "Luyện các câu hỏi bài tập tự luận/thực hành" });
                }
                if (viewModel.BloomUnderstandPercent < 75)
                {
                    viewModel.WeakTopics.Add(new WeakTopicItemViewModel { TopicName = "Tầng Thông hiểu (Understand)", AccuracyRate = viewModel.BloomUnderstandPercent, Recommendation = "Giải thích lý do lựa chọn đáp án" });
                }
                if (viewModel.BloomRememberPercent < 75)
                {
                    viewModel.WeakTopics.Add(new WeakTopicItemViewModel { TopicName = "Tầng Nhận biết (Remember)", AccuracyRate = viewModel.BloomRememberPercent, Recommendation = "Ghi nhớ các khái niệm cốt lõi" });
                }
            }

            // 8. Query In Progress session
            var inProgress = await _context.ExamSessions
                .Include(es => es.QuizSet)
                .Where(es => es.UserId == userId && es.Status == ExamSessionStatus.InProgress)
                .OrderByDescending(es => es.StartedAt)
                .FirstOrDefaultAsync();

            if (inProgress != null)
            {
                int totalQuestions = await _context.Questions.CountAsync(q => q.QuizSetId == inProgress.QuizSetId);
                int answeredQuestions = await _context.ExamAnswers.CountAsync(ea => ea.ExamSessionId == inProgress.Id && ea.SelectedAnswerOptionId != null);
                viewModel.InProgressExam = new InProgressExamViewModel
                {
                    SessionId = inProgress.Id,
                    Title = inProgress.QuizSet?.Title ?? "Bài thi chưa đặt tên",
                    QuestionsRemaining = Math.Max(0, totalQuestions - answeredQuestions),
                    TotalQuestions = totalQuestions
                };
            }

            // 9. Query Recent completed sessions
            viewModel.RecentExams = completedSessions
                .OrderByDescending(es => es.FinishedAt ?? es.StartedAt)
                .Take(3)
                .Select(es => new RecentExamItemViewModel
                {
                    SessionId = es.Id,
                    Title = es.QuizSet?.Title ?? "Bài thi tự do",
                    TakenAt = es.FinishedAt ?? es.StartedAt,
                    ScorePercent = es.TotalQuestions > 0 ? (int)Math.Round(es.CorrectAnswers * 100.0 / es.TotalQuestions) : 0,
                    CorrectAnswers = es.CorrectAnswers,
                    TotalQuestions = es.TotalQuestions
                })
                .ToList();

            return View(viewModel);
        }
    }
}
