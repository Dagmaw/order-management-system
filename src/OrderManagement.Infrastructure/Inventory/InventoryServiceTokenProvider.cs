using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace OrderManagement.Infrastructure.Inventory;

public interface IInventoryServiceTokenProvider { string CreateToken(); }

// Development-only issuer adapter. Replace with identity-provider token acquisition later.
public sealed class InventoryServiceTokenProvider : IInventoryServiceTokenProvider, IDisposable
{
    private readonly RSA _rsa = RSA.Create();
    private readonly InventoryClientOptions _options;
    private readonly object _gate = new();
    public InventoryServiceTokenProvider(IOptions<InventoryClientOptions> options)
    {
        _options = options.Value;
        try { _rsa.ImportFromPem(File.ReadAllText(_options.PrivateKeyPath)); _rsa.ExportParameters(true); }
        catch { _rsa.Dispose(); throw new InvalidOperationException("A valid local Inventory private signing key is required."); }
        if (_rsa.KeySize < 2048) { _rsa.Dispose(); throw new InvalidOperationException("Inventory RSA key must be at least 2048 bits."); }
    }
    public string CreateToken()
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            var token = new JwtSecurityToken(_options.Issuer, _options.Audience,
                [new Claim("sub", "order-management-api"), new Claim("scope", "inventory.reserve"),
                 new Claim("jti", Guid.NewGuid().ToString())],
                notBefore: now, expires: now.AddMinutes(2),
                signingCredentials: new SigningCredentials(new RsaSecurityKey(_rsa), SecurityAlgorithms.RsaSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
    public void Dispose() => _rsa.Dispose();
}
