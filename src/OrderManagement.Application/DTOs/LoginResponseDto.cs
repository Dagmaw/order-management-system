

namespace OrderManagement.Application.DTOs
{
    public class LoginResponseDto
    {
        public string AccessToken { get; set; } = string.Empty;

        public string TokenType { get; set; } = "Bearer";

        public DateTimeOffset ExpiresAtUtc { get; set; }

        public int UserId { get; set; }

        public string Email { get; set; } = string.Empty;

        public string[] Roles { get; set; } = Array.Empty<string>();
    }
}
