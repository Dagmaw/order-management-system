using System.Globalization;
using System.Security.Claims;

namespace OrderManagement.API.Security;

public static class ClaimsPrincipalExtensions
{
    public static bool TryGetDomainUserId(
        this ClaimsPrincipal principal,
        out int userId)
    {
        return int.TryParse(
                   principal.FindFirst("user_id")?.Value,
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out userId)
               && userId > 0;
    }
}