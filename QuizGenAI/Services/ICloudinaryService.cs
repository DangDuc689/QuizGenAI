using Microsoft.AspNetCore.Http;

namespace QuizGenAI.Services;

public interface ICloudinaryService
{
    Task<string?> UploadImageAsync(IFormFile file, string folderName);
    Task<bool> DeleteImageAsync(string imageUrl);
}
