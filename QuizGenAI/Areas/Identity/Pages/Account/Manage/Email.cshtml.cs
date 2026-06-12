using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QuizGenAI.Models;

namespace QuizGenAI.Areas.Identity.Pages.Account.Manage;

[Authorize]
public class EmailModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public EmailModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    public string CurrentEmail { get; private set; } = string.Empty;

    [TempData]
    public string? StatusMessage { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required(ErrorMessage = "Vui lòng nhập email mới.")]
        [EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ.")]
        [Display(Name = "Email mới")]
        public string NewEmail { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu hiện tại.")]
        [DataType(DataType.Password)]
        [Display(Name = "Mật khẩu hiện tại")]
        public string CurrentPassword { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Không tìm thấy người dùng có ID '{_userManager.GetUserId(User)}'.");
        }

        CurrentEmail = user.Email ?? string.Empty;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Không tìm thấy người dùng có ID '{_userManager.GetUserId(User)}'.");
        }

        CurrentEmail = user.Email ?? string.Empty;
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var newEmail = Input.NewEmail.Trim();
        if (string.Equals(newEmail, CurrentEmail, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "Email mới giống email hiện tại.";
            return RedirectToPage();
        }

        if (!await _userManager.CheckPasswordAsync(user, Input.CurrentPassword))
        {
            ModelState.AddModelError("Input.CurrentPassword", "Mật khẩu hiện tại không đúng.");
            return Page();
        }

        var existingUser = await _userManager.FindByEmailAsync(newEmail);
        if (existingUser != null && existingUser.Id != user.Id)
        {
            ModelState.AddModelError("Input.NewEmail", "Email này đã được sử dụng bởi tài khoản khác.");
            return Page();
        }

        var changeEmailToken = await _userManager.GenerateChangeEmailTokenAsync(user, newEmail);
        var changeEmailResult = await _userManager.ChangeEmailAsync(user, newEmail, changeEmailToken);
        if (!changeEmailResult.Succeeded)
        {
            AddErrors(changeEmailResult);
            return Page();
        }

        var setUserNameResult = await _userManager.SetUserNameAsync(user, newEmail);
        if (!setUserNameResult.Succeeded)
        {
            AddErrors(setUserNameResult);
            return Page();
        }

        await _signInManager.RefreshSignInAsync(user);
        StatusMessage = "Email đăng nhập đã được thay đổi.";
        return RedirectToPage();
    }

    private void AddErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }
    }
}
