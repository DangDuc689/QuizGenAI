using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using QuizGenAI.Models;

namespace QuizGenAI.Services
{
    public class GeminiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _modelName;

        public GeminiService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _apiKey = configuration["Gemini:ApiKey"] ?? string.Empty;
            // Cho phép cấu hình modelName, mặc định là gemini-2.5-flash
            _modelName = configuration["Gemini:ModelName"] ?? "gemini-2.5-flash";
        }

        public async Task<List<GeneratedQuestionDto>> GenerateQuestionsAsync(
            string textContent, 
            int totalQuestions, 
            int rememberPercent, 
            int understandPercent, 
            int applyPercent,
            OutputLanguage language)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                throw new InvalidOperationException("Gemini API Key chưa được cấu hình. Vui lòng kiểm tra appsettings.json.");
            }

            if (string.IsNullOrWhiteSpace(textContent))
            {
                throw new ArgumentException("Nội dung tài liệu học tập không được để trống.", nameof(textContent));
            }

            // Tính toán số câu hỏi cho từng tầng Bloom
            int numRemember = (int)Math.Round(totalQuestions * rememberPercent / 100.0);
            int numUnderstand = (int)Math.Round(totalQuestions * understandPercent / 100.0);
            int numApply = totalQuestions - numRemember - numUnderstand; // Lấy phần còn lại để đảm bảo tổng số câu chính xác

            // Đảm bảo không có số âm
            numRemember = Math.Max(0, numRemember);
            numUnderstand = Math.Max(0, numUnderstand);
            numApply = Math.Max(0, numApply);

            var languageStr = language == OutputLanguage.English ? "English" : "Tiếng Việt";

            // Xây dựng Prompt chi tiết
            var promptBuilder = new StringBuilder();
            promptBuilder.AppendLine("Bạn là một chuyên gia khảo thí và xây dựng đề thi học thuật chất lượng cao.");
            promptBuilder.AppendLine("Dựa trên tài liệu được cung cấp dưới đây, hãy tạo bộ câu hỏi trắc nghiệm (mỗi câu gồm 4 đáp án A, B, C, D và chỉ có duy nhất 1 đáp án đúng) theo chuẩn phân loại Bloom.");
            promptBuilder.AppendLine($"Ngôn ngữ của câu hỏi, các đáp án và lời giải thích phải hoàn toàn bằng: {languageStr}.");
            promptBuilder.AppendLine($"Tổng số câu hỏi cần tạo là {totalQuestions} câu, trong đó:");
            promptBuilder.AppendLine($"- {numRemember} câu ở mức độ NHẬN BIẾT (Remembering - Bloom Level: 0): Hỏi về các dữ kiện trực tiếp, định nghĩa, thông tin rõ ràng trong bài.");
            promptBuilder.AppendLine($"- {numUnderstand} câu ở mức độ THÔNG HIỂU (Understanding - Bloom Level: 1): Hỏi về việc hiểu bản chất, giải thích lý do, so sánh các khái niệm.");
            promptBuilder.AppendLine($"- {numApply} câu ở mức độ VẬN DỤNG (Applying - Bloom Level: 2): Hỏi cách giải quyết tình huống, tính toán cụ thể từ công thức hoặc áp dụng lý thuyết vào ngữ cảnh thực tế.");
            promptBuilder.AppendLine("Yêu cầu mỗi câu hỏi phải có lời giải thích chi tiết vì sao đáp án đó đúng.");
            promptBuilder.AppendLine("Yêu cầu chỉ sử dụng thông tin có trong tài liệu dưới đây, không tự suy diễn các thông tin nằm ngoài tài liệu.");
            promptBuilder.AppendLine("\n--- TÀI LIỆU HỌC TẬP ---");
            promptBuilder.AppendLine(textContent);
            promptBuilder.AppendLine("--- HẾT TÀI LIỆU ---");

            // Cấu hình Request body cho Gemini API với Structured Output (JSON Schema)
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = promptBuilder.ToString() }
                        }
                    }
                },
                generationConfig = new
                {
                    responseMimeType = "application/json",
                    responseSchema = new
                    {
                        type = "ARRAY",
                        description = "Danh sách câu hỏi trắc nghiệm được tạo",
                        items = new
                        {
                            type = "OBJECT",
                            properties = new
                            {
                                content = new { type = "STRING", description = "Nội dung câu hỏi trắc nghiệm." },
                                bloomLevel = new { type = "INTEGER", description = "Cấp độ Bloom của câu hỏi: 0 = Nhận biết, 1 = Thông hiểu, 2 = Vận dụng." },
                                explanation = new { type = "STRING", description = "Giải thích chi tiết tại sao đáp án đúng được chọn và trích dẫn thông tin liên quan từ tài liệu." },
                                options = new
                                {
                                    type = "ARRAY",
                                    description = "Danh sách 4 đáp án lựa chọn A, B, C, D.",
                                    items = new
                                    {
                                        type = "OBJECT",
                                        properties = new
                                        {
                                            label = new { type = "STRING", description = "Nhãn đáp án: A, B, C hoặc D." },
                                            content = new { type = "STRING", description = "Nội dung chi tiết của đáp án." },
                                            isCorrect = new { type = "BOOLEAN", description = "Đặt là true nếu là đáp án đúng duy nhất, ngược lại là false." }
                                        },
                                        required = new[] { "label", "content", "isCorrect" }
                                    }
                                }
                            },
                            required = new[] { "content", "bloomLevel", "explanation", "options" }
                        }
                    }
                }
            };

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_modelName}:generateContent?key={_apiKey}";
            var jsonContent = JsonSerializer.Serialize(requestBody);
            var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, httpContent);
            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Lỗi khi gọi Gemini API ({response.StatusCode}): {errorMsg}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            
            // Parse kết quả trả về từ Gemini API
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            
            if (root.TryGetProperty("candidates", out var candidates) && 
                candidates.GetArrayLength() > 0 &&
                candidates[0].TryGetProperty("content", out var contentElement) &&
                contentElement.TryGetProperty("parts", out var parts) &&
                parts.GetArrayLength() > 0 &&
                parts[0].TryGetProperty("text", out var textElement))
            {
                var rawJsonText = textElement.GetString();
                if (string.IsNullOrWhiteSpace(rawJsonText))
                {
                    throw new Exception("Gemini API trả về nội dung trống.");
                }

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var questionsList = JsonSerializer.Deserialize<List<GeneratedQuestionDto>>(rawJsonText, options);
                return questionsList ?? new List<GeneratedQuestionDto>();
            }

            throw new Exception("Không thể parse dữ liệu phản hồi từ Gemini API. Cấu trúc JSON không hợp lệ.");
        }
    }

    // DTO để parse dữ liệu trả về từ Gemini
    public class GeneratedQuestionDto
    {
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("bloomLevel")]
        public int BloomLevel { get; set; } // 0, 1, 2

        [JsonPropertyName("explanation")]
        public string Explanation { get; set; } = string.Empty;

        [JsonPropertyName("options")]
        public List<GeneratedOptionDto> Options { get; set; } = new List<GeneratedOptionDto>();
    }

    public class GeneratedOptionDto
    {
        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty; // A, B, C, D

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("isCorrect")]
        public bool IsCorrect { get; set; }
    }
}
