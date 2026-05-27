using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuizGenAI.Models
{
    public enum BloomLevel
    {
        Remember = 0,    // Nhận biết
        Understand = 1,  // Thông hiểu
        Apply = 2        // Vận dụng
    }

    public class Question
    {
        public int Id { get; set; }

        [Required]
        public string Content { get; set; } = string.Empty;

        public BloomLevel BloomLevel { get; set; }

        /// <summary>
        /// Đoạn nội dung gốc mà câu hỏi được trích ra từ đó.
        /// Dùng để highlight nội dung gốc trong trang kết quả.
        /// </summary>
        public string? SourceChunk { get; set; }

        /// <summary>Giải thích đáp án đúng do AI tạo ra</summary>
        public string? Explanation { get; set; }

        /// <summary>Thứ tự câu hỏi trong bộ đề</summary>
        public int OrderIndex { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Foreign key
        public int QuizSetId { get; set; }

        [ForeignKey("QuizSetId")]
        public QuizSet? QuizSet { get; set; }

        // Navigation
        public ICollection<AnswerOption> AnswerOptions { get; set; } = new List<AnswerOption>();
        public ICollection<ExamAnswer> ExamAnswers { get; set; } = new List<ExamAnswer>();
    }
}
