using System.ComponentModel.DataAnnotations;

namespace QuizGenAI.Models
{
    public class ExamHistoryViewModel
    {
        public string Title { get; set; } = "Lịch sử thi";
        public string Subtitle { get; set; } = "Review your past performance and identify areas for improvement.";
        public double AverageScorePercent { get; set; }
        public string AverageScoreLabel => AverageScorePercent > 0 ? $"{Math.Round(AverageScorePercent)}%" : "0%";
        public string HighlightBadge { get; set; } = "Top 15% in class";
        public List<int> PerformanceTrend { get; set; } = new List<int>();
        public List<ExamHistoryItem> RecentAssessments { get; set; } = new List<ExamHistoryItem>();
    }

    public class ExamHistoryItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = "General";
        public DateTime TakenAt { get; set; }
        public int ScorePercent { get; set; }
        public int CorrectAnswers { get; set; }
        public int TotalQuestions { get; set; }
        public string QuestionSummary => $"{CorrectAnswers} / {TotalQuestions}";
        public string ScoreBadgeClass => ScorePercent >= 85 ? "bg-emerald-100 text-emerald-900" : ScorePercent >= 70 ? "bg-amber-100 text-amber-900" : "bg-rose-100 text-rose-900";
    }
}
