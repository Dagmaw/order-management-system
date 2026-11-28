namespace OrderManagement.Infrastructure.Inventory;

public sealed class InventoryClientOptions
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "http://inventory:8080/";
    public int TimeoutSeconds { get; set; } = 5;
    public string AuthenticationMode { get; set; } = "LocalRsa";
    public string Issuer { get; set; } = "Inventory.LocalIssuer";
    public string Audience { get; set; } = "Inventory.API";
    public string PrivateKeyPath { get; set; } = "";
    public string TokenEndpoint { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Scope { get; set; } = "inventory.reserve";

    public bool IsValid() => Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
        && uri.AbsolutePath == "/"
        && (uri.Scheme == "https" || (uri.Scheme == "http" && (uri.IsLoopback || uri.Host == "inventory")))
        && TimeoutSeconds is >= 1 and <= 30
        && (AuthenticationMode switch
        {
            "LocalRsa" => !string.IsNullOrWhiteSpace(Issuer) && !string.IsNullOrWhiteSpace(Audience)
                && File.Exists(PrivateKeyPath),
            "ClientCredentials" => IsHttpsEndpoint(TokenEndpoint)
                && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret)
                && !string.IsNullOrWhiteSpace(Scope) && Scope.Length <= 1024
                && Scope.Split(' ').All(s => s.Length > 0 && s.All(c => c is >= '!' and <= '~' && c != '"' && c != '\\')),
            _ => false
        });

    public static bool IsHttpsEndpoint(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
}
