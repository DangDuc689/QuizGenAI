using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuizGenAI.Models;

namespace QuizGenAI.Services
{
    public class GeminiService
    {
        private const string AiBadRequestMessage = "AI không nhận được yêu cầu hợp lệ. Vui lòng thử lại với tài liệu ngắn hơn hoặc số câu ít hơn.";
        private const string AiRateLimitMessage = "AI đang quá tải hoặc đã chạm giới hạn gọi API. Vui lòng thử lại sau ít phút hoặc giảm số lượng câu hỏi.";
        private const string AiOverloadedMessage = "AI đang quá tải, vui lòng thử lại sau ít phút.";

        private readonly HttpClient _httpClient;
        private readonly ILogger<GeminiService> _logger;
        private readonly List<string> _apiKeys = new();
        private static int _currentKeyIndex = 0;
        private readonly string _apiKey; // Lưu key mặc định đầu tiên để tương thích ngược
        private readonly string _modelName;

        public GeminiService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<GeminiService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _modelName = configuration["Gemini:ModelName"] ?? "gemini-2.5-flash";

            // 1. Đọc danh sách ApiKeys
            var apiKeysSection = configuration.GetSection("Gemini:ApiKeys");
            if (apiKeysSection.Exists())
            {
                var keys = apiKeysSection.Get<List<string>>();
                if (keys != null)
                {
                    _apiKeys.AddRange(keys.Where(k => !string.IsNullOrWhiteSpace(k)));
                }
            }

            // 2. Fallback sang ApiKey đơn lẻ nếu chưa có key nào trong danh sách
            var singleKey = configuration["Gemini:ApiKey"];
            if (!string.IsNullOrWhiteSpace(singleKey) && !_apiKeys.Contains(singleKey))
            {
                _apiKeys.Add(singleKey);
            }

            _apiKey = _apiKeys.Count > 0 ? _apiKeys[0] : string.Empty;

            // Log danh sách fingerprints an toàn khi khởi tạo
            var keyFingerprints = _apiKeys.Select(k => k.Length >= 4 ? "..." + k[^4..] : "(empty)");
            _logger.LogInformation(
                "GeminiService initialized. Model={ModelName}, KeyPoolSize={KeyPoolSize}, KeyFingerprints=[{KeyFingerprints}]",
                _modelName,
                _apiKeys.Count,
                string.Join(", ", keyFingerprints));
        }

        private string GetCurrentApiKey()
        {
            if (_apiKeys.Count == 0)
            {
                return string.Empty;
            }
            var index = Math.Abs(_currentKeyIndex) % _apiKeys.Count;
            return _apiKeys[index];
        }

        private void RotateToNextKey()
        {
            if (_apiKeys.Count > 1)
            {
                System.Threading.Interlocked.Increment(ref _currentKeyIndex);
            }
        }

        /// <summary>
        /// Test nhỏ: gọi Gemini với prompt cực ngắn để kiểm tra quota/rate limit.
        /// </summary>
        public async Task<(bool Success, int StatusCode, string Message)> PingAsync()
        {
            EnsureApiKeyConfigured();

            var activeKey = GetCurrentApiKey();
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_modelName}:generateContent?key={activeKey}";
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = "Trả lời đúng chữ OK" }
                        }
                    }
                },
                generationConfig = new
                {
                    maxOutputTokens = 32
                }
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);

            try
            {
                using var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync(url, httpContent);
                var responseText = await response.Content.ReadAsStringAsync();
                var statusCode = (int)response.StatusCode;

                var keyFingerprint = activeKey.Length >= 4 ? "..." + activeKey[^4..] : "(empty)";
                _logger.LogInformation(
                    "[GEMINI_PING] StatusCode={StatusCode}, Key={Key}, Model={ModelName}, ResponseLength={ResponseLength}",
                    statusCode,
                    keyFingerprint,
                    _modelName,
                    responseText.Length);

                if (response.IsSuccessStatusCode)
                {
                    var text = ExtractTextFromGeminiResponse(responseText);
                    RotateToNextKey(); // Xoay sang key khác cho request sau
                    return (true, statusCode, $"OK - Gemini trả lời: {text?.Trim() ?? "(empty)"}");
                }

                RotateToNextKey(); // Xoay key khi gặp lỗi
                return (false, statusCode, $"Gemini lỗi {statusCode} (Key: {keyFingerprint}): {SanitizeForLog(responseText[..Math.Min(300, responseText.Length)])}");
            }
            catch (Exception ex)
            {
                _logger.LogError("[GEMINI_PING] Exception: {Error}", ex.Message);
                RotateToNextKey();
                return (false, 0, $"Exception: {ex.Message}");
            }
        }

        public async Task<string> ExtractTextFromImageBytesAsync(
            byte[] imageBytes,
            string mimeType,
            CancellationToken cancellationToken = default)
        {
            EnsureApiKeyConfigured();

            if (imageBytes == null || imageBytes.Length == 0)
            {
                return "KHONG_DOC_DUOC_NOI_DUNG_ANH";
            }

            if (string.IsNullOrWhiteSpace(mimeType))
            {
                mimeType = "image/png";
            }

            var prompt = """
                Bạn là hệ thống đọc nội dung học tập từ ảnh được nhúng trong file Word cho QuizGen AI.

                Nhiệm vụ:
                1. Đọc kỹ toàn bộ chữ xuất hiện trong ảnh, kể cả tiêu đề, gạch đầu dòng, bảng, nhãn sơ đồ, công thức và chú thích.
                2. Nếu ảnh là slide, bảng, sơ đồ, công thức, ảnh chụp trang tài liệu hoặc ảnh chụp màn hình bài học, hãy chuyển thành nội dung học tập có cấu trúc.
                3. Nếu đọc được ít nhất vài ý chính, vẫn phải trả về các ý đó. Không được trả lời chung chung như "ảnh chứa nội dung học tập" nếu có thể đọc được chữ hoặc ý chính.
                4. Nếu là bảng, hãy mô tả các cột, hàng và dữ liệu quan trọng.
                5. Nếu là sơ đồ, hãy mô tả các thành phần chính và mối quan hệ giữa chúng.
                6. Không thêm kiến thức ngoài ảnh, không tự bịa phần không nhìn thấy.

                Định dạng trả về:
                - Trả về tiếng Việt dạng văn bản thuần.
                - Có thể dùng các dòng ngắn theo cấu trúc: "Tiêu đề:", "Ý chính:", "Bảng:", "Sơ đồ:", "Công thức:".
                - Không dùng markdown phức tạp.
                - Nếu ảnh quá mờ, không liên quan học tập hoặc thật sự không đọc được nội dung hữu ích, chỉ trả về đúng: KHONG_DOC_DUOC_NOI_DUNG_ANH.
                """;

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new
                            {
                                inline_data = new
                                {
                                    mime_type = mimeType,
                                    data = Convert.ToBase64String(imageBytes)
                                }
                            },
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.1,
                    maxOutputTokens = 2048
                }
            };

            var responseJson = await SendGenerateContentRequestWithRetryAsync(
                requestBody,
                cancellationToken,
                "Gemini Vision");

            var text = ExtractTextFromGeminiResponse(responseJson);

            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning(
                    "Gemini Vision returned empty text. Model={ModelName}, MimeType={MimeType}, ImageBytes={ImageBytes}. The model may not support vision, the request may be rejected, or the image may be unreadable.",
                    _modelName,
                    mimeType,
                    imageBytes.Length);

                return "KHONG_DOC_DUOC_NOI_DUNG_ANH";
            }

            return text.Trim();
        }

        public async Task<string> ExtractStructuredTextFromPdfAsync(
            byte[] pdfBytes,
            CancellationToken cancellationToken = default)
        {
            EnsureApiKeyConfigured();

            if (pdfBytes == null || pdfBytes.Length == 0)
            {
                return string.Empty;
            }

            var prompt = """
                Bạn là hệ thống đọc và trích xuất nội dung học tập từ tài liệu PDF cho QuizGen AI.

                Nhiệm vụ:
                1. Đọc kỹ và trích xuất toàn bộ văn bản xuất hiện trong file PDF.
                2. Nếu tài liệu chứa các bảng biểu, hãy chuyển đổi chúng thành định dạng bảng Markdown rõ ràng.
                3. Nếu tài liệu chứa hình ảnh, sơ đồ hoặc công thức toán, hãy mô tả chi tiết nội dung học tập và mối quan hệ được hiển thị trong đó.
                4. Giữ nguyên cấu trúc logic của tài liệu (tiêu đề, các mục lớn, mục con).
                5. Không thêm kiến thức ngoài tài liệu, không tự bịa thông tin không có trong PDF.

                Định dạng trả về:
                - Trả về bằng tiếng Việt dạng văn bản sạch, có cấu trúc tốt.
                - Sử dụng Markdown cơ bản để định dạng tiêu đề, danh sách và bảng biểu.
                - Không thêm bất kỳ lời dẫn đề hay lời kết nào của AI (ví dụ: không viết "Đây là nội dung...", chỉ trả về nội dung trích xuất).
                """;

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new
                            {
                                inline_data = new
                                {
                                    mime_type = "application/pdf",
                                    data = Convert.ToBase64String(pdfBytes)
                                }
                            },
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.2,
                    maxOutputTokens = 32768  // Tăng từ 8192 lên 32768 để hỗ trợ PDF dài hơn
                }
            };

            var responseJson = await SendGenerateContentRequestWithRetryAsync(
                requestBody,
                cancellationToken,
                "Gemini PDF Extraction");

            var text = ExtractTextFromGeminiResponse(responseJson);

            return text?.Trim() ?? string.Empty;
        }

        public async Task<List<GeneratedQuestionDto>> GenerateQuestionsAsync(
            string textContent,
            int totalQuestions,
            int rememberPercent,
            int understandPercent,
            int applyPercent,
            OutputLanguage language,
            DifficultyLevel difficulty,
            byte[]? pdfBytes = null)
        {
            EnsureApiKeyConfigured();

            if (pdfBytes == null && string.IsNullOrWhiteSpace(textContent))
            {
                throw new ArgumentException("Nội dung tài liệu học tập không được để trống.", nameof(textContent));
            }

            var numRemember = (int)Math.Round(totalQuestions * rememberPercent / 100.0);
            var numUnderstand = (int)Math.Round(totalQuestions * understandPercent / 100.0);
            var numApply = totalQuestions - numRemember - numUnderstand;

            numRemember = Math.Max(0, numRemember);
            numUnderstand = Math.Max(0, numUnderstand);
            numApply = Math.Max(0, numApply);

            var languageText = language == OutputLanguage.English ? "English" : "Tiếng Việt";

            // Xử lý độ khó
            var difficultyText = difficulty switch
            {
                DifficultyLevel.Easy => "DỄ (câu hỏi tập trung vào định nghĩa, dữ kiện trực tiếp, đáp án dễ phân biệt, không lắt léo)",
                DifficultyLevel.Hard => "KHÓ (câu hỏi phức tạp, lắt léo, yêu cầu lập luận sâu, đáp án nhiễu rất thuyết phục)",
                _ => "TRUNG BÌNH (kết hợp lý thuyết cơ bản và tư duy phân tích)"
            };

            // Prompt tối ưu: yêu cầu JSON object, giới hạn độ dài field, explanation ngắn
            var promptBuilder = new StringBuilder();
            promptBuilder.AppendLine("Bạn là chuyên gia khảo thí. Tạo câu hỏi trắc nghiệm từ tài liệu ôn tập được cung cấp.");
            promptBuilder.AppendLine($"Ngôn ngữ: {languageText}. Tổng: {totalQuestions} câu.");
            promptBuilder.AppendLine($"Độ khó: {difficultyText}");
            promptBuilder.AppendLine($"- {numRemember} câu Nhận biết: hỏi dữ kiện, định nghĩa trực tiếp.");
            promptBuilder.AppendLine($"- {numUnderstand} câu Thông hiểu: hỏi bản chất, so sánh, giải thích.");
            promptBuilder.AppendLine($"- {numApply} câu Vận dụng: áp dụng lý thuyết vào tình huống.");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("QUY TẮC BẮT BUỘC:");
            promptBuilder.AppendLine("1. Trả về DUY NHẤT một JSON object hợp lệ, KHÔNG bọc trong markdown, KHÔNG thêm text ngoài JSON.");
            promptBuilder.AppendLine("2. Format: {\"questions\": [{...}, {...}]}");
            promptBuilder.AppendLine("3. Mỗi câu hỏi có: questionText (tối đa 250 ký tự), bloomLevel (\"Nhận biết\" hoặc \"Thông hiểu\" hoặc \"Vận dụng\"), options (mảng đúng 4 chuỗi, mỗi chuỗi tối đa 120 ký tự), correctAnswerIndex (số 0-3), explanation (tối đa 120 ký tự, 1 câu ngắn gọn).");
            promptBuilder.AppendLine("4. Chỉ dùng thông tin trong tài liệu, không bịa.");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("VÍ DỤ OUTPUT:");
            promptBuilder.AppendLine("{\"questions\":[{\"questionText\":\"Câu hỏi?\",\"bloomLevel\":\"Nhận biết\",\"options\":[\"A\",\"B\",\"C\",\"D\"],\"correctAnswerIndex\":0,\"explanation\":\"Giải thích ngắn.\"}]}");
            promptBuilder.AppendLine();

            if (pdfBytes == null)
            {
                promptBuilder.AppendLine("--- TÀI LIỆU ---");
                promptBuilder.AppendLine(textContent);
                promptBuilder.AppendLine("--- HẾT TÀI LIỆU ---");
            }
            else
            {
                promptBuilder.AppendLine("Tài liệu ôn tập chính là file PDF đã được đính kèm.");
            }

            var promptText = promptBuilder.ToString();

            // maxOutputTokens cao hơn: gemini-2.5-flash dùng thinking tokens trong output budget
            var maxOutputTokens = totalQuestions switch
            {
                <= 5 => 4096,
                <= 10 => 8192,
                <= 20 => 16384,
                _ => 16384
            };

            object requestBody;
            if (pdfBytes != null)
            {
                requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new object[]
                            {
                                new
                                {
                                    inline_data = new
                                    {
                                        mime_type = "application/pdf",
                                        data = Convert.ToBase64String(pdfBytes)
                                    }
                                },
                                new { text = promptText }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        maxOutputTokens,
                        temperature = 0.3
                    }
                };
            }
            else
            {
                requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[]
                            {
                                new { text = promptText }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        maxOutputTokens,
                        temperature = 0.3
                    }
                };
            }

            var responseJson = await SendGenerateContentRequestWithRetryAsync(
                requestBody,
                CancellationToken.None,
                "Gemini Quiz Generation",
                new GeminiRequestDiagnostics(
                    _modelName,
                    promptText.Length,
                    pdfBytes != null ? pdfBytes.Length : textContent.Length,
                    totalQuestions,
                    maxOutputTokens));
            var rawJsonText = ExtractTextFromGeminiResponse(responseJson);

            if (string.IsNullOrWhiteSpace(rawJsonText))
            {
                _logger.LogError(
                    "Gemini Quiz Generation: Response rỗng. Model={ModelName}, ResponseLength={ResponseLength}",
                    _modelName,
                    responseJson.Length);
                throw new InvalidOperationException("Gemini API trả về nội dung trống.");
            }

            // Strip markdown code fence nếu Gemini wrap JSON trong ```json...```
            var jsonToParse = StripMarkdownCodeFence(rawJsonText.Trim());

            _logger.LogInformation(
                "Gemini Quiz Generation: Nhận phản hồi. Model={ModelName}, RawLength={RawLength}, ParseLength={ParseLength}, MaxOutputTokens={MaxOutputTokens}, RequestedQuestions={RequestedQuestions}",
                _modelName,
                rawJsonText.Length,
                jsonToParse.Length,
                maxOutputTokens,
                totalQuestions);

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            try
            {
                // Parse: hỗ trợ cả object {"questions":[...]} lẫn array [...] thuần
                return ParseQuestionsFromJson(jsonToParse, jsonOptions);
            }
            catch (JsonException ex)
            {
                // Detect JSON bị cụt do output token limit
                var trimmedEnd = jsonToParse.TrimEnd();
                var isTruncated = !trimmedEnd.EndsWith("]", StringComparison.Ordinal)
                    && !trimmedEnd.EndsWith("}", StringComparison.Ordinal);

                // Log 3000 ký tự cuối để thấy chỗ cụt
                var tailPreview = jsonToParse.Length > 3000
                    ? "..." + jsonToParse[^3000..]
                    : jsonToParse;

                _logger.LogError(
                    "Gemini Quiz Generation: JSON parse fail. Model={ModelName}, IsTruncated={IsTruncated}, MaxOutputTokens={MaxOutputTokens}, RequestedQuestions={RequestedQuestions}, ParseLength={ParseLength}, Error={Error}, TailPreview={TailPreview}",
                    _modelName,
                    isTruncated,
                    maxOutputTokens,
                    totalQuestions,
                    jsonToParse.Length,
                    ex.Message,
                    tailPreview);

                if (isTruncated)
                {
                    throw new GeminiTruncatedResponseException(
                        "AI trả về dữ liệu chưa hoàn chỉnh. Vui lòng thử lại với số câu ít hơn.");
                }

                throw new InvalidOperationException($"Gemini trả về JSON không hợp lệ: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Parse JSON từ Gemini: hỗ trợ cả object {"questions":[...]} lẫn array [...] thuần.
        /// </summary>
        private static List<GeneratedQuestionDto> ParseQuestionsFromJson(
            string json,
            JsonSerializerOptions options)
        {
            var trimmed = json.TrimStart();

            // Format object: {"questions": [...]}
            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                var wrapper = JsonSerializer.Deserialize<QuizResponseWrapper>(trimmed, options);
                if (wrapper?.Questions != null && wrapper.Questions.Count > 0)
                {
                    return ConvertWrappedQuestions(wrapper.Questions);
                }

                throw new JsonException("JSON object không chứa field 'questions' hợp lệ.");
            }

            // Format array thuần: [...]
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                return JsonSerializer.Deserialize<List<GeneratedQuestionDto>>(trimmed, options)
                    ?? new List<GeneratedQuestionDto>();
            }

            throw new JsonException($"JSON không bắt đầu bằng {{ hoặc [. Preview: {trimmed[..Math.Min(100, trimmed.Length)]}");
        }

        /// <summary>
        /// Convert từ format mới (questionText, options string[], correctAnswerIndex) sang DTO cũ.
        /// </summary>
        private static List<GeneratedQuestionDto> ConvertWrappedQuestions(
            List<WrappedQuestionDto> wrappedQuestions)
        {
            var optionLabels = new[] { "A", "B", "C", "D" };
            var result = new List<GeneratedQuestionDto>();

            foreach (var wq in wrappedQuestions)
            {
                var dto = new GeneratedQuestionDto
                {
                    Content = wq.QuestionText ?? wq.Content ?? string.Empty,
                    BloomLevel = ConvertBloomLevelText(wq.BloomLevel),
                    Explanation = wq.Explanation ?? string.Empty,
                    Options = new List<GeneratedOptionDto>()
                };

                if (wq.Options != null)
                {
                    for (var i = 0; i < Math.Min(4, wq.Options.Count); i++)
                    {
                        dto.Options.Add(new GeneratedOptionDto
                        {
                            Label = optionLabels[i],
                            Content = wq.Options[i]?.Trim() ?? string.Empty,
                            IsCorrect = i == wq.CorrectAnswerIndex
                        });
                    }
                }

                result.Add(dto);
            }

            return result;
        }

        /// <summary>
        /// Convert bloom level text "Nhận biết"/"Thông hiểu"/"Vận dụng" sang int 0/1/2.
        /// </summary>
        private static int ConvertBloomLevelText(string? bloomLevel)
        {
            if (string.IsNullOrWhiteSpace(bloomLevel)) return 1;

            // Nếu là số
            if (int.TryParse(bloomLevel, out var intLevel))
            {
                return intLevel is >= 0 and <= 2 ? intLevel : 1;
            }

            return bloomLevel.Trim() switch
            {
                "Nhận biết" or "Remember" or "Remembering" => 0,
                "Thông hiểu" or "Understand" or "Understanding" => 1,
                "Vận dụng" or "Apply" or "Applying" => 2,
                _ => 1
            };
        }

        private void EnsureApiKeyConfigured()
        {
            if (_apiKeys.Count == 0)
            {
                throw new InvalidOperationException("Gemini API Key chưa được cấu hình. Vui lòng kiểm tra appsettings.json.");
            }
        }

        private async Task<string> SendGenerateContentRequestWithRetryAsync(
            object requestBody,
            CancellationToken cancellationToken,
            string operationName,
            GeminiRequestDiagnostics? diagnostics = null)
        {
            const int maxAttempts = 3;
            var jsonContent = JsonSerializer.Serialize(requestBody);

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var activeKey = GetCurrentApiKey();
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_modelName}:generateContent?key={activeKey}";
                var keyFingerprint = activeKey.Length >= 4 ? "..." + activeKey[^4..] : "(empty)";

                using var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync(url, httpContent, cancellationToken);
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    RotateToNextKey(); // Xoay key cho request tiếp theo
                    return responseText;
                }

                _logger.LogWarning(
                    "{OperationName} request failed with Key={KeyFingerprint}. Attempt={Attempt}/{MaxAttempts}, StatusCode={StatusCode}, Model={ModelName}, PromptLength={PromptLength}, ContentLength={ContentLength}, RequestedQuestions={RequestedQuestions}, MaxOutputTokens={MaxOutputTokens}, RequestJsonLength={RequestJsonLength}, Response={ResponseText}",
                    operationName,
                    keyFingerprint,
                    attempt,
                    maxAttempts,
                    response.StatusCode,
                    diagnostics?.ModelName ?? _modelName,
                    diagnostics?.PromptLength,
                    diagnostics?.ContentLength,
                    diagnostics?.RequestedQuestions,
                    diagnostics?.MaxOutputTokens,
                    jsonContent.Length,
                    TruncateForLog(SanitizeForLog(responseText)));

                RotateToNextKey(); // Xoay sang key tiếp theo ngay lập tức khi gặp lỗi

                if ((int)response.StatusCode == 400)
                {
                    _logger.LogError(
                        "Gemini BadRequest diagnostics. Operation={OperationName}, Model={ModelName}, PromptLength={PromptLength}, ContentLength={ContentLength}, RequestedQuestions={RequestedQuestions}, MaxOutputTokens={MaxOutputTokens}, RequestJsonLength={RequestJsonLength}, ResponseBody={ResponseBody}",
                        operationName,
                        diagnostics?.ModelName ?? _modelName,
                        diagnostics?.PromptLength,
                        diagnostics?.ContentLength,
                        diagnostics?.RequestedQuestions,
                        diagnostics?.MaxOutputTokens,
                        jsonContent.Length,
                        TruncateForLog(SanitizeForLog(responseText)));

                    throw new GeminiBadRequestException(AiBadRequestMessage);
                }

                if (IsGeminiRateLimited(response.StatusCode, responseText))
                {
                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);
                        continue;
                    }
                    throw new GeminiRateLimitException(AiRateLimitMessage);
                }

                if (IsGeminiOverloaded(response.StatusCode, responseText))
                {
                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);
                        continue;
                    }

                    throw new GeminiServiceUnavailableException(AiOverloadedMessage);
                }

                if (attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);
                    continue;
                }

                throw new HttpRequestException($"Lỗi khi gọi Gemini API ({response.StatusCode}).");
            }

            throw new GeminiServiceUnavailableException(AiOverloadedMessage);
        }

        private static string TruncateForLog(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return text.Length <= 1200
                ? text
                : text[..1200] + "...";
        }

        private string SanitizeForLog(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            if (_apiKeys.Count == 0)
            {
                return text;
            }

            var result = text;
            foreach (var key in _apiKeys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    result = result.Replace(key, "[REDACTED_API_KEY]", StringComparison.Ordinal);
                }
            }

            return result;
        }

        private static bool IsGeminiOverloaded(System.Net.HttpStatusCode statusCode, string? responseText)
        {
            if ((int)statusCode == 503)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(responseText)
                && responseText.Contains("UNAVAILABLE", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsGeminiRateLimited(System.Net.HttpStatusCode statusCode, string? responseText)
        {
            if ((int)statusCode == 429)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(responseText)
                && (responseText.Contains("TooManyRequests", StringComparison.OrdinalIgnoreCase)
                    || responseText.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase)
                    || responseText.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
                    || responseText.Contains("quota", StringComparison.OrdinalIgnoreCase));
        }

        private static string? ExtractTextFromGeminiResponse(string responseJson)
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("candidates", out var candidates) &&
                candidates.GetArrayLength() > 0 &&
                candidates[0].TryGetProperty("content", out var contentElement) &&
                contentElement.TryGetProperty("parts", out var parts) &&
                parts.GetArrayLength() > 0 &&
                parts[0].TryGetProperty("text", out var textElement))
            {
                return textElement.GetString();
            }

            return null;
        }

        /// <summary>
        /// Strip markdown code fence (```json...``` hoặc ```...```) khỏi text.
        /// Khi không dùng responseMimeType, Gemini thường bọc JSON trong code block.
        /// </summary>
        private static string StripMarkdownCodeFence(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            // Xử lý ```json\n...\n``` hoặc ```\n...\n```
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewLine = text.IndexOf('\n');
                if (firstNewLine > 0)
                {
                    text = text[(firstNewLine + 1)..];
                }

                // Xóa phần đóng ``` ở cuối
                var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
                if (lastFence >= 0)
                {
                    text = text[..lastFence].Trim();
                }
            }

            return text;
        }
    }

    public sealed class GeminiServiceUnavailableException : Exception
    {
        public GeminiServiceUnavailableException(string message)
            : base(message)
        {
        }
    }

    public sealed class GeminiRateLimitException : Exception
    {
        public GeminiRateLimitException(string message)
            : base(message)
        {
        }
    }

    public sealed class GeminiBadRequestException : Exception
    {
        public GeminiBadRequestException(string message)
            : base(message)
        {
        }
    }

    public sealed class GeminiTruncatedResponseException : Exception
    {
        public GeminiTruncatedResponseException(string message)
            : base(message)
        {
        }
    }

    public sealed record GeminiRequestDiagnostics(
        string ModelName,
        int PromptLength,
        int ContentLength,
        int RequestedQuestions,
        int MaxOutputTokens);

    public class GeneratedQuestionDto
    {
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("bloomLevel")]
        public int BloomLevel { get; set; }

        [JsonPropertyName("explanation")]
        public string Explanation { get; set; } = string.Empty;

        [JsonPropertyName("options")]
        public List<GeneratedOptionDto> Options { get; set; } = new();
    }

    public class GeneratedOptionDto
    {
        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("isCorrect")]
        public bool IsCorrect { get; set; }
    }

    /// <summary>
    /// Wrapper cho format object {"questions": [...]}
    /// </summary>
    public class QuizResponseWrapper
    {
        [JsonPropertyName("questions")]
        public List<WrappedQuestionDto> Questions { get; set; } = new();
    }

    /// <summary>
    /// DTO cho format câu hỏi mới: questionText, options string[], correctAnswerIndex.
    /// </summary>
    public class WrappedQuestionDto
    {
        [JsonPropertyName("questionText")]
        public string? QuestionText { get; set; }

        /// <summary>Fallback nếu Gemini dùng "content" thay vì "questionText"</summary>
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("bloomLevel")]
        public string? BloomLevel { get; set; }

        [JsonPropertyName("options")]
        public List<string>? Options { get; set; }

        [JsonPropertyName("correctAnswerIndex")]
        public int CorrectAnswerIndex { get; set; }

        [JsonPropertyName("explanation")]
        public string? Explanation { get; set; }
    }
}
