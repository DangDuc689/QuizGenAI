using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QuizGenAI.Models;
using QuizGenAI.Services;

namespace QuizGenAI.Areas.Identity.Pages.Account.Manage;

[Authorize]
public class IndexModel : PageModel
{
    private const long MaxAvatarSize = 2 * 1024 * 1024;
    private static readonly HashSet<string> AllowedAvatarExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };
    private static readonly HashSet<string> AllowedAvatarContentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp" };

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<IndexModel> _logger;
    private readonly ICloudinaryService _cloudinaryService;

    public IndexModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IWebHostEnvironment environment,
        ILogger<IndexModel> logger,
        ICloudinaryService cloudinaryService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _environment = environment;
        _logger = logger;
        _cloudinaryService = cloudinaryService;
    }

    public string UserId { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
    public string AccountStatus { get; private set; } = string.Empty;
    public string Roles { get; private set; } = string.Empty;
    public string? AvatarPath { get; private set; }
    public bool EmailConfirmed { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required(ErrorMessage = "Vui lòng nhập họ và tên.")]
        [StringLength(100, ErrorMessage = "Họ và tên không được vượt quá 100 ký tự.")]
        [Display(Name = "Họ và tên")]
        public string FullName { get; set; } = string.Empty;

        [StringLength(200, ErrorMessage = "Địa chỉ không được vượt quá 200 ký tự.")]
        [Display(Name = "Địa chỉ")]
        public string? Address { get; set; }

        [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
        [Display(Name = "Số điện thoại")]
        public string? PhoneNumber { get; set; }

        [Display(Name = "Ảnh đại diện")]
        public IFormFile? Avatar { get; set; }

        [Display(Name = "Xóa ảnh đại diện hiện tại")]
        public bool RemoveAvatar { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Không tìm thấy người dùng có ID '{_userManager.GetUserId(User)}'.");
        }

        await LoadAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Không tìm thấy người dùng có ID '{_userManager.GetUserId(User)}'.");
        }

        ValidateAvatar(Input.Avatar);
        if (!ModelState.IsValid)
        {
            await LoadMetadataAsync(user);
            return Page();
        }

        string? newAvatarPath = null;
        var oldAvatarPath = user.AvatarPath;

        try
        {
            if (Input.Avatar is { Length: > 0 })
            {
                // Upload avatar lên Cloudinary thay vì lưu local
                newAvatarPath = await _cloudinaryService.UploadImageAsync(Input.Avatar, "avatars");
                
                if (string.IsNullOrEmpty(newAvatarPath))
                {
                    ModelState.AddModelError("Input.Avatar", "Không thể tải ảnh đại diện lên Cloudinary. Vui lòng thử lại.");
                    await LoadMetadataAsync(user);
                    return Page();
                }
                
                user.AvatarPath = newAvatarPath;
            }
            else if (Input.RemoveAvatar)
            {
                user.AvatarPath = null;
            }

            user.FullName = Input.FullName.Trim();
            user.Address = string.IsNullOrWhiteSpace(Input.Address) ? null : Input.Address.Trim();
            user.PhoneNumber = string.IsNullOrWhiteSpace(Input.PhoneNumber) ? null : Input.PhoneNumber.Trim();

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                AddErrors(result);
                // Xóa ảnh mới tải lên nếu update DB thất bại
                await DeleteAvatarAsync(newAvatarPath);
                user.AvatarPath = oldAvatarPath;
                await LoadMetadataAsync(user);
                return Page();
            }

            if ((newAvatarPath != null || Input.RemoveAvatar) && oldAvatarPath != user.AvatarPath)
            {
                // Dọn dẹp ảnh cũ (có thể ở local hoặc Cloudinary)
                await DeleteAvatarAsync(oldAvatarPath);
            }

            await _signInManager.RefreshSignInAsync(user);
            StatusMessage = "Thông tin tài khoản đã được cập nhật.";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            await DeleteAvatarAsync(newAvatarPath);
            _logger.LogError(ex, "Failed to update profile for user {UserId}.", user.Id);
            ModelState.AddModelError(string.Empty, "Không thể cập nhật thông tin lúc này. Vui lòng thử lại.");
            await LoadMetadataAsync(user);
            return Page();
        }
    }

    private async Task LoadAsync(ApplicationUser user)
    {
        Input = new InputModel
        {
            FullName = user.FullName,
            Address = user.Address,
            PhoneNumber = user.PhoneNumber
        };

        await LoadMetadataAsync(user);
    }

    private async Task LoadMetadataAsync(ApplicationUser user)
    {
        UserId = user.Id;
        Email = user.Email ?? string.Empty;
        CreatedAt = user.CreatedAt;
        AccountStatus = user.IsActive ? "Đang hoạt động" : "Đã khóa";
        AvatarPath = user.AvatarPath;
        EmailConfirmed = user.EmailConfirmed;
        var roles = await _userManager.GetRolesAsync(user);
        Roles = roles.Count > 0 ? string.Join(", ", roles) : "Người dùng";
    }

    private void ValidateAvatar(IFormFile? avatar)
    {
        if (avatar == null || avatar.Length == 0)
        {
            return;
        }

        if (avatar.Length > MaxAvatarSize)
        {
            ModelState.AddModelError("Input.Avatar", "Ảnh đại diện không được vượt quá 2 MB.");
        }

        var extension = Path.GetExtension(avatar.FileName);
        if (!AllowedAvatarExtensions.Contains(extension))
        {
            ModelState.AddModelError("Input.Avatar", "Chỉ hỗ trợ ảnh JPG, JPEG, PNG hoặc WEBP.");
        }

        if (!AllowedAvatarContentTypes.Contains(avatar.ContentType))
        {
            ModelState.AddModelError("Input.Avatar", "Nội dung file ảnh không hợp lệ.");
        }

        if (!HasValidImageSignature(avatar, extension))
        {
            ModelState.AddModelError("Input.Avatar", "File được chọn không phải là ảnh hợp lệ.");
        }
    }

    private static bool HasValidImageSignature(IFormFile avatar, string extension)
    {
        Span<byte> header = stackalloc byte[12];
        using var stream = avatar.OpenReadStream();
        var bytesRead = stream.Read(header);

        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => bytesRead >= 3
                && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            ".png" => bytesRead >= 8
                && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            ".webp" => bytesRead >= 12
                && header[..4].SequenceEqual("RIFF"u8)
                && header.Slice(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };
    }

    private async Task DeleteAvatarAsync(string? avatarPath)
    {
        if (string.IsNullOrWhiteSpace(avatarPath))
        {
            return;
        }

        // Nếu là link ảnh Cloudinary thì xóa trên Cloudinary
        if (avatarPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || avatarPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            await _cloudinaryService.DeleteImageAsync(avatarPath);
        }
        else
        {
            // Nếu là file local thì xóa vật lý
            DeleteAvatarFile(avatarPath);
        }
    }

    private void DeleteAvatarFile(string? avatarPath)
    {
        if (string.IsNullOrWhiteSpace(avatarPath)
            || !avatarPath.StartsWith("/uploads/avatars/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var fileName = Path.GetFileName(avatarPath);
        var fullPath = Path.Combine(_environment.WebRootPath, "uploads", "avatars", fileName);
        if (System.IO.File.Exists(fullPath))
        {
            System.IO.File.Delete(fullPath);
        }
    }

    private void AddErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }
    }
}
