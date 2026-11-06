

namespace OrderManagement.Infrastructure.Identity
{
    public class JwtOptions
    {
        public const string SectionName = "Jwt";

        public string Issuer { get; set; } = string.Empty;

        public string Audience { get; set; } = string.Empty;
        public string SigningKeyBase64 { get; set; } = string.Empty;

        public int AccessTokenMinutes { get; set; } = 15;

        public bool HasValidSigningKey()
        {
            try
            {
                return Convert.FromBase64String(SigningKeyBase64).Length >= 32;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
