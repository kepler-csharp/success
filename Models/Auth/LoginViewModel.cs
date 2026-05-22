using System.ComponentModel.DataAnnotations;

namespace success.Models.Auth;

public class LoginViewModel
{
    [Required(ErrorMessage = "Email is needed.")]
    [EmailAddress(ErrorMessage = "Write a valid email.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Password is needed.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
}
