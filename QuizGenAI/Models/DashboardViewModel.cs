using System;
using System.Collections.Generic;

namespace QuizGenAI.Models
{
    public class DashboardViewModel
    {
        public int TotalExams { get; set; }
        public double AverageScorePercent { get; set; }
        public int SavedQuizSetsCount { get; set; }
        public double StudyHours { get; set; }
        
        // Progress trend (Chart.js data)
        public List<string> TrendLabels { get; set; } = new List<string>();
        public List<int> TrendScores { get; set; } = new List<int>();
        
        // Bloom taxonomy distribution
        public int BloomRememberPercent { get; set; }
        public int BloomUnderstandPercent { get; set; }
        public int BloomApplyPercent { get; set; }
        
        // Weak topics
        public List<WeakTopicItemViewModel> WeakTopics { get; set; } = new List<WeakTopicItemViewModel>();
        
        // In progress exam session if any
        public InProgressExamViewModel? InProgressExam { get; set; }
        
        // Recent completed exam sessions
        public List<RecentExamItemViewModel> RecentExams { get; set; } = new List<RecentExamItemViewModel>();
        
        // AI Feedback
        public string AiFeedback { get; set; } = string.Empty;
        public string AiFeedbackTargetBloom { get; set; } = string.Empty; // "Remember", "Understand", "Apply"
    }

    public class WeakTopicItemViewModel
    {
        public string TopicName { get; set; } = string.Empty;
        public int AccuracyRate { get; set; }
        public string Recommendation { get; set; } = string.Empty;
    }

    public class InProgressExamViewModel
    {
        public int SessionId { get; set; }
        public string Title { get; set; } = string.Empty;
        public int QuestionsRemaining { get; set; }
        public int TotalQuestions { get; set; }
    }

    public class RecentExamItemViewModel
    {
        public int SessionId { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateTime TakenAt { get; set; }
        public int ScorePercent { get; set; }
        public int CorrectAnswers { get; set; }
        public int TotalQuestions { get; set; }
    }
}
