using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace QuizGenAI.Services
{
    public sealed class DocxExtractionService
    {
        public const int MaxImagesToProcess = 20;
        public const string NormalTextSectionHeading = "[Nội dung văn bản trích xuất từ Word]";
        public const string ImageTextSectionHeading = "[Nội dung nhận diện từ hình ảnh trong tài liệu]";
        public const string WordTableStartMarker = "[WORD_TABLE]";
        public const string WordTableEndMarker = "[/WORD_TABLE]";
        public const string ImageUnreadableMessage = "Tài liệu có chứa hình ảnh, nhưng hệ thống chưa nhận diện được đủ nội dung học tập từ các ảnh này. Bạn có thể thử dùng ảnh rõ hơn hoặc bổ sung nội dung bằng Paste Text.";
        public const string ImageVisionTroubleshootingMessage = "Nếu ảnh rõ chữ nhưng vẫn không nhận diện được, vui lòng kiểm tra model Gemini hiện tại có hỗ trợ Vision không và xem log backend để xác nhận request ảnh đã được Gemini chấp nhận.";

        private static readonly HashSet<string> SupportedImageMimeTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/webp",
            "image/heic",
            "image/heif"
        };

        private readonly GeminiService _geminiService;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<DocxExtractionService> _logger;

        public DocxExtractionService(
            GeminiService geminiService,
            IWebHostEnvironment environment,
            ILogger<DocxExtractionService> logger)
        {
            _geminiService = geminiService;
            _environment = environment;
            _logger = logger;
        }

        public async Task<DocxExtractionResult> ExtractTextFromDocxAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            var normalText = ExtractTextFromWord(filePath);
            var imageTexts = new List<string>();
            var totalImageCount = 0;
            var processedImageCount = 0;
            var skippedImageCount = 0;
            var failedImageCount = 0;
            var unreadableImageCount = 0;
            var pageCount = 1;

            try
            {
                using var wordDocument = WordprocessingDocument.Open(filePath, false);
                pageCount = EstimateWordPageCount(wordDocument, normalText);

                var mainPart = wordDocument.MainDocumentPart;

                if (mainPart != null)
                {
                    var imageParts = mainPart.ImageParts.ToList();
                    totalImageCount = imageParts.Count;
                    var seenImageFingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    _logger.LogInformation(
                        "DOCX image scan found {TotalImageCount} ImageParts in {FilePath}. MaxImagesToProcess={MaxImagesToProcess}",
                        totalImageCount,
                        filePath,
                        MaxImagesToProcess);

                    var validImages = new List<ImageExtractionTask>();
                    var imageIndex = 0;

                    foreach (var imagePart in imageParts.Take(MaxImagesToProcess))
                    {
                        imageIndex++;
                        cancellationToken.ThrowIfCancellationRequested();

                        var originalContentType = imagePart.ContentType;
                        var mimeType = NormalizeImageMimeType(originalContentType);

                        byte[] imageBytes;
                        await using (var imageStream = imagePart.GetStream(FileMode.Open, FileAccess.Read))
                        using (var memoryStream = new MemoryStream())
                        {
                            await imageStream.CopyToAsync(memoryStream, cancellationToken);
                            imageBytes = memoryStream.ToArray();
                        }

                        if (imageBytes.Length == 0)
                        {
                            skippedImageCount++;
                            _logger.LogWarning(
                                "Skipped DOCX image {ImageIndex} because extracted bytes are empty. ContentType={ContentType}",
                                imageIndex,
                                originalContentType);
                            continue;
                        }

                        if (!TryMarkUniqueImage(imageBytes, seenImageFingerprints))
                        {
                            skippedImageCount++;
                            _logger.LogInformation(
                                "Skipped duplicate DOCX image {ImageIndex}. ContentType={ContentType}, Bytes={Bytes}",
                                imageIndex,
                                originalContentType,
                                imageBytes.Length);
                            continue;
                        }

                        mimeType = DetectMimeTypeFromBytes(imageBytes) ?? mimeType;
                        var hasDimensions = TryGetImageDimensions(imageBytes, out var width, out var height);

                        if (!SupportedImageMimeTypes.Contains(mimeType))
                        {
                            skippedImageCount++;
                            _logger.LogInformation(
                                "Skipped unsupported DOCX image {ImageIndex}. ContentType={ContentType}, DetectedMimeType={MimeType}, Bytes={Bytes}",
                                imageIndex,
                                originalContentType,
                                mimeType,
                                imageBytes.Length);
                            continue;
                        }

                        await SaveDebugImageIfDevelopmentAsync(
                            imageBytes,
                            mimeType,
                            imageIndex,
                            cancellationToken);

                        if (IsSmallDecorativeImage(imageBytes))
                        {
                            skippedImageCount++;
                            _logger.LogInformation(
                                "Skipped small decorative DOCX image {ImageIndex}. MimeType={MimeType}, Bytes={Bytes}, Width={Width}, Height={Height}",
                                imageIndex,
                                mimeType,
                                imageBytes.Length,
                                width,
                                height);
                            continue;
                        }

                        validImages.Add(new ImageExtractionTask
                        {
                            ImageIndex = imageIndex,
                            ImageBytes = imageBytes,
                            MimeType = mimeType,
                            Width = width,
                            Height = height,
                            HasDimensions = hasDimensions
                        });
                    }

                    if (validImages.Count > 0)
                    {
                        using var semaphore = new SemaphoreSlim(4);
                        var imageTextResults = new System.Collections.Concurrent.ConcurrentBag<(int Index, string Text)>();

                        var tasks = validImages.Select(async img =>
                        {
                            await semaphore.WaitAsync(cancellationToken);
                            try
                            {
                                Interlocked.Increment(ref processedImageCount);

                                _logger.LogInformation(
                                    "Sending DOCX image {ImageIndex} to Gemini Vision (Parallel). MimeType={MimeType}, Bytes={Bytes}, Width={Width}, Height={Height}",
                                    img.ImageIndex,
                                    img.MimeType,
                                    img.ImageBytes.Length,
                                    img.HasDimensions ? img.Width : (int?)null,
                                    img.HasDimensions ? img.Height : (int?)null);

                                var imageText = await _geminiService.ExtractTextFromImageBytesAsync(
                                    img.ImageBytes,
                                    img.MimeType,
                                    cancellationToken);

                                var isUsefulImageText = IsUsefulImageText(imageText);

                                _logger.LogInformation(
                                    "Gemini Vision result for DOCX image {ImageIndex} (Parallel). IsUseful={IsUsefulImageText}, Text={ImageText}",
                                    img.ImageIndex,
                                    isUsefulImageText,
                                    TruncateForLog(imageText));

                                if (isUsefulImageText)
                                {
                                    imageTextResults.Add((img.ImageIndex, imageText.Trim()));
                                }
                                else
                                {
                                    Interlocked.Increment(ref unreadableImageCount);
                                }
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                Interlocked.Increment(ref failedImageCount);
                                _logger.LogWarning(
                                    ex,
                                    "Could not extract text from DOCX image {ImageIndex} (Parallel). MimeType={MimeType}, Bytes={Bytes}, Width={Width}, Height={Height}.",
                                    img.ImageIndex,
                                    img.MimeType,
                                    img.ImageBytes.Length,
                                    img.HasDimensions ? img.Width : (int?)null,
                                    img.HasDimensions ? img.Height : (int?)null);
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        });

                        await Task.WhenAll(tasks);

                        // Sắp xếp lại kết quả theo thứ tự ảnh xuất hiện trong tài liệu Word
                        var sortedTexts = imageTextResults
                            .OrderBy(r => r.Index)
                            .Select(r => r.Text)
                            .ToList();

                        imageTexts.AddRange(sortedTexts);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not inspect embedded images in DOCX file {FilePath}.", filePath);
            }

            var combinedText = MergeNormalTextAndImageText(
                normalText,
                imageTexts,
                totalImageCount,
                processedImageCount,
                skippedImageCount,
                failedImageCount,
                unreadableImageCount);

            return new DocxExtractionResult
            {
                NormalText = normalText,
                ExtractedText = combinedText,
                ImageExtractedText = imageTexts.Count > 0
                    ? BuildImageTextSection(imageTexts, totalImageCount, processedImageCount, skippedImageCount, failedImageCount, unreadableImageCount)
                    : null,
                HasImages = totalImageCount > 0,
                TotalImageCount = totalImageCount,
                ProcessedImageCount = processedImageCount,
                ReadableImageCount = imageTexts.Count,
                SkippedImageCount = skippedImageCount,
                FailedImageCount = failedImageCount,
                UnreadableImageCount = unreadableImageCount,
                PageCount = pageCount
            };
        }

        private static string? ExtractTextFromWord(string filePath)
        {
            using var wordDocument = WordprocessingDocument.Open(filePath, false);
            var mainPart = wordDocument.MainDocumentPart;

            if (mainPart?.Document?.Body == null)
            {
                return null;
            }

            var textBuilder = new StringBuilder();

            foreach (var element in mainPart.Document.Body.Elements())
            {
                if (element is Paragraph paragraph)
                {
                    AppendParagraphText(textBuilder, paragraph);
                    continue;
                }

                if (element is Table table)
                {
                    var tableText = ExtractTableText(table);

                    if (!string.IsNullOrWhiteSpace(tableText))
                    {
                        textBuilder.AppendLine(tableText);
                        textBuilder.AppendLine();
                    }
                }
            }

            var extractedText = textBuilder.ToString().Trim();

            return string.IsNullOrWhiteSpace(extractedText)
                ? null
                : extractedText;
        }

        private static void AppendParagraphText(StringBuilder textBuilder, Paragraph paragraph)
        {
            var paragraphText = ExtractParagraphText(paragraph);

            if (string.IsNullOrWhiteSpace(paragraphText))
            {
                return;
            }

            if (IsListParagraph(paragraph))
            {
                var level = paragraph.ParagraphProperties?
                    .NumberingProperties?
                    .NumberingLevelReference?
                    .Val?
                    .Value ?? 0;
                textBuilder.Append(new string(' ', Math.Min(level, 4) * 2));
                textBuilder.Append("- ");
            }

            textBuilder.AppendLine(paragraphText);
            textBuilder.AppendLine();
        }

        private static string? ExtractTableText(Table table)
        {
            var rows = new List<IReadOnlyList<string>>();
            var maxColumnCount = 0;

            foreach (var tableRow in table.Elements<TableRow>())
            {
                var cells = tableRow
                    .Elements<TableCell>()
                    .Select(ExtractTableCellText)
                    .ToList();

                if (cells.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                maxColumnCount = Math.Max(maxColumnCount, cells.Count);
                rows.Add(cells);
            }

            if (rows.Count == 0 || maxColumnCount == 0)
            {
                return null;
            }

            var builder = new StringBuilder();
            builder.AppendLine(WordTableStartMarker);

            foreach (var row in rows)
            {
                builder.Append("| ");

                for (var columnIndex = 0; columnIndex < maxColumnCount; columnIndex++)
                {
                    var cellText = columnIndex < row.Count
                        ? EscapeTableCellText(row[columnIndex])
                        : string.Empty;

                    builder.Append(cellText);
                    builder.Append(" |");

                    if (columnIndex < maxColumnCount - 1)
                    {
                        builder.Append(' ');
                    }
                }

                builder.AppendLine();
            }

            builder.AppendLine(WordTableEndMarker);

            return builder.ToString().Trim();
        }

        private static string ExtractTableCellText(TableCell cell)
        {
            var paragraphTexts = cell
                .Elements<Paragraph>()
                .Select(ExtractParagraphText)
                .Select(NormalizeTableCellText)
                .Where(text => !string.IsNullOrWhiteSpace(text));

            return string.Join(" ", paragraphTexts).Trim();
        }

        private static string NormalizeTableCellText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        private static string EscapeTableCellText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return NormalizeTableCellText(text)
                .Replace("|", " / ", StringComparison.Ordinal);
        }

        private static bool TryMarkUniqueImage(byte[] imageBytes, HashSet<string> seenImageFingerprints)
        {
            var fingerprint = Convert.ToHexString(SHA256.HashData(imageBytes));
            return seenImageFingerprints.Add(fingerprint);
        }

        private static string ExtractParagraphText(Paragraph paragraph)
        {
            var builder = new StringBuilder();

            foreach (var element in paragraph.Descendants())
            {
                switch (element)
                {
                    case Text text:
                        builder.Append(text.Text);
                        break;
                    case TabChar:
                        builder.Append(' ');
                        break;
                    case Break:
                    case CarriageReturn:
                        builder.AppendLine();
                        break;
                }
            }

            var lines = builder
                .ToString()
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n')
                .Select(line => Regex.Replace(line, @"[ \t\f\v]+", " ").Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line));

            return string.Join(Environment.NewLine, lines).Trim();
        }

        private static bool IsListParagraph(Paragraph paragraph)
        {
            return paragraph.ParagraphProperties?.NumberingProperties != null;
        }

        private static string? MergeNormalTextAndImageText(
            string? normalText,
            IReadOnlyCollection<string> imageTexts,
            int totalImageCount,
            int processedImageCount,
            int skippedImageCount,
            int failedImageCount,
            int unreadableImageCount)
        {
            if (totalImageCount == 0)
            {
                return normalText;
            }

            var builder = new StringBuilder();

            builder.AppendLine(NormalTextSectionHeading);
            builder.AppendLine(!string.IsNullOrWhiteSpace(normalText)
                ? normalText.Trim()
                : "(Không có nội dung văn bản thường được trích xuất từ Word.)");
            builder.AppendLine();
            builder.AppendLine(ImageTextSectionHeading);

            if (imageTexts.Count > 0)
            {
                builder.Append(BuildImageTextSection(imageTexts, totalImageCount, processedImageCount, skippedImageCount, failedImageCount, unreadableImageCount));
            }
            else
            {
                builder.AppendLine(ImageUnreadableMessage);
                builder.AppendLine(ImageVisionTroubleshootingMessage);
                AppendImageProcessingNotes(builder, totalImageCount, processedImageCount, skippedImageCount, failedImageCount, unreadableImageCount);
            }

            return builder.ToString().Trim();
        }

        private static string BuildImageTextSection(
            IReadOnlyCollection<string> imageTexts,
            int totalImageCount,
            int processedImageCount,
            int skippedImageCount,
            int failedImageCount,
            int unreadableImageCount)
        {
            var builder = new StringBuilder();
            var imageIndex = 1;

            foreach (var imageText in imageTexts)
            {
                builder.AppendLine($"Ảnh {imageIndex}:");
                builder.AppendLine(imageText.Trim());
                builder.AppendLine();
                imageIndex++;
            }

            AppendImageProcessingNotes(builder, totalImageCount, processedImageCount, skippedImageCount, failedImageCount, unreadableImageCount);

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
                builder.AppendLine($"Ghi chú: tài liệu có {totalImageCount} ảnh, hệ thống chỉ xử lý tối đa {MaxImagesToProcess} ảnh đầu tiên để tránh timeout/quota.");
            }

            if (skippedImageCount > 0 || failedImageCount > 0 || unreadableImageCount > 0)
            {
                builder.AppendLine();
                builder.AppendLine($"Ghi chú kỹ thuật: đã gửi {processedImageCount} ảnh cho AI; bỏ qua {skippedImageCount} ảnh nhỏ/không hỗ trợ; {failedImageCount} ảnh lỗi khi nhận diện.");
            }
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

        private static string TruncateForLog(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var normalized = Regex.Replace(text.Trim(), @"\s+", " ");

            return normalized.Length <= 500
                ? normalized
                : normalized[..500] + "...";
        }

        private async Task SaveDebugImageIfDevelopmentAsync(
            byte[] imageBytes,
            string mimeType,
            int imageIndex,
            CancellationToken cancellationToken)
        {
            if (!_environment.IsDevelopment())
            {
                return;
            }

            try
            {
                var webRootPath = _environment.WebRootPath;
                if (string.IsNullOrWhiteSpace(webRootPath))
                {
                    webRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                }

                var debugFolder = Path.Combine(webRootPath, "uploads", "debug-docx-images");
                Directory.CreateDirectory(debugFolder);

                var extension = GetFileExtensionForMimeType(mimeType);
                var fileName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-docx-image-{imageIndex}{extension}";
                var fullPath = Path.Combine(debugFolder, fileName);

                await File.WriteAllBytesAsync(fullPath, imageBytes, cancellationToken);

                _logger.LogInformation(
                    "Saved DOCX debug image {ImageIndex} to {DebugImagePath}. MimeType={MimeType}, Bytes={Bytes}",
                    imageIndex,
                    fullPath,
                    mimeType,
                    imageBytes.Length);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not save DOCX debug image {ImageIndex}.", imageIndex);
            }
        }

        private static string NormalizeImageMimeType(string? contentType)
        {
            return contentType?.Trim().ToLowerInvariant() switch
            {
                "image/jpg" => "image/jpeg",
                "image/pjpeg" => "image/jpeg",
                "image/x-png" => "image/png",
                var mime when !string.IsNullOrWhiteSpace(mime) => mime,
                _ => "application/octet-stream"
            };
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

        private static string GetFileExtensionForMimeType(string mimeType)
        {
            return mimeType.ToLowerInvariant() switch
            {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/webp" => ".webp",
                "image/heic" => ".heic",
                "image/heif" => ".heif",
                _ => ".bin"
            };
        }

        private static bool IsSmallDecorativeImage(byte[] imageBytes)
        {
            return TryGetImageDimensions(imageBytes, out var width, out var height)
                && width < 100
                && height < 100;
        }

        private static bool TryGetImageDimensions(byte[] bytes, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (bytes.Length >= 24 &&
                bytes[0] == 0x89 &&
                bytes[1] == 0x50 &&
                bytes[2] == 0x4E &&
                bytes[3] == 0x47)
            {
                width = ReadBigEndianInt32(bytes, 16);
                height = ReadBigEndianInt32(bytes, 20);
                return width > 0 && height > 0;
            }

            if (bytes.Length >= 10 &&
                bytes[0] == 0x47 &&
                bytes[1] == 0x49 &&
                bytes[2] == 0x46)
            {
                width = bytes[6] | (bytes[7] << 8);
                height = bytes[8] | (bytes[9] << 8);
                return width > 0 && height > 0;
            }

            if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8)
            {
                var index = 2;

                while (index + 9 < bytes.Length)
                {
                    if (bytes[index] != 0xFF)
                    {
                        index++;
                        continue;
                    }

                    var marker = bytes[index + 1];
                    var segmentLength = (bytes[index + 2] << 8) + bytes[index + 3];

                    if (segmentLength < 2 || index + 2 + segmentLength > bytes.Length)
                    {
                        break;
                    }

                    if (marker is >= 0xC0 and <= 0xC3 or >= 0xC5 and <= 0xC7 or >= 0xC9 and <= 0xCB or >= 0xCD and <= 0xCF)
                    {
                        height = (bytes[index + 5] << 8) + bytes[index + 6];
                        width = (bytes[index + 7] << 8) + bytes[index + 8];
                        return width > 0 && height > 0;
                    }

                    index += 2 + segmentLength;
                }
            }

            return false;
        }

        private static int ReadBigEndianInt32(byte[] bytes, int startIndex)
        {
            return (bytes[startIndex] << 24)
                | (bytes[startIndex + 1] << 16)
                | (bytes[startIndex + 2] << 8)
                | bytes[startIndex + 3];
        }

        private static int EstimateWordPageCount(WordprocessingDocument wordDocument, string? normalText)
        {
            try
            {
                var mainPart = wordDocument.MainDocumentPart;
                if (mainPart == null) return 1;

                // 1. Kiểm tra số trang trong Extended File Properties (metadata cached)
                var pagesText = wordDocument.ExtendedFilePropertiesPart?.Properties?.Pages?.InnerText;
                int pageCount = 0;
                if (int.TryParse(pagesText, out var parsedPages) && parsedPages > 0)
                {
                    pageCount = parsedPages;
                }

                // 2. Đếm các thẻ ngắt trang trong XML (lastRenderedPageBreak và manual breaks)
                var body = mainPart.Document?.Body;
                if (body != null)
                {
                    var lastRenderedBreaks = body.Descendants<LastRenderedPageBreak>().Count();
                    var manualBreaks = body.Descendants<Break>().Count(b => b.Type != null && b.Type.Value == BreakValues.Page);

                    var breakBasedPages = Math.Max(lastRenderedBreaks, manualBreaks) + 1;
                    pageCount = Math.Max(pageCount, breakBasedPages);
                }

                // 3. Ước lượng dựa trên Word Count từ metadata hoặc đếm từ thực tế
                if (pageCount <= 1)
                {
                    var wordsText = wordDocument.ExtendedFilePropertiesPart?.Properties?.Words?.InnerText;
                    if (int.TryParse(wordsText, out var parsedWords) && parsedWords > 0)
                    {
                        int estimatedPages = (int)Math.Ceiling(parsedWords / 400.0);
                        pageCount = Math.Max(pageCount, estimatedPages);
                    }
                    else if (!string.IsNullOrEmpty(normalText))
                    {
                        var wordCount = normalText.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
                        int estimatedPages = (int)Math.Ceiling(wordCount / 400.0);
                        pageCount = Math.Max(pageCount, estimatedPages);
                    }
                }

                return pageCount > 0 ? pageCount : 1;
            }
            catch
            {
                return 1;
            }
        }

        private sealed class ImageExtractionTask
        {
            public int ImageIndex { get; set; }
            public required byte[] ImageBytes { get; set; }
            public required string MimeType { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public bool HasDimensions { get; set; }
        }
    }

    public sealed class DocxExtractionResult
    {
        public string? NormalText { get; init; }
        public string? ExtractedText { get; init; }
        public string? ImageExtractedText { get; init; }
        public bool HasImages { get; init; }
        public int TotalImageCount { get; init; }
        public int ProcessedImageCount { get; init; }
        public int ReadableImageCount { get; init; }
        public int SkippedImageCount { get; init; }
        public int FailedImageCount { get; init; }
        public int UnreadableImageCount { get; init; }
        public int PageCount { get; init; }
    }
}
