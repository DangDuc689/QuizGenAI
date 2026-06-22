using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace QuizGenAI.Models
{
    public class ApplicationUser : IdentityUser
    {
        [Required]
        [StringLength(100)]
        public string FullName { get; set; } = string.Empty;

        [StringLength(200)]
        public string? Address { get; set; }

        [StringLength(500)]
        public string? AvatarPath { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsActive { get; set; } = true;
        
        public DateTime? LockedAt { get; set; }

        // Navigation
        public ICollection<Document> Documents { get; set; } = new List<Document>();
        public ICollection<QuizSet> QuizSets { get; set; } = new List<QuizSet>();
        public ICollection<ExamSession> ExamSessions { get; set; } = new List<ExamSession>();
    }
}
