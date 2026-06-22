using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace QuizGenAI.Services
{
    public sealed class PdfExtractionService
    {
        public const string NormalTextSectionHeading = "[Nội dung văn bản trích xuất từ PDF]";
        public const string ImageTextSectionHeading = "[Nội dung nhận diện từ hình ảnh trong PDF]";
        public const string MetadataSectionHeading = "[PDF_EXTRACTION_METADATA]";
        public const string MetadataSectionEndHeading = "[/PDF_EXTRACTION_METADATA]";
        public const string ScannedPdfMessage = "PDF có vẻ là dạng scan/ảnh hoặc văn bản trích xuất trực tiếp quá ít. Hệ thống đã thử nhận diện nội dung từ hình ảnh trong PDF.";
        public const string ImageUnreadableMessage = "PDF có chứa hình ảnh, nhưng hệ thống chưa nhận diện được đủ nội dung học tập từ các ảnh này. Bạn có thể thử dùng PDF rõ hơn hoặc bổ sung nội dung bằng Paste Text.";

        private readonly GeminiService _geminiService;
        private readonly ILogger<PdfExtractionService> _logger;

        public PdfExtractionService(
            GeminiService geminiService,
            ILogger<PdfExtractionService> logger)
        {
            _geminiService = geminiService;
            _logger = logger;
        }

        public async Task<PdfExtractionResult> ExtractAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("PDF extraction starting for {FilePath} using Gemini API.", filePath);

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Không tìm thấy file PDF yêu cầu.", filePath);
            }

            // 1. Đếm số trang thủ công bằng quét nhị phân ASCII
            var pageCount = GetPdfPageCount(filePath);

            // 2. Đọc toàn bộ byte file PDF
            byte[] pdfBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);

            // 3. Gọi Gemini để trích xuất văn bản sạch có cấu trúc
            var structuredText = await _geminiService.ExtractStructuredTextFromPdfAsync(pdfBytes, cancellationToken);

            var textLength = structuredText.Length;
            var hasEnoughText = textLength >= 100; // Ước lượng thô dựa trên ký tự

            var result = new PdfExtractionResult
            {
                PageCount = pageCount,
                NormalText = structuredText,
                RawTextLength = textLength,
                HasEnoughText = hasEnoughText,
                HasEnoughRawText = hasEnoughText,
                IsScannedOrImagePdf = false,
                IsScanPdf = false,
                HasImages = false,
                TotalImageCount = 0,
                DetectedImageCount = 0,
                ProcessedImageCount = 0,
                ReadableImageCount = 0,
                RecognizedImageCount = 0,
                HasImageContent = false,
                SkippedImageCount = 0,
                FailedImageCount = 0,
                UnreadableImageCount = 0
            };

            // 4. Đóng gói kèm Metadata block để tương thích ngược với Views và Controllers
            var textWithMetadata = AppendMetadataBlock(structuredText, result);

            _logger.LogInformation(
                "PDF extraction completed for {FilePath}. PageCount={PageCount}, TextLength={TextLength}, HasEnoughText={HasEnoughText}",
                filePath,
                pageCount,
                textLength,
                hasEnoughText);

            return result with
            {
                Text = textWithMetadata,
                ExtractedText = textWithMetadata
            };
        }

        private static int GetPdfPageCount(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new StreamReader(fs, Encoding.ASCII);
                var content = reader.ReadToEnd();
                
                // Tìm kiếm mẫu /Type /Pages /Count X hoặc /Count X
                var matches = Regex.Matches(content, @"/Type\s*/Pages\s*/Count\s+(\d+)");
                if (matches.Count > 0)
                {
                    foreach (Match match in matches)
                    {
                        if (int.TryParse(match.Groups[1].Value, out var count))
                        {
                            return count;
                        }
                    }
                }

                // Fallback 1: Quét tất cả /Count X và lấy giá trị lớn nhất (thường là tổng số trang)
                var countMatches = Regex.Matches(content, @"/Count\s+(\d+)");
                if (countMatches.Count > 0)
                {
                    var maxCount = 0;
                    foreach (Match match in countMatches)
                    {
                        if (int.TryParse(match.Groups[1].Value, out var count))
                        {
                            if (count > maxCount)
                            {
                                maxCount = count;
                            }
                        }
                    }
                    if (maxCount > 0) return maxCount;
                }

                // Fallback 2: Đếm số lượng đối tượng /Page (nếu không tìm thấy /Count)
                var pageMatches = Regex.Matches(content, @"/Type\s*/Page\b");
                if (pageMatches.Count > 0)
                {
                    return pageMatches.Count;
                }
            }
            catch (Exception ex)
            {
                // Chỉ log cảnh báo, không gây lỗi hệ thống
                System.Diagnostics.Debug.WriteLine($"Error counting PDF pages: {ex.Message}");
            }

            return 1; // Mặc định nếu không phân tích được
        }

        private static string AppendMetadataBlock(string? text, PdfExtractionResult result)
        {
            var builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(text))
            {
                builder.AppendLine(text.Trim());
                builder.AppendLine();
            }

            builder.AppendLine(MetadataSectionHeading);
            builder.AppendLine($"PageCount={result.PageCount}");
            builder.AppendLine($"RawTextLength={result.RawTextLength}");
            builder.AppendLine($"HasEnoughRawText={result.HasEnoughRawText}");
            builder.AppendLine($"IsScanPdf={result.IsScanPdf}");
            builder.AppendLine($"TotalImageCount={result.TotalImageCount}");
            builder.AppendLine($"DetectedImageCount={result.DetectedImageCount}");
            builder.AppendLine($"ProcessedImageCount={result.ProcessedImageCount}");
            builder.AppendLine($"RecognizedImageCount={result.RecognizedImageCount}");
            builder.AppendLine($"HasImageContent={result.HasImageContent}");
            builder.AppendLine(MetadataSectionEndHeading);

            return builder.ToString().Trim();
        }
    }

    public sealed record PdfExtractionResult
    {
        public string? Text { get; init; }
        public int PageCount { get; init; }
        public string? NormalText { get; init; }
        public string? ExtractedText { get; init; }
        public int RawTextLength { get; init; }
        public bool HasEnoughText { get; init; }
        public bool HasEnoughRawText { get; init; }
        public bool IsScannedOrImagePdf { get; init; }
        public bool IsScanPdf { get; init; }
        public bool HasImages { get; init; }
        public int TotalImageCount { get; init; }
        public int DetectedImageCount { get; init; }
        public int ProcessedImageCount { get; init; }
        public int ReadableImageCount { get; init; }
        public int RecognizedImageCount { get; init; }
        public bool HasImageContent { get; init; }
        public int SkippedImageCount { get; init; }
        public int FailedImageCount { get; init; }
        public int UnreadableImageCount { get; init; }
    }
}
