using System.ComponentModel.DataAnnotations;

namespace OrderManagement.Application.DTOs
{
    public class LoginDto
    {
        [Required]
        [EmailAddress]
        [StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(120)]
        public string Password { get; set; } = string.Empty;
    }
}
