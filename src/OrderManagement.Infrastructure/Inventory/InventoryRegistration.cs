using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OrderManagement.Application.Interfaces;

namespace OrderManagement.Infrastructure.Inventory;

public static class InventoryRegistration
{
    public static IServiceCollection AddInventoryClient(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!configuration.GetValue<bool>("Inventory:Enabled")) return services;
        var mode = configuration.GetValue<string>("Inventory:AuthenticationMode") ?? "LocalRsa";
        if (mode is not ("LocalRsa" or "ClientCredentials"))
            throw new InvalidOperationException("Inventory authentication mode must be LocalRsa or ClientCredentials.");
        if (mode == "LocalRsa" && !environment.IsDevelopment())
            throw new InvalidOperationException("Local Inventory token issuance is allowed only in Development.");
        services.AddOptions<InventoryClientOptions>().Bind(configuration.GetSection("Inventory"))
            .Validate(options => options.IsValid(), "Configure valid Inventory connection and authentication settings.")
            .Validate(options => environment.IsDevelopment() || InventoryClientOptions.IsHttpsEndpoint(options.BaseUrl),
                "Inventory requires HTTPS outside Development.")
            .ValidateOnStart();
        if (mode == "LocalRsa")
            services.AddSingleton<IInventoryServiceTokenProvider, InventoryServiceTokenProvider>();
        else
        {
            services.AddSingleton<TimeProvider>(TimeProvider.System);
            services.AddHttpClient(OAuthInventoryTokenProvider.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                client.MaxResponseContentBufferSize = 64 * 1024;
            }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
            services.AddSingleton<IInventoryServiceTokenProvider, OAuthInventoryTokenProvider>();
        }
        services.AddScoped<OrderSubmissionCoordinator>();
        services.AddHostedService<SubmissionRecoveryWorker>();
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
