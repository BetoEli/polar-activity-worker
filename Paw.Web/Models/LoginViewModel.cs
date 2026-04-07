using System.ComponentModel.DataAnnotations;

namespace Paw.Web.Models;

public class LoginViewModel
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Enter a valid email address")]
    public string Email { get; set; } = "";

    public string? ReturnUrl { get; set; }
}
