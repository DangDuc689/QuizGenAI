using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuizGenAI.Models
{
    public class WeakTopic
    {
        public int Id { get; set; }

        /// <summary>
        /// Tên chủ đề – có thể là tên tầng Bloom ("Vận dụng")
        /// hoặc một chủ đề AI cluster trong tương lai
        /// </summary>
        [Required]
        [StringLength(200)]
        public string TopicName { get; set; } = string.Empty;

        public BloomLevel? BloomLevel { get; set; }

        public int TotalAttempts { get; set; } = 0;
        public int CorrectAttempts { get; set; } = 0;

        /// <summary>% đúng, tính lại sau mỗi lần thi</summary>
        [Column(TypeName = "decimal(5,2)")]
        public decimal AccuracyRate { get; set; } = 0;

        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        // Foreign key
        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }
    }
}
