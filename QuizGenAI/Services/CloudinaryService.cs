using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace QuizGenAI.Services;

public class CloudinaryService : ICloudinaryService
{
    private readonly Cloudinary _cloudinary;
    private readonly ILogger<CloudinaryService> _logger;

    public CloudinaryService(IConfiguration configuration, ILogger<CloudinaryService> logger)
    {
        _logger = logger;

        // Đọc thông số cấu hình Cloudinary từ appsettings hoặc User Secrets
        var cloudName = configuration["Cloudinary:CloudName"];
        var apiKey = configuration["Cloudinary:ApiKey"];
        var apiSecret = configuration["Cloudinary:ApiSecret"];

        if (string.IsNullOrEmpty(cloudName) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
        {
            _logger.LogError("Cloudinary configuration values are missing in settings.");
            throw new ArgumentException("Cấu hình Cloudinary không hợp lệ hoặc bị thiếu.");
        }

        var account = new Account(cloudName, apiKey, apiSecret);
        _cloudinary = new Cloudinary(account);
        _cloudinary.Api.Secure = true; // Luôn sử dụng HTTPS
    }

    public async Task<string?> UploadImageAsync(IFormFile file, string folderName)
    {
        if (file == null || file.Length == 0)
        {
            return null;
        }

        try
        {
            // Mở stream của file tải lên
            await using var stream = file.OpenReadStream();
            
            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Folder = folderName,
                // Thực hiện biến đổi ảnh thông minh: Tự động crop nhận diện khuôn mặt
                Transformation = new Transformation()
                    .Width(500)
                    .Height(500)
                    .Crop("fill")
                    .Gravity("face")
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams);
            
            if (uploadResult.Error != null)
            {
                _logger.LogError("Lỗi upload ảnh lên Cloudinary: {Message}", uploadResult.Error.Message);
                return null;
            }

            // Trả về URL bảo mật dạng HTTPS
            return uploadResult.SecureUrl?.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Đã xảy ra ngoại lệ khi tải ảnh lên Cloudinary.");
            return null;
        }
    }

    public async Task<bool> DeleteImageAsync(string imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return false;
        }

        try
        {
            // Trích xuất Public ID từ URL ảnh
            var publicId = ExtractPublicIdFromUrl(imageUrl);
            if (string.IsNullOrEmpty(publicId))
            {
                _logger.LogWarning("Không thể trích xuất Public ID từ URL: {Url}", imageUrl);
                return false;
            }

            var deletionParams = new DeletionParams(publicId)
            {
                ResourceType = ResourceType.Image
            };

            var deletionResult = await _cloudinary.DestroyAsync(deletionParams);
            
            if (deletionResult.Result != "ok")
            {
                _logger.LogWarning("Xóa ảnh thất bại trên Cloudinary cho PublicId: {PublicId}. Kết quả: {Result}", publicId, deletionResult.Result);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Đã xảy ra ngoại lệ khi xóa ảnh trên Cloudinary cho URL: {Url}", imageUrl);
            return false;
        }
    }

    /// <summary>
    /// Trích xuất public id từ URL Cloudinary dạng:
    /// https://res.cloudinary.com/[cloud_name]/image/upload/v[version]/[folder]/[public_id].[extension]
    /// </summary>
    private string? ExtractPublicIdFromUrl(string imageUrl)
    {
        try
        {
            var uri = new Uri(imageUrl);
            var path = uri.AbsolutePath; // /<cloud_name>/image/upload/v<version>/<folder>/<public_id>.<extension>
            
            var uploadKeyword = "/upload/";
            var uploadIndex = path.IndexOf(uploadKeyword, StringComparison.OrdinalIgnoreCase);
            if (uploadIndex == -1)
            {
                return null;
            }

            // Lấy phần phía sau chữ "/upload/"
            var segmentAfterUpload = path[(uploadIndex + uploadKeyword.Length)..]; // v<version>/<folder>/<public_id>.<extension>
            
            // Bỏ phần version nếu có (ví dụ "v1570975264/")
            var firstSlashIndex = segmentAfterUpload.IndexOf('/');
            if (firstSlashIndex != -1 && segmentAfterUpload.StartsWith('v') && char.IsDigit(segmentAfterUpload[1]))
            {
                segmentAfterUpload = segmentAfterUpload[(firstSlashIndex + 1)..]; // <folder>/<public_id>.<extension>
            }

            // Bỏ phần mở rộng file (ví dụ ".jpg")
            var lastDotIndex = segmentAfterUpload.LastIndexOf('.');
            if (lastDotIndex != -1)
            {
                segmentAfterUpload = segmentAfterUpload[..lastDotIndex];
            }

            return segmentAfterUpload; // Trả về dạng: <folder>/<public_id>
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi trích xuất PublicId từ URL Cloudinary: {Url}", imageUrl);
            return null;
        }
    }
}
