using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuizGenAI.Models
{
    public class AnswerOption
    {
        public int Id { get; set; }

        /// <summary>A, B, C, hoặc D</summary>
        [Required]
        [StringLength(1)]
        public string Label { get; set; } = string.Empty;

        [Required]
        public string Content { get; set; } = string.Empty;

        public bool IsCorrect { get; set; } = false;

        // Foreign key
        public int QuestionId { get; set; }

        [ForeignKey("QuestionId")]
        public Question? Question { get; set; }
    }
}
