using System.ComponentModel.DataAnnotations;

namespace success.Models.Auth;

public class LoginViewModel
{
    [Required(ErrorMessage = "Ingresa tu correo.")]
    [EmailAddress(ErrorMessage = "Ingresa un correo valido.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Ingresa tu contrasena.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
}
