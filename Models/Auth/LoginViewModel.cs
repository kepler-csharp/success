using System.ComponentModel.DataAnnotations;

namespace success.Models.Auth;

public class LoginViewModel
{
    [Required(ErrorMessage = "Enter your email.")]
    [EmailAddress(ErrorMessage = "Enter a valid email.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Enter your password.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
}
