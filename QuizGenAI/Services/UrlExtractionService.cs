using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace QuizGenAI.Services
{
    public sealed class UrlExtractionService
    {
        public const int MinimumUsefulWordsForUrl = 80;
        public const int MaxDownloadBytes = 2 * 1024 * 1024;

        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(12);

        private readonly HttpClient _httpClient;
        private readonly ILogger<UrlExtractionService> _logger;

        public UrlExtractionService(HttpClient httpClient, ILogger<UrlExtractionService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<UrlExtractionResult> ExtractAsync(
            string? url,
            CancellationToken cancellationToken = default)
        {
            if (!TryNormalizeUrl(url, out var uri, out var validationMessage))
            {
                return UrlExtractionResult.Fail(validationMessage);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(RequestTimeout);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.ParseAdd("QuizGenAI/1.0 (+https://localhost)");

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutCts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    return UrlExtractionResult.Fail(
                        $"Không tải được nội dung từ liên kết này. Máy chủ trả về {(int)response.StatusCode} {response.ReasonPhrase}.");
                }

                var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? string.Empty;
                var contentLength = response.Content.Headers.ContentLength;

                if (contentLength > MaxDownloadBytes)
                {
                    return UrlExtractionResult.Fail("Liên kết có nội dung quá lớn để trích xuất trực tiếp. Bạn có thể upload file hoặc dùng Paste Text.");
                }

                if (contentType == "application/pdf")
                {
                    return UrlExtractionResult.Fail("Hiện tại hệ thống chưa trích xuất trực tiếp PDF từ URL. Bạn có thể tải PDF về và upload file PDF.");
                }

                if (!IsHtmlContentType(contentType))
                {
                    return UrlExtractionResult.Fail("Liên kết này không trả về nội dung HTML hỗ trợ trích xuất. Bạn có thể mở link gốc và dùng Paste Text.");
                }

                var html = await ReadLimitedStringAsync(response.Content, timeoutCts.Token);
                var extractedText = ExtractTextFromHtml(html);
                var usefulWords = CountUsefulWords(extractedText);

                if (usefulWords < MinimumUsefulWordsForUrl)
                {
                    return UrlExtractionResult.Fail(
                        "Không lấy được nội dung học tập đủ rõ ràng từ liên kết này. Bạn có thể mở link gốc, copy nội dung và dùng Paste Text hoặc upload file.",
                        extractedText,
                        contentType,
                        usefulWords);
                }

                return UrlExtractionResult.Succeeded(extractedText, contentType, usefulWords);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return UrlExtractionResult.Fail("Quá thời gian tải nội dung từ liên kết. Vui lòng thử lại hoặc dùng Paste Text/upload file.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not extract text from URL {Url}", uri);
                return UrlExtractionResult.Fail("Không thể trích xuất nội dung từ liên kết này. Bạn có thể mở link gốc và dùng Paste Text.");
            }
        }

        private static bool TryNormalizeUrl(
            string? url,
            out Uri uri,
            out string message)
        {
            uri = null!;
            message = string.Empty;

            if (string.IsNullOrWhiteSpace(url))
            {
                message = "Vui lòng nhập đường dẫn URL.";
                return false;
            }

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri!))
            {
                message = "URL không hợp lệ. Vui lòng nhập liên kết đầy đủ bắt đầu bằng http:// hoặc https://.";
                return false;
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                message = "Chỉ hỗ trợ liên kết http:// hoặc https://.";
                return false;
            }

            return true;
        }

        private static bool IsHtmlContentType(string contentType)
        {
            return string.IsNullOrWhiteSpace(contentType)
                || contentType == "text/html"
                || contentType == "application/xhtml+xml";
        }

        private static async Task<string> ReadLimitedStringAsync(
            HttpContent content,
            CancellationToken cancellationToken)
        {
            await using var stream = await content.ReadAsStreamAsync(cancellationToken);
            using var memoryStream = new MemoryStream();
            var buffer = new byte[81920];
            var totalBytes = 0;

            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                totalBytes += read;
                if (totalBytes > MaxDownloadBytes)
                {
                    throw new InvalidOperationException("Downloaded content exceeded the maximum allowed size.");
                }

                memoryStream.Write(buffer, 0, read);
            }

            return Encoding.UTF8.GetString(memoryStream.ToArray());
        }

        private static string ExtractTextFromHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            var htmlDocument = new HtmlDocument();
            htmlDocument.LoadHtml(html);

            var nodesToRemove = htmlDocument.DocumentNode.SelectNodes(
                "//script|//style|//nav|//footer|//header|//noscript|//svg|//canvas|//form");

            if (nodesToRemove != null)
            {
                foreach (var node in nodesToRemove)
                {
                    node.Remove();
                }
            }

            var contentNode = htmlDocument.DocumentNode.SelectSingleNode("//article")
                ?? htmlDocument.DocumentNode.SelectSingleNode("//main")
                ?? htmlDocument.DocumentNode.SelectSingleNode("//body")
                ?? htmlDocument.DocumentNode;

            return NormalizeText(WebUtility.HtmlDecode(contentNode.InnerText));
        }

        private static string NormalizeText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var normalized = Regex.Replace(text, @"[ \t\f\v]+", " ");
            normalized = Regex.Replace(normalized, @"\s*\r?\n\s*", "\n");
            normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");

            return normalized.Trim();
        }

        private static int CountUsefulWords(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            return Regex.Matches(text, @"\b[\p{L}\p{N}]{2,}\b").Count;
        }
    }

    public sealed class UrlExtractionResult
    {
        public bool Success { get; init; }
        public string? ExtractedText { get; init; }
        public string? Message { get; init; }
        public string? ContentType { get; init; }
        public int UsefulWordCount { get; init; }

        public static UrlExtractionResult Succeeded(
            string extractedText,
            string? contentType,
            int usefulWordCount)
        {
            return new UrlExtractionResult
            {
                Success = true,
                ExtractedText = extractedText,
                ContentType = contentType,
                UsefulWordCount = usefulWordCount
            };
        }

        public static UrlExtractionResult Fail(
            string message,
            string? extractedText = null,
            string? contentType = null,
            int usefulWordCount = 0)
        {
            return new UrlExtractionResult
            {
                Success = false,
                ExtractedText = extractedText,
                Message = message,
                ContentType = contentType,
                UsefulWordCount = usefulWordCount
            };
        }
    }
}
