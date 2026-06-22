using System.Globalization;
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
        public const string UrlTableStartMarker = "[URL_TABLE]";
        public const string UrlTableEndMarker = "[/URL_TABLE]";

        private const int MaxExtractedTableRows = 30;
        private const int MaxExtractedTableColumns = 8;

        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(12);

        private static readonly string[] MainContentSelectors =
        {
            "//article",
            "//main",
            "//*[@id='mw-content-text']",
            "//*[contains(concat(' ', normalize-space(@class), ' '), ' mw-parser-output ')]",
            "//*[contains(translate(@id, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'main-content')]",
            "//*[contains(translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'main-content')]",
            "//*[contains(translate(@id, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'article-content')]",
            "//*[contains(translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'article-content')]",
            "//*[contains(translate(@id, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'post-content')]",
            "//*[contains(translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'post-content')]",
            "//*[contains(translate(@id, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'entry-content')]",
            "//*[contains(translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'entry-content')]",
            "//*[contains(concat(' ', translate(normalize-space(@id), 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), ' '), ' content ')]",
            "//*[contains(concat(' ', translate(normalize-space(@class), 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), ' '), ' content ')]"
        };

        private static readonly HashSet<string> MeaningfulElementNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "h1",
            "h2",
            "h3",
            "h4",
            "p",
            "ul",
            "ol",
            "table"
        };

        private static readonly string[] NoiseAttributeTokens =
        {
            "menu",
            "navbar",
            "sidebar",
            "footer",
            "header",
            "breadcrumb",
            "ads",
            "advertisement",
            "related",
            "recommend",
            "comment",
            "share",
            "social",
            "login",
            "subscribe",
            "newsletter",
            "popup",
            "modal",
            "toc",
            "reference",
            "references",
            "reflist",
            "external-links",
            "catlinks",
            "metadata",
            "noprint",
            "portal"
        };

        private static readonly string[] NonContentTableTokens =
        {
            "infobox",
            "navbox",
            "sidebar",
            "metadata",
            "toc",
            "vertical-navbox",
            "vertical navbox",
            "ambox",
            "portal",
            "reflist",
            "reference",
            "layout",
            "navigation"
        };

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

            if (LooksLikePdfUrl(uri))
            {
                return UrlExtractionResult.Fail("Hiện tại hệ thống chưa trích xuất trực tiếp PDF từ URL. Bạn có thể tải PDF về và upload file PDF.");
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

        private static bool LooksLikePdfUrl(Uri uri)
        {
            return uri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
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

            RemoveGlobalNonContentNodes(htmlDocument.DocumentNode);
            var contentNode = FindMainContentNode(htmlDocument);
            RemoveNonContentNodes(contentNode);

            var textBuilder = new StringBuilder();
            var shouldSkipSection = false;
            var hasStartedMainContent = false;

            foreach (var node in contentNode.ChildNodes)
            {
                AppendMeaningfulNodeText(
                    node,
                    textBuilder,
                    ref shouldSkipSection,
                    ref hasStartedMainContent,
                    allowLeadingStructuredContent: false);
            }

            if (CountUsefulWords(textBuilder.ToString()) < MinimumUsefulWordsForUrl)
            {
                textBuilder.Clear();
                AppendMeaningfulDescendantText(contentNode, textBuilder, allowLeadingStructuredContent: true);
            }

            return NormalizeText(textBuilder.ToString());
        }

        private static HtmlNode FindMainContentNode(HtmlDocument htmlDocument)
        {
            var candidates = MainContentSelectors
                .SelectMany(selector => htmlDocument.DocumentNode.SelectNodes(selector) ?? Enumerable.Empty<HtmlNode>())
                .Where(node => node.NodeType == HtmlNodeType.Element)
                .Where(node => !IsNoiseNode(node))
                .Distinct()
                .Select(node => new
                {
                    Node = node,
                    Score = ScoreContentCandidate(node)
                })
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score)
                .ToList();

            return candidates.FirstOrDefault()?.Node
                ?? htmlDocument.DocumentNode.SelectSingleNode("//body")
                ?? htmlDocument.DocumentNode;
        }

        private static int ScoreContentCandidate(HtmlNode node)
        {
            var text = WebUtility.HtmlDecode(node.InnerText);
            var usefulWords = CountUsefulWords(text);
            var paragraphCount = node.Descendants("p").Count();
            var tableCount = node.Descendants("table").Count(table => !IsLikelyNonContentTable(table));
            var listCount = node.Descendants("ul").Count() + node.Descendants("ol").Count();

            return usefulWords + (paragraphCount * 25) + (tableCount * 35) + (listCount * 10);
        }

        private static void RemoveNonContentNodes(HtmlNode root)
        {
            var nodesToRemove = root
                .Descendants()
                .Where(node => node.NodeType == HtmlNodeType.Element)
                .Where(IsNoiseNode)
                .ToList();

            foreach (var node in nodesToRemove)
            {
                node.Remove();
            }
        }

        private static void RemoveGlobalNonContentNodes(HtmlNode root)
        {
            var nodesToRemove = root
                .Descendants()
                .Where(node => node.NodeType == HtmlNodeType.Element)
                .Where(node => node.Name.ToLowerInvariant() is "script" or "style" or "noscript" or "iframe" or "svg")
                .ToList();

            foreach (var node in nodesToRemove)
            {
                node.Remove();
            }
        }

        private static bool IsNoiseNode(HtmlNode node)
        {
            var name = node.Name.ToLowerInvariant();

            if (name is "html" or "body" or "main" or "article")
            {
                return false;
            }

            if (name is "script" or "style" or "noscript" or "iframe" or "svg" or
                "nav" or "header" or "footer" or "aside" or "form")
            {
                return true;
            }

            if (IsMainContentContainer(node))
            {
                return false;
            }

            var attributes = NormalizeForComparison(
                $"{node.GetAttributeValue("id", string.Empty)} {node.GetAttributeValue("class", string.Empty)}");

            return NoiseAttributeTokens.Any(token => attributes.Contains(token, StringComparison.Ordinal));
        }

        private static bool IsMainContentContainer(HtmlNode node)
        {
            var attributes = NormalizeForComparison(
                $"{node.GetAttributeValue("id", string.Empty)} {node.GetAttributeValue("class", string.Empty)}");

            return attributes.Contains("content", StringComparison.Ordinal) ||
                   attributes.Contains("main-content", StringComparison.Ordinal) ||
                   attributes.Contains("article-content", StringComparison.Ordinal) ||
                   attributes.Contains("post-content", StringComparison.Ordinal) ||
                   attributes.Contains("entry-content", StringComparison.Ordinal) ||
                   attributes.Contains("mw-content-text", StringComparison.Ordinal) ||
                   attributes.Contains("mw-parser-output", StringComparison.Ordinal);
        }

        private static void AppendMeaningfulDescendantText(
            HtmlNode contentNode,
            StringBuilder builder,
            bool allowLeadingStructuredContent)
        {
            var shouldSkipSection = false;
            var hasStartedMainContent = allowLeadingStructuredContent;

            foreach (var node in contentNode
                .Descendants()
                .Where(node => node.NodeType == HtmlNodeType.Element && MeaningfulElementNames.Contains(node.Name)))
            {
                AppendMeaningfulNodeText(
                    node,
                    builder,
                    ref shouldSkipSection,
                    ref hasStartedMainContent,
                    allowLeadingStructuredContent);
            }
        }

        private static void AppendMeaningfulNodeText(
            HtmlNode node,
            StringBuilder builder,
            ref bool shouldSkipSection,
            ref bool hasStartedMainContent,
            bool allowLeadingStructuredContent)
        {
            if (node.NodeType != HtmlNodeType.Element)
            {
                return;
            }

            if (IsNoiseNode(node))
            {
                return;
            }

            var name = node.Name.ToLowerInvariant();

            if (!MeaningfulElementNames.Contains(name))
            {
                AppendMeaningfulDescendantText(node, builder, allowLeadingStructuredContent);
                return;
            }

            if (name is "h1" or "h2" or "h3" or "h4")
            {
                var headingText = NormalizeHeadingText(WebUtility.HtmlDecode(node.InnerText));
                shouldSkipSection = ShouldSkipSectionAfterHeading(headingText);

                if (!shouldSkipSection)
                {
                    AppendTextBlock(builder, headingText);
                }

                return;
            }

            if (shouldSkipSection)
            {
                return;
            }

            switch (name)
            {
                case "p":
                    var paragraphText = CleanInlineText(WebUtility.HtmlDecode(node.InnerText));
                    AppendTextBlock(builder, paragraphText);
                    if (CountUsefulWords(paragraphText) >= 12)
                    {
                        hasStartedMainContent = true;
                    }
                    break;
                case "ul":
                case "ol":
                    if (!allowLeadingStructuredContent && !hasStartedMainContent)
                    {
                        return;
                    }
                    AppendList(builder, node);
                    break;
                case "table":
                    if (!allowLeadingStructuredContent && !hasStartedMainContent)
                    {
                        return;
                    }
                    AppendUrlTable(builder, node);
                    break;
            }
        }

        private static string NormalizeHeadingText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var normalized = Regex.Replace(text, @"\[\s*edit\s*\]", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            normalized = Regex.Replace(normalized, @"\s+", " ");
            normalized = normalized.Trim(' ', ':', '-', '|');

            return normalized.Trim();
        }

        private static bool ShouldSkipSectionAfterHeading(string headingText)
        {
            var comparable = NormalizeForComparison(headingText);

            return comparable is
                "see also" or
                "references" or
                "external links" or
                "notes" or
                "further reading" or
                "related articles" or
                "tin lien quan" or
                "bai viet lien quan" or
                "xem them" or
                "lien he";
        }

        private static void AppendTextBlock(StringBuilder builder, string? text)
        {
            var normalized = CleanInlineText(text);

            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            AppendParagraphBreak(builder);
            builder.Append(normalized);
            AppendParagraphBreak(builder);
        }

        private static void AppendList(StringBuilder builder, HtmlNode listNode)
        {
            foreach (var item in listNode.Elements("li"))
            {
                if (IsNoiseNode(item))
                {
                    continue;
                }

                var itemText = CleanInlineText(WebUtility.HtmlDecode(item.InnerText));

                if (string.IsNullOrWhiteSpace(itemText))
                {
                    continue;
                }

                AppendParagraphBreak(builder);
                builder.Append("- ");
                builder.Append(itemText);
                AppendParagraphBreak(builder);
            }
        }

        private static void AppendUrlTable(StringBuilder builder, HtmlNode tableNode)
        {
            if (!TryExtractUrlTableRows(tableNode, out var rows))
            {
                return;
            }

            AppendParagraphBreak(builder);
            builder.AppendLine(UrlTableStartMarker);

            foreach (var row in rows)
            {
                builder.Append("| ");

                for (var columnIndex = 0; columnIndex < row.Count; columnIndex++)
                {
                    builder.Append(EscapeUrlTableCellText(row[columnIndex]));
                    builder.Append(" |");

                    if (columnIndex < row.Count - 1)
                    {
                        builder.Append(' ');
                    }
                }

                builder.AppendLine();
            }

            builder.AppendLine(UrlTableEndMarker);
            AppendParagraphBreak(builder);
        }

        private static bool TryExtractUrlTableRows(HtmlNode tableNode, out IReadOnlyList<IReadOnlyList<string>> rows)
        {
            rows = Array.Empty<IReadOnlyList<string>>();

            if (IsLikelyNonContentTable(tableNode))
            {
                return false;
            }

            var parsedRows = new List<IReadOnlyList<string>>();
            var maxColumnCount = 0;

            foreach (var rowNode in tableNode.Descendants("tr"))
            {
                var cells = rowNode
                    .ChildNodes
                    .Where(cell => cell.Name.Equals("th", StringComparison.OrdinalIgnoreCase) ||
                                   cell.Name.Equals("td", StringComparison.OrdinalIgnoreCase))
                    .Take(MaxExtractedTableColumns)
                    .Select(ExtractUrlTableCellText)
                    .ToList();

                if (cells.All(string.IsNullOrWhiteSpace) || IsLegendRow(cells))
                {
                    continue;
                }

                maxColumnCount = Math.Max(maxColumnCount, cells.Count);
                parsedRows.Add(cells);

                if (parsedRows.Count >= MaxExtractedTableRows)
                {
                    break;
                }
            }

            if (parsedRows.Count < 2 || maxColumnCount < 2)
            {
                return false;
            }

            var usefulTableWords = parsedRows.Sum(row => row.Sum(CountUsefulWords));
            if (usefulTableWords < 8)
            {
                return false;
            }

            rows = parsedRows
                .Select(row => NormalizeUrlTableRow(row, maxColumnCount))
                .ToList();
            return true;
        }

        private static IReadOnlyList<string> NormalizeUrlTableRow(IReadOnlyList<string> row, int maxColumnCount)
        {
            var normalizedRow = new List<string>(maxColumnCount);

            for (var columnIndex = 0; columnIndex < maxColumnCount; columnIndex++)
            {
                normalizedRow.Add(columnIndex < row.Count ? row[columnIndex] : string.Empty);
            }

            return normalizedRow;
        }

        private static string ExtractUrlTableCellText(HtmlNode cellNode)
        {
            var text = WebUtility.HtmlDecode(cellNode.InnerText);
            text = CleanInlineText(text);
            text = Regex.Replace(
                text,
                @"^(?:Unsupported|Supported|Latest version)\s*:\s*",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            return text.Trim();
        }

        private static string EscapeUrlTableCellText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return Regex.Replace(text.Trim(), @"\s+", " ")
                .Replace("|", " / ", StringComparison.Ordinal);
        }

        private static bool IsLegendRow(IReadOnlyList<string> cells)
        {
            var rowText = string.Join(" ", cells).Trim();
            return rowText.StartsWith("Legend:", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLikelyNonContentTable(HtmlNode tableNode)
        {
            var attributes = NormalizeForComparison(
                $"{tableNode.GetAttributeValue("id", string.Empty)} {tableNode.GetAttributeValue("class", string.Empty)}");
            var roleValue = NormalizeForComparison(tableNode.GetAttributeValue("role", string.Empty));

            if (roleValue == "presentation")
            {
                return true;
            }

            return NonContentTableTokens.Any(token => attributes.Contains(token, StringComparison.Ordinal));
        }

        private static string CleanInlineText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var cleaned = text;
            cleaned = Regex.Replace(cleaned, @"\[\s*\d+\s*\]", string.Empty);
            cleaned = Regex.Replace(cleaned, @"\[\s*note\s+\d+\s*\]", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            cleaned = Regex.Replace(cleaned, @"\[\s*citation needed\s*\]", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            cleaned = Regex.Replace(cleaned, @"citation needed", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            cleaned = Regex.Replace(cleaned, @"permanent dead link", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            cleaned = Regex.Replace(cleaned, @"\[\s*edit\s*\]", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            cleaned = Regex.Replace(cleaned, @"\s+", " ");

            return cleaned.Trim();
        }

        private static void AppendParagraphBreak(StringBuilder builder)
        {
            if (builder.Length == 0)
            {
                return;
            }

            var text = builder.ToString();
            if (text.EndsWith("\n\n", StringComparison.Ordinal))
            {
                return;
            }

            if (text.EndsWith('\n'))
            {
                builder.AppendLine();
                return;
            }

            builder.AppendLine();
            builder.AppendLine();
        }

        private static string NormalizeText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var normalized = text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');

            normalized = Regex.Replace(normalized, @"[ \t\f\v]+", " ");
            normalized = Regex.Replace(normalized, @" *\n *", "\n");
            normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
            normalized = RemoveBoilerplateLines(normalized);

            return normalized.Trim();
        }

        private static string RemoveBoilerplateLines(string text)
        {
            var keptLines = new List<string>();
            var seenMeaningfulLines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var isInsideUrlTable = false;

            foreach (var rawLine in text.Split('\n'))
            {
                var line = NormalizeLineSpacing(rawLine);

                if (line.Equals(UrlTableStartMarker, StringComparison.OrdinalIgnoreCase))
                {
                    keptLines.Add(line);
                    isInsideUrlTable = true;
                    continue;
                }

                if (line.Equals(UrlTableEndMarker, StringComparison.OrdinalIgnoreCase))
                {
                    keptLines.Add(line);
                    isInsideUrlTable = false;
                    continue;
                }

                if (isInsideUrlTable)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        keptLines.Add(line);
                    }

                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    if (keptLines.Count > 0 && keptLines[^1].Length > 0)
                    {
                        keptLines.Add(string.Empty);
                    }

                    continue;
                }

                if (IsLikelyBoilerplateLine(line))
                {
                    continue;
                }

                var duplicateKey = NormalizeLineForDuplicateCheck(line);
                if (duplicateKey.Length > 0 && !seenMeaningfulLines.Add(duplicateKey))
                {
                    continue;
                }

                if (keptLines.Count > 0 && keptLines[^1].Length == 0)
                {
                    keptLines.Add(line);
                }
                else if (keptLines.Count == 0 || !string.Equals(keptLines[^1], line, StringComparison.Ordinal))
                {
                    keptLines.Add(line);
                }
            }

            while (keptLines.Count > 0 && keptLines[^1].Length == 0)
            {
                keptLines.RemoveAt(keptLines.Count - 1);
            }

            return string.Join(Environment.NewLine, keptLines);
        }

        private static bool IsLikelyBoilerplateLine(string line)
        {
            var normalized = NormalizeLineSpacing(line);
            var withoutListMarker = RemoveLeadingListMarker(normalized);
            var comparable = NormalizeForComparison(withoutListMarker);

            if (IsImportantShortToken(normalized) || IsImportantShortToken(withoutListMarker))
            {
                return false;
            }

            if (normalized.StartsWith(">>", StringComparison.Ordinal))
            {
                return true;
            }

            if (IsMicrosoftLearnAuthorizationLine(comparable) || IsWikiMetadataLine(comparable))
            {
                return true;
            }

            if (normalized.Length <= 2)
            {
                return true;
            }

            if (comparable.Length <= 4 && CountUsefulWords(comparable) <= 1)
            {
                return true;
            }

            return IsExactBoilerplateText(comparable)
                || IsLikelyNavigationCluster(comparable)
                || IsLikelyLinkOnlyPrompt(comparable);
        }

        private static string NormalizeLineSpacing(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return string.Empty;
            }

            return Regex.Replace(line.Trim(), @"\s+", " ");
        }

        private static string RemoveLeadingListMarker(string line)
        {
            return Regex.Replace(line, @"^(?:[-*•]\s+|\d+[\.)]\s+)+", string.Empty).Trim();
        }

        private static string NormalizeLineForDuplicateCheck(string line)
        {
            var normalized = NormalizeForComparison(RemoveLeadingListMarker(line));
            normalized = Regex.Replace(normalized, @"[^\p{L}\p{N}+#.]+", " ").Trim();

            return normalized.Length < 6
                ? string.Empty
                : normalized;
        }

        private static string NormalizeForComparison(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var normalized = text.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);

            foreach (var character in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(char.ToLowerInvariant(character));
                }
            }

            return Regex.Replace(builder.ToString().Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
        }

        private static bool IsImportantShortToken(string line)
        {
            return Regex.IsMatch(
                    line,
                    @"^(?:[A-Z]{2,5}\d{2,4}|[IVXLCDM]+\.\d{1,2}|\.NET|C#)$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static bool IsMicrosoftLearnAuthorizationLine(string comparable)
        {
            return comparable.StartsWith("access to this page requires authorization", StringComparison.Ordinal)
                || comparable.Contains("you can try signing in or changing directories", StringComparison.Ordinal)
                || comparable.Contains("requires authorization you can try signing in", StringComparison.Ordinal);
        }

        private static bool IsWikiMetadataLine(string comparable)
        {
            if (comparable.StartsWith("coordinates:", StringComparison.Ordinal) ||
                comparable.StartsWith("coordinates ", StringComparison.Ordinal))
            {
                return true;
            }

            return comparable is
                "from wikipedia, the free encyclopedia" or
                "jump to navigation" or
                "jump to search" or
                "[edit]" or
                "edit" or
                "original author" or
                "developer" or
                "developers" or
                "initial release" or
                "stable release" or
                "written in" or
                "operating system" or
                "platform" or
                "type" or
                "license" or
                "website" or
                "repository";
        }

        private static bool IsExactBoilerplateText(string comparable)
        {
            return comparable is
                "menu" or
                "search" or
                "subscribe" or
                "advertisement" or
                "skip to content" or
                "share" or
                "related posts" or
                "thu vien lien he" or
                "tin lien quan" or
                "xem them" or
                "lien he" or
                "dang nhap" or
                "dang ky" or
                "theo doi" or
                "thu vien" or
                "hoi dap" or
                "sitemap" or
                "print" or
                "feedback";
        }

        private static bool IsLikelyNavigationCluster(string comparable)
        {
            var navigationWords = new[]
            {
                "thu vien",
                "lien he",
                "dang nhap",
                "dang ky",
                "theo doi",
                "xem them",
                "tin lien quan",
                "chia se",
                "gui bai",
                "rss"
            };

            var matchedWords = navigationWords.Count(word => comparable.Contains(word, StringComparison.Ordinal));

            return matchedWords >= 2 && CountUsefulWords(comparable) <= 8;
        }

        private static bool IsLikelyLinkOnlyPrompt(string comparable)
        {
            if (CountUsefulWords(comparable) > 6)
            {
                return false;
            }

            return comparable.StartsWith("xem them", StringComparison.Ordinal)
                || comparable.StartsWith("tin lien quan", StringComparison.Ordinal)
                || comparable.StartsWith("bai viet lien quan", StringComparison.Ordinal)
                || comparable.StartsWith("chu de lien quan", StringComparison.Ordinal)
                || comparable.StartsWith("read more", StringComparison.Ordinal)
                || comparable.StartsWith("learn more", StringComparison.Ordinal)
                || comparable.StartsWith("see more", StringComparison.Ordinal)
                || comparable.StartsWith("related", StringComparison.Ordinal);
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