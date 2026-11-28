namespace OrderManagement.Infrastructure.Inventory;

public sealed class InventoryClientOptions
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "http://inventory:8080/";
    public int TimeoutSeconds { get; set; } = 5;
    public string Issuer { get; set; } = "Inventory.LocalIssuer";
    public string Audience { get; set; } = "Inventory.API";
    public string PrivateKeyPath { get; set; } = "";
    public bool IsValid() => Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
        && uri.AbsolutePath == "/"
        && (uri.Scheme == "https" || (uri.Scheme == "http" && (uri.IsLoopback || uri.Host == "inventory")))
        && TimeoutSeconds is >= 1 and <= 30 && !string.IsNullOrWhiteSpace(Issuer)
        && !string.IsNullOrWhiteSpace(Audience) && File.Exists(PrivateKeyPath);
}
