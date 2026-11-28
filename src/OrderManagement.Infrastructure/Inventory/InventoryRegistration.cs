using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OrderManagement.Application.Interfaces;

namespace OrderManagement.Infrastructure.Inventory;

public static class InventoryRegistration
{
    public static IServiceCollection AddLocalInventoryClient(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!configuration.GetValue<bool>("Inventory:Enabled")) return services;
        if (!environment.IsDevelopment())
            throw new InvalidOperationException("Local Inventory token issuance is allowed only in Development. Configure a production identity provider before AWS integration.");
        services.AddOptions<InventoryClientOptions>().Bind(configuration.GetSection("Inventory"))
            .Validate(options => options.IsValid(), "Configure a valid Inventory base URL, timeout, issuer, audience, and private-key file.")
            .ValidateOnStart();
        services.AddSingleton<IInventoryServiceTokenProvider, InventoryServiceTokenProvider>();
        services.AddHttpClient<IInventoryClient, InventoryHttpClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<InventoryClientOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.MaxResponseContentBufferSize = 64 * 1024;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        return services;
    }
}
