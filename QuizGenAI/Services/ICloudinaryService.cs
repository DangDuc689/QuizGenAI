using Microsoft.AspNetCore.Http;

namespace QuizGenAI.Services;

public interface ICloudinaryService
{
    /// <summary>
    /// Tải một ảnh lên Cloudinary và trả về đường dẫn URL an toàn (HTTPS).
    /// </summary>
    /// <param name="file">File ảnh cần tải lên</param>
    /// <param name="folderName">Tên thư mục trên Cloudinary</param>
    /// <returns>URL ảnh bảo mật, hoặc null nếu tải lên thất bại</returns>
    Task<string?> UploadImageAsync(IFormFile file, string folderName);

    /// <summary>
    /// Xóa một ảnh trên Cloudinary bằng URL ảnh.
    /// </summary>
    /// <param name="imageUrl">URL ảnh đầy đủ trên Cloudinary</param>
    /// <returns>True nếu xóa thành công, ngược lại False</returns>
    Task<bool> DeleteImageAsync(string imageUrl);
}
