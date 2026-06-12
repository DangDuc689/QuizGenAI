using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace QuizGenAI.Services
{
    public sealed class PdfExtractionService
    {
        public const int MaxImagesToProcess = 5;
        public const string NormalTextSectionHeading = "[Nội dung văn bản trích xuất từ PDF]";
        public const string ImageTextSectionHeading = "[Nội dung nhận diện từ hình ảnh trong PDF]";
        public const string MetadataSectionHeading = "[PDF_EXTRACTION_METADATA]";
        public const string MetadataSectionEndHeading = "[/PDF_EXTRACTION_METADATA]";
        public const string ScannedPdfMessage = "PDF có vẻ là dạng scan/ảnh hoặc văn bản trích xuất trực tiếp quá ít. Hệ thống đã thử nhận diện nội dung từ hình ảnh trong PDF.";
        public const string ImageUnreadableMessage = "PDF có chứa hình ảnh, nhưng hệ thống chưa nhận diện được đủ nội dung học tập từ các ảnh này. Bạn có thể thử dùng PDF rõ hơn hoặc bổ sung nội dung bằng Paste Text.";

        private static readonly HashSet<string> SupportedImageMimeTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/webp",
            "image/heic",
            "image/heif"
        };

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
            using var pdfDocument = PdfDocument.Open(filePath);

            var pageCount = pdfDocument.NumberOfPages;
            var pageTexts = new List<string>();
            var imageCandidates = new List<PdfImageCandidate>();

            foreach (var page in pdfDocument.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(page.Text))
                {
                    pageTexts.Add(page.Text.Trim());
                }

                foreach (var image in page.GetImages())
                {
                    imageCandidates.Add(new PdfImageCandidate(page.Number, image));
                }
            }

            var normalText = MergePageTexts(pageTexts);
            var hasEnoughText = HasEnoughTextContent(normalText);
            var isScannedOrImagePdf = !hasEnoughText;
            var totalImageCount = imageCandidates.Count;
            var imageTexts = new List<string>();
            var processedImageCount = 0;
            var skippedImageCount = 0;
            var failedImageCount = 0;
            var unreadableImageCount = 0;

            _logger.LogInformation(
                "PDF extraction opened {FilePath}. PageCount={PageCount}, TextLength={TextLength}, HasEnoughText={HasEnoughText}, ImageCount={ImageCount}, MaxImagesToProcess={MaxImagesToProcess}",
                filePath,
                pageCount,
                normalText?.Length ?? 0,
                hasEnoughText,
                totalImageCount,
                MaxImagesToProcess);

            if (totalImageCount > 0)
            {
                foreach (var candidate in SelectImportantImages(imageCandidates).Take(MaxImagesToProcess))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!TryGetImageBytes(candidate.Image, out var imageBytes, out var mimeType))
                    {
                        skippedImageCount++;
                        _logger.LogInformation(
                            "Skipped PDF image on page {PageNumber} because readable image bytes were not available. Width={Width}, Height={Height}, RawBytes={RawBytes}",
                            candidate.PageNumber,
                            candidate.Image.WidthInSamples,
                            candidate.Image.HeightInSamples,
                            candidate.Image.RawBytes.Length);
                        continue;
                    }

                    if (imageBytes.Length == 0 || !SupportedImageMimeTypes.Contains(mimeType))
                    {
                        skippedImageCount++;
                        _logger.LogInformation(
                            "Skipped unsupported PDF image on page {PageNumber}. MimeType={MimeType}, Bytes={Bytes}, Width={Width}, Height={Height}",
                            candidate.PageNumber,
                            mimeType,
                            imageBytes.Length,
                            candidate.Image.WidthInSamples,
                            candidate.Image.HeightInSamples);
                        continue;
                    }

                    if (IsSmallDecorativeImage(candidate.Image, imageBytes))
                    {
                        skippedImageCount++;
                        _logger.LogInformation(
                            "Skipped small decorative PDF image on page {PageNumber}. MimeType={MimeType}, Bytes={Bytes}, Width={Width}, Height={Height}",
                            candidate.PageNumber,
                            mimeType,
                            imageBytes.Length,
                            candidate.Image.WidthInSamples,
                            candidate.Image.HeightInSamples);
                        continue;
                    }

                    processedImageCount++;

                    try
                    {
                        _logger.LogInformation(
                            "Sending PDF image to Gemini Vision. PageNumber={PageNumber}, MimeType={MimeType}, Bytes={Bytes}, Width={Width}, Height={Height}",
                            candidate.PageNumber,
                            mimeType,
                            imageBytes.Length,
                            candidate.Image.WidthInSamples,
                            candidate.Image.HeightInSamples);

                        var imageText = await _geminiService.ExtractTextFromImageBytesAsync(
                            imageBytes,
                            mimeType,
                            cancellationToken);

                        if (IsUsefulImageText(imageText))
                        {
                            imageTexts.Add($"Trang {candidate.PageNumber}:\n{imageText.Trim()}");
                        }
                        else
                        {
                            unreadableImageCount++;
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failedImageCount++;
                        _logger.LogWarning(
                            ex,
                            "Could not extract text from PDF image on page {PageNumber}. MimeType={MimeType}, Bytes={Bytes}, Width={Width}, Height={Height}.",
                            candidate.PageNumber,
                            mimeType,
                            imageBytes.Length,
                            candidate.Image.WidthInSamples,
                            candidate.Image.HeightInSamples);
                    }
                }
            }

            var extractedText = MergeNormalTextAndImageText(
                normalText,
                imageTexts,
                totalImageCount,
                processedImageCount,
                skippedImageCount,
                failedImageCount,
                unreadableImageCount,
                isScannedOrImagePdf);

            var result = new PdfExtractionResult
            {
                PageCount = pageCount,
                NormalText = normalText,
                RawTextLength = normalText?.Length ?? 0,
                HasEnoughText = hasEnoughText,
                HasEnoughRawText = hasEnoughText,
                IsScannedOrImagePdf = isScannedOrImagePdf,
                IsScanPdf = isScannedOrImagePdf,
                HasImages = totalImageCount > 0,
                TotalImageCount = totalImageCount,
                DetectedImageCount = processedImageCount,
                ProcessedImageCount = processedImageCount,
                ReadableImageCount = imageTexts.Count,
                RecognizedImageCount = imageTexts.Count,
                HasImageContent = imageTexts.Count > 0,
                SkippedImageCount = skippedImageCount,
                FailedImageCount = failedImageCount,
                UnreadableImageCount = unreadableImageCount
            };

            var textWithMetadata = AppendMetadataBlock(extractedText, result);

            return result with
            {
                Text = textWithMetadata,
                ExtractedText = textWithMetadata
            };
        }

        private static string? MergePageTexts(IReadOnlyCollection<string> pageTexts)
        {
            if (pageTexts.Count == 0)
            {
                return null;
            }

            var text = string.Join(Environment.NewLine + Environment.NewLine, pageTexts.Select(t => t.Trim())).Trim();

            return string.IsNullOrWhiteSpace(text)
                ? null
                : text;
        }

        private static string? MergeNormalTextAndImageText(
            string? normalText,
            IReadOnlyCollection<string> imageTexts,
            int totalImageCount,
            int processedImageCount,
            int skippedImageCount,
            int failedImageCount,
            int unreadableImageCount,
            bool isScannedOrImagePdf)
        {
            if (totalImageCount == 0 && !isScannedOrImagePdf)
            {
                return normalText;
            }

            if (totalImageCount == 0 && isScannedOrImagePdf)
            {
                return string.IsNullOrWhiteSpace(normalText)
                    ? ScannedPdfMessage
                    : normalText.Trim();
            }

            var builder = new StringBuilder();

            builder.AppendLine(NormalTextSectionHeading);
            builder.AppendLine(!string.IsNullOrWhiteSpace(normalText)
                ? normalText.Trim()
                : "(Không có nội dung văn bản trực tiếp được trích xuất từ PDF.)");
            builder.AppendLine();

            if (isScannedOrImagePdf)
            {
                builder.AppendLine(ScannedPdfMessage);
                builder.AppendLine();
            }

            builder.AppendLine(ImageTextSectionHeading);

            if (imageTexts.Count > 0)
            {
                var imageIndex = 1;
                foreach (var imageText in imageTexts)
                {
                    builder.AppendLine($"Ảnh {imageIndex}:");
                    builder.AppendLine(imageText.Trim());
                    builder.AppendLine();
                    imageIndex++;
                }
            }
            else
            {
                builder.AppendLine(ImageUnreadableMessage);
                builder.AppendLine();
            }

            AppendImageProcessingNotes(
                builder,
                totalImageCount,
                processedImageCount,
                skippedImageCount,
                failedImageCount,
                unreadableImageCount);

            return builder.ToString().Trim();
        }

        private static void AppendImageProcessingNotes(
            StringBuilder builder,
            int totalImageCount,
            int processedImageCount,
            int skippedImageCount,
            int failedImageCount,
            int unreadableImageCount)
        {
            if (totalImageCount > MaxImagesToProcess)
            {
                builder.AppendLine();
                builder.AppendLine($"Ghi chú: PDF có {totalImageCount} ảnh, hệ thống chỉ xử lý tối đa {MaxImagesToProcess} ảnh quan trọng để tránh timeout/quota.");
            }

            if (skippedImageCount > 0 || failedImageCount > 0 || unreadableImageCount > 0)
            {
                builder.AppendLine();
                builder.AppendLine($"Ghi chú kỹ thuật: đã gửi {processedImageCount} ảnh cho AI; bỏ qua {skippedImageCount} ảnh nhỏ/không đọc được; {failedImageCount} ảnh lỗi khi nhận diện.");
            }
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

        private static IEnumerable<PdfImageCandidate> SelectImportantImages(IEnumerable<PdfImageCandidate> candidates)
        {
            return candidates
                .Where(candidate => !candidate.Image.IsImageMask)
                .OrderBy(candidate => candidate.PageNumber)
                .ThenByDescending(candidate => GetImageArea(candidate.Image));
        }

        private static bool TryGetImageBytes(IPdfImage image, out byte[] imageBytes, out string mimeType)
        {
            if (image.TryGetPng(out var pngBytes) && pngBytes.Length > 0)
            {
                imageBytes = pngBytes;
                mimeType = "image/png";
                return true;
            }

            var rawBytes = image.RawBytes.ToArray();
            var detectedMimeType = DetectMimeTypeFromBytes(rawBytes);

            if (!string.IsNullOrWhiteSpace(detectedMimeType))
            {
                imageBytes = rawBytes;
                mimeType = detectedMimeType;
                return imageBytes.Length > 0;
            }

            imageBytes = Array.Empty<byte>();
            mimeType = "application/octet-stream";
            return false;
        }

        private static bool HasEnoughTextContent(string? text)
        {
            return CountUsefulWords(text) >= 30
                || HasMathTextSignal(text ?? string.Empty);
        }

        private static bool IsUsefulImageText(string? imageText)
        {
            if (string.IsNullOrWhiteSpace(imageText))
            {
                return false;
            }

            if (imageText.Contains("KHONG_DOC_DUOC_NOI_DUNG_ANH", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return CountUsefulWords(imageText) >= 3
                || HasMathTextSignal(imageText);
        }

        private static int CountUsefulWords(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            return Regex.Matches(text, @"\b[\p{L}\p{N}]{2,}\b").Count;
        }

        private static bool HasMathTextSignal(string text)
        {
            return Regex.IsMatch(
                text,
                @"[\p{L}\p{N}]\s*[=+\-*/÷×]\s*[\p{L}\p{N}]|\\frac\s*\{|[¼½¾⅓⅔⅕⅖⅗⅘⅙⅚⅛⅜⅝⅞]|[\p{L}\p{N}]\s*\^\s*\d+|[⁰¹²³⁴⁵⁶⁷⁸⁹]|√|\\sqrt|π|∞|≤|≥|≠|≈|∑|∫|∆|Δ|\b(?:sin|cos|tan|log|ln)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static bool IsSmallDecorativeImage(IPdfImage image, byte[] imageBytes)
        {
            return image.WidthInSamples < 100
                && image.HeightInSamples < 100
                && imageBytes.Length < 40_000;
        }

        private static double GetImageArea(IPdfImage image)
        {
            var width = image.WidthInSamples > 0 ? image.WidthInSamples : image.Bounds.Width;
            var height = image.HeightInSamples > 0 ? image.HeightInSamples : image.Bounds.Height;

            return width * height;
        }

        private static string? DetectMimeTypeFromBytes(byte[] bytes)
        {
            if (bytes.Length >= 12 &&
                bytes[0] == 0x52 &&
                bytes[1] == 0x49 &&
                bytes[2] == 0x46 &&
                bytes[3] == 0x46 &&
                bytes[8] == 0x57 &&
                bytes[9] == 0x45 &&
                bytes[10] == 0x42 &&
                bytes[11] == 0x50)
            {
                return "image/webp";
            }

            if (bytes.Length >= 24 &&
                bytes[0] == 0x89 &&
                bytes[1] == 0x50 &&
                bytes[2] == 0x4E &&
                bytes[3] == 0x47)
            {
                return "image/png";
            }

            if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8)
            {
                return "image/jpeg";
            }

            if (bytes.Length >= 12 &&
                bytes[4] == 0x66 &&
                bytes[5] == 0x74 &&
                bytes[6] == 0x79 &&
                bytes[7] == 0x70)
            {
                var brand = Encoding.ASCII.GetString(bytes, 8, 4).ToLowerInvariant();
                return brand.StartsWith("heic", StringComparison.Ordinal) ||
                       brand.StartsWith("heix", StringComparison.Ordinal) ||
                       brand.StartsWith("hevc", StringComparison.Ordinal) ||
                       brand.StartsWith("hevx", StringComparison.Ordinal)
                    ? "image/heic"
                    : brand.StartsWith("mif1", StringComparison.Ordinal) ||
                      brand.StartsWith("msf1", StringComparison.Ordinal)
                        ? "image/heif"
                        : null;
            }

            return null;
        }

        private sealed record PdfImageCandidate(int PageNumber, IPdfImage Image);
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
