using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using QuizGenAI.Models;

namespace QuizGenAI.Services
{
    public class PracticeQuizService
    {
        private readonly ApplicationDbContext _context;

        public PracticeQuizService(ApplicationDbContext context)
        {
            _context = context;
        }

        // Tạo bộ đề luyện tập ngẫu nhiên từ câu hỏi cũ của user theo BloomLevel.
        public async Task<QuizSet?> GeneratePracticeQuizAsync(string userId, BloomLevel targetBloomLevel, int questionCount)
        {
            var questions = await _context.Questions
                .Include(q => q.AnswerOptions)
                .Where(q => q.QuizSet!.UserId == userId && q.BloomLevel == targetBloomLevel && q.QuizSet.Status != QuizSetStatus.Practice)
                .ToListAsync();

            if (!questions.Any())
            {
                return null;
            }

            var random = new Random();
            var shuffledQuestions = questions.OrderBy(q => random.Next()).Take(questionCount).ToList();

            var levelName = targetBloomLevel == BloomLevel.Remember ? "Nhận biết" :
                            targetBloomLevel == BloomLevel.Understand ? "Thông hiểu" : "Vận dụng";

            var practiceQuizSet = new QuizSet
            {
                Title = $"Luyện tập: {levelName} ({DateTime.Now:dd/MM/yyyy HH:mm})",
                TotalQuestions = shuffledQuestions.Count,
                BloomRememberPercent = targetBloomLevel == BloomLevel.Remember ? 100 : 0,
                BloomUnderstandPercent = targetBloomLevel == BloomLevel.Understand ? 100 : 0,
                BloomApplyPercent = targetBloomLevel == BloomLevel.Apply ? 100 : 0,
                TimeLimitMinutes = Math.Max(5, shuffledQuestions.Count * 2),
                Status = QuizSetStatus.Practice,
                TargetBloomLevel = targetBloomLevel,
                UserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            // Nhân bản các câu hỏi và các lựa chọn đáp án tương ứng sang bộ đề mới
            int orderIndex = 1;
            foreach (var sq in shuffledQuestions)
            {
                var newQuestion = new Question
                {
                    Content = sq.Content,
                    BloomLevel = sq.BloomLevel,
                    SourceChunk = sq.SourceChunk,
                    Explanation = sq.Explanation,
                    OrderIndex = orderIndex++,
                    CreatedAt = DateTime.UtcNow
                };

                foreach (var opt in sq.AnswerOptions)
                {
                    newQuestion.AnswerOptions.Add(new AnswerOption
                    {
                        Label = opt.Label,
                        Content = opt.Content,
                        IsCorrect = opt.IsCorrect
                    });
                }

                practiceQuizSet.Questions.Add(newQuestion);
            }

            _context.QuizSets.Add(practiceQuizSet);
            await _context.SaveChangesAsync();

            return practiceQuizSet;
        }

        // Cập nhật hoặc xóa WeakTopic sau khi user hoàn thành bài luyện tập.
        public async Task UpdateWeakTopicAfterPractice(string userId, ExamSession session)
        {
            if (session.QuizSet == null)
            {
                session.QuizSet = await _context.QuizSets.FindAsync(session.QuizSetId);
            }

            if (session.QuizSet == null || !session.QuizSet.TargetBloomLevel.HasValue)
            {
                return;
            }

            var targetLevel = session.QuizSet.TargetBloomLevel.Value;

            // Xác định số câu làm và số câu đúng của level này trong session
            int totalAttempts = session.TotalQuestions;
            int correctAttempts = session.CorrectAnswers;

            if (totalAttempts <= 0) return;

            // Tìm WeakTopic tương ứng
            var existingWeakTopic = await _context.WeakTopics
                .FirstOrDefaultAsync(wt => wt.UserId == userId && wt.BloomLevel == targetLevel);

            if (existingWeakTopic != null)
            {
                existingWeakTopic.TotalAttempts += totalAttempts;
                existingWeakTopic.CorrectAttempts += correctAttempts;
                existingWeakTopic.AccuracyRate = existingWeakTopic.TotalAttempts > 0
                    ? (decimal)(existingWeakTopic.CorrectAttempts * 100.0 / existingWeakTopic.TotalAttempts)
                    : 0;
                existingWeakTopic.LastUpdated = DateTime.UtcNow;

                if (existingWeakTopic.AccuracyRate >= 80)
                {
                    _context.WeakTopics.Remove(existingWeakTopic);
                }
                else
                {
                    _context.WeakTopics.Update(existingWeakTopic);
                }
            }
            else
            {
                // Nếu chưa có WeakTopic nhưng làm bài dưới 80%, ta tạo mới để theo dõi tiếp
                double accuracy = (double)correctAttempts / totalAttempts;
                if (accuracy < 0.80)
                {
                    var levelName = targetLevel == BloomLevel.Remember ? "Nhận biết (Remembering)" :
                                     targetLevel == BloomLevel.Understand ? "Thông hiểu (Understanding)" :
                                     "Vận dụng (Applying)";

                    var weakTopic = new WeakTopic
                    {
                        UserId = userId,
                        TopicName = $"Kỹ năng: {levelName}",
                        BloomLevel = targetLevel,
                        TotalAttempts = totalAttempts,
                        CorrectAttempts = correctAttempts,
                        AccuracyRate = (decimal)(accuracy * 100),
                        LastUpdated = DateTime.UtcNow
                    };
                    _context.WeakTopics.Add(weakTopic);
                }
            }

            await _context.SaveChangesAsync();
        }
    }
}
