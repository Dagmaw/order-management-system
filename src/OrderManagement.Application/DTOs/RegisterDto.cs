using System.ComponentModel.DataAnnotations;

namespace OrderManagement.Application.DTOs
{
    public class RegisterDto
    {
        [Required]
        [EmailAddress]
        [StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(128, MinimumLength = 15)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;
    }
}
