using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OrderManagement.Application.Exceptions;
using OrderManagement.Infrastructure.Inventory;
namespace OrderManagement.API.Tests;
public class InventoryIdentityBoundaryTests
{
    private sealed class Production : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    [Fact]
    public void ProductionCannotEnableLocalSigning()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
            { ["Inventory:Enabled"]="true" }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddInventoryClient(config,new Production()));
    }
    [Fact]
    public void ProductionRejectsPlainHttpInventoryConnection()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["Inventory:Enabled"]="true", ["Inventory:AuthenticationMode"]="ClientCredentials",
            ["Inventory:BaseUrl"]="http://inventory:8080/", ["Inventory:TokenEndpoint"]="https://identity.example/token",
            ["Inventory:ClientId"]="orders", ["Inventory:ClientSecret"]="test-only"
        }).Build();
        using var services = new ServiceCollection().AddLogging().AddInventoryClient(config,new Production()).BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<InventoryClientOptions>>().Value);
    }
    private sealed class WaitingTokens : IInventoryServiceTokenProvider
    {
        public async ValueTask<string> GetTokenAsync(CancellationToken cancellationToken=default)
        { await Task.Delay(Timeout.InfiniteTimeSpan,cancellationToken); return "unreachable"; }
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        { Calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }
    }
    [Fact]
    public async Task TokenAcquisitionTimeoutDoesNotReportUnknownInventoryMutation()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler) { BaseAddress=new Uri("https://inventory.example/") };
        var client = new InventoryHttpClient(http,new WaitingTokens(),Options.Create(new InventoryClientOptions { TimeoutSeconds=1 }));
        var error = await Assert.ThrowsAsync<InventoryUnavailableException>(() => client.ReserveAsync(Guid.NewGuid(),[new(1,1)]));
        Assert.False(error.OutcomeUnknown); Assert.Equal(0,handler.Calls);
    }
}
