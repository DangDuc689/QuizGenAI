using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuizGenAI.Models
{
    public class ExamAnswer
    {
        public int Id { get; set; }

        /// <summary>User có trả lời câu này không (null = bỏ qua)</summary>
        public bool? IsCorrect { get; set; }

        // Foreign keys
        public int ExamSessionId { get; set; }

        [ForeignKey("ExamSessionId")]
        public ExamSession? ExamSession { get; set; }

        public int QuestionId { get; set; }

        [ForeignKey("QuestionId")]
        public Question? Question { get; set; }

        /// <summary>Đáp án user đã chọn (null nếu bỏ qua)</summary>
        public int? SelectedAnswerOptionId { get; set; }

        [ForeignKey("SelectedAnswerOptionId")]
        public AnswerOption? SelectedAnswerOption { get; set; }
    }
}
