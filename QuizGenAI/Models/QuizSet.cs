using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuizGenAI.Models
{
    public enum OutputLanguage
    {
        Vietnamese = 0,
        English = 1
    }

    public enum QuizSetStatus
    {
        Draft = 0,       // Vừa tạo xong, chưa xem trước
        Ready = 1,       // Đã xem trước, sẵn sàng thi
        Archived = 2     // Đã lưu trữ
    }

    public enum DifficultyLevel
    {
        Easy = 0,
        Medium = 1,
        Hard = 2
    }

    public class QuizSet
    {
        public int Id { get; set; }

        [Required]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;

        public int TotalQuestions { get; set; } = 20;

        /// <summary>Tỉ lệ % câu hỏi tầng Nhận biết (Remember)</summary>
        [Range(0, 100)]
        public int BloomRememberPercent { get; set; } = 40;

        /// <summary>Tỉ lệ % câu hỏi tầng Thông hiểu (Understand)</summary>
        [Range(0, 100)]
        public int BloomUnderstandPercent { get; set; } = 40;

        /// <summary>Tỉ lệ % câu hỏi tầng Vận dụng (Apply)</summary>
        [Range(0, 100)]
        public int BloomApplyPercent { get; set; } = 20;

        /// <summary>Thời gian làm bài tính theo phút</summary>
        public int TimeLimitMinutes { get; set; } = 30;

        public OutputLanguage Language { get; set; } = OutputLanguage.Vietnamese;

        public DifficultyLevel Difficulty { get; set; } = DifficultyLevel.Medium;

        public QuizSetStatus Status { get; set; } = QuizSetStatus.Draft;

        /// <summary>Bộ đề có được hiển thị công khai không (trang Khám phá)</summary>
        public bool IsPublic { get; set; } = false;

        public int ViewCount { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Foreign keys
        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        /// <summary>Tài liệu gốc (null nếu guest dùng thử hoặc paste text trực tiếp không lưu tài liệu)</summary>
        public int? DocumentId { get; set; }

        [ForeignKey("DocumentId")]
        public Document? Document { get; set; }

        // Navigation
        public ICollection<Question> Questions { get; set; } = new List<Question>();
        public ICollection<ExamSession> ExamSessions { get; set; } = new List<ExamSession>();
    }
}
