using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuizGenAI.Models
{
    public enum ExamSessionStatus
    {
        InProgress = 0,  // Đang thi
        Completed = 1,   // Đã nộp / hết giờ
        Abandoned = 2    // Bỏ giữa chừng
    }

    public class ExamSession
    {
        public int Id { get; set; }

        public ExamSessionStatus Status { get; set; } = ExamSessionStatus.InProgress;

        /// <summary>Đánh dấu session này thuộc chế độ luyện tập chủ đề còn yếu</summary>
        public bool IsPracticeMode { get; set; } = false;

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? FinishedAt { get; set; }

        /// <summary>Thời gian thực tế làm bài tính theo giây</summary>
        public int? ActualDurationSeconds { get; set; }

        // Kết quả tổng quan
        public int TotalQuestions { get; set; }
        public int CorrectAnswers { get; set; }

        /// <summary>Số câu đúng ở tầng Nhận biết</summary>
        public int RememberCorrect { get; set; }
        public int RememberTotal { get; set; }

        /// <summary>Số câu đúng ở tầng Thông hiểu</summary>
        public int UnderstandCorrect { get; set; }
        public int UnderstandTotal { get; set; }

        /// <summary>Số câu đúng ở tầng Vận dụng</summary>
        public int ApplyCorrect { get; set; }
        public int ApplyTotal { get; set; }

        // Foreign keys
        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        public int QuizSetId { get; set; }

        [ForeignKey("QuizSetId")]
        public QuizSet? QuizSet { get; set; }

        // Navigation
        public ICollection<ExamAnswer> ExamAnswers { get; set; } = new List<ExamAnswer>();
    }
}
