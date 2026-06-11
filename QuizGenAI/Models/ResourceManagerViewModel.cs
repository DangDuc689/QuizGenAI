using System;
using System.Collections.Generic;

namespace QuizGenAI.Models
{
    public class ResourceManagerViewModel
    {
        // Filters & Search
        public string SearchQuery { get; set; } = string.Empty;
        public string SelectedFormat { get; set; } = string.Empty;
        public string SelectedSort { get; set; } = "newest";

        // Pagination
        public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; set; } = 1;
        public int PageSize { get; set; } = 8;
        public int TotalItems { get; set; }

        // Documents
        public List<DocumentItemViewModel> Documents { get; set; } = new();
    }

    public class DocumentItemViewModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string OwnerName { get; set; } = string.Empty;
        public string OwnerEmail { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string FileSize { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty; // "pdf", "docx", "url", "txt", "xlsx"
        public int QuestionsCount { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? SourceUrl { get; set; }
        public int? PageCount { get; set; }
    }

    public class DocumentMetadataAnalysis
    {
        public int CharacterCount { get; set; }
        public int WordCount { get; set; }
        public int ParagraphCount { get; set; }
        public string DetectedLanguage { get; set; } = string.Empty;
        public string LengthCategory { get; set; } = string.Empty;
        public bool HasExternalLinks { get; set; }
        public bool HasEmailLikeText { get; set; }
        public bool HasPhoneLikeText { get; set; }
        public int QuizSetCount { get; set; }
        public int QuestionCount { get; set; }
        public string ReadinessStatus { get; set; } = string.Empty;
        public string PrivacyNote { get; set; } = string.Empty;
    }
}
