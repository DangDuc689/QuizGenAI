using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace QuizGenAI.Models
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Document> Documents { get; set; }
        public DbSet<QuizSet> QuizSets { get; set; }
        public DbSet<Question> Questions { get; set; }
        public DbSet<AnswerOption> AnswerOptions { get; set; }
        public DbSet<ExamSession> ExamSessions { get; set; }
        public DbSet<ExamAnswer> ExamAnswers { get; set; }
        public DbSet<WeakTopic> WeakTopics { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Cascade delete: Xóa QuizSet → xóa hết Question và AnswerOption
            modelBuilder.Entity<Question>()
                .HasOne(q => q.QuizSet)
                .WithMany(qs => qs.Questions)
                .HasForeignKey(q => q.QuizSetId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AnswerOption>()
                .HasOne(a => a.Question)
                .WithMany(q => q.AnswerOptions)
                .HasForeignKey(a => a.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            // Cascade delete: Xóa ExamSession -> xóa sạch ExamAnswer
            modelBuilder.Entity<ExamAnswer>()
                .HasOne(ea => ea.ExamSession)
                .WithMany(es => es.ExamAnswers)
                .HasForeignKey(ea => ea.ExamSessionId)
                .OnDelete(DeleteBehavior.Cascade);

            // ExamAnswer → SelectedAnswerOption: NO ACTION để tránh multiple cascade paths
            modelBuilder.Entity<ExamAnswer>()
                .HasOne(ea => ea.SelectedAnswerOption)
                .WithMany()
                .HasForeignKey(ea => ea.SelectedAnswerOptionId)
                .OnDelete(DeleteBehavior.NoAction);

            // ExamAnswer → Question: NO ACTION
            modelBuilder.Entity<ExamAnswer>()
                .HasOne(ea => ea.Question)
                .WithMany(q => q.ExamAnswers)
                .HasForeignKey(ea => ea.QuestionId)
                .OnDelete(DeleteBehavior.NoAction);

            // ExamSession → QuizSet: NO ACTION
            modelBuilder.Entity<ExamSession>()
                .HasOne(es => es.QuizSet)
                .WithMany(qs => qs.ExamSessions)
                .HasForeignKey(es => es.QuizSetId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}
