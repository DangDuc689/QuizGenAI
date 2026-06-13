using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuizGenAI.Models
{
    public enum DocumentSourceType
    {
        PastedText = 0,
        PDF = 1,
        Word = 2,      // .docx
        Excel = 3,     // .xlsx
        URL = 4
    }

    public class Document
    {
        public int Id { get; set; }

        [Required]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Mô tả ngắn về tài liệu, do người dùng nhập khi tạo tài liệu.
        /// </summary>
        [StringLength(500)]
        public string? Description { get; set; }

        public DocumentSourceType SourceType { get; set; }

        /// <summary>
        /// Đường dẫn file đã lưu trong wwwroot (null nếu là PastedText hoặc URL)
        /// </summary>
        [StringLength(500)]
        public string? FilePath { get; set; }

        /// <summary>
        /// URL gốc nếu SourceType = URL
        /// </summary>
        [StringLength(2000)]
        public string? SourceUrl { get; set; }

        /// <summary>
        /// Nội dung text đã được extract ra từ file/URL/paste.
        /// Dùng để gửi lên AI và hỗ trợ highlight nội dung gốc.
        /// </summary>
        public string? ExtractedText { get; set; }

        /// <summary>
        /// Số trang (với PDF/Word), null nếu không xác định được
        /// </summary>
        public int? PageCount { get; set; }

        public long? FileSizeBytes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Foreign key
        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        // Navigation
        public ICollection<QuizSet> QuizSets { get; set; } = new List<QuizSet>();

        /// <summary>
        /// Bản tóm tắt chính của AI (3-5 đoạn ngắn, khoảng 250-350 từ)
        /// </summary>
        public string? AiSummary { get; set; }

        /// <summary>
        /// Các điểm nổi bật dưới dạng chuỗi JSON của một mảng string (["ý 1", "ý 2", ...])
        /// </summary>
        public string? AiKeyPoints { get; set; }

        /// <summary>
        /// Gợi ý đối tượng học tập phù hợp từ AI
        /// </summary>
        public string? AiAudience { get; set; }
    }
}
