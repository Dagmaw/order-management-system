using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using OrderManagement.Application.Interfaces;
using OrderManagement.Infrastructure.Inventory;
using System.Text.Json;

if (args.Length != 1 || args[0] != "--live")
{
    Console.WriteLine("Run from the repository root with --live to reserve/release ONE local unit for product 1. Requires a local key and running Inventory.");
    return 0;
}
var root = Directory.GetCurrentDirectory();
var statePath = Path.Combine(root, ".local-inventory-auth", "client-probe-state.json");
var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Inventory:Enabled"] = "true",
    ["Inventory:BaseUrl"] = "http://localhost:5081/",
    ["Inventory:PrivateKeyPath"] = Path.Combine(root, ".local-inventory-auth", "private-key.pem")
}).Build();
var services = new ServiceCollection().AddLogging();
services.AddLocalInventoryClient(settings, new LocalEnvironment());
using var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<IInventoryClient>();
Guid id;
if (File.Exists(statePath))
{
    id = JsonDocument.Parse(File.ReadAllText(statePath)).RootElement.GetProperty("reservationId").GetGuid();
    Console.WriteLine("Reconciling the previously recorded probe ID before creating any new reservation.");
}
else
{
    id = Guid.NewGuid();
    File.WriteAllText(statePath, JsonSerializer.Serialize(new { reservationId = id }));
}
try
{
    var prior = await client.GetReservationAsync(id);
    if (prior is null) prior = await client.ReserveAsync(id, [new(1, 1)]);
    if (prior.Status == "RESERVED")
    {
        var replay = await client.ReserveAsync(id, [new(1, 1)]);
        Console.WriteLine($"Reserve/replay: {replay.Status}. ID: {id}");
        await client.ReleaseAsync(id);
        var released = await client.ReleaseAsync(id);
        Console.WriteLine($"Release/replay: {released.Status}. Quantity restored; stock version may increase.");
    }
    else Console.WriteLine("Previous probe is already RELEASED; no stock change performed.");
    Console.WriteLine("Local .NET client → authenticated Java Inventory verification passed. No token or private key printed.");
    return 0;
}
catch
{
    Console.Error.WriteLine($"Probe did not complete. Keep the recorded ID {id}; rerun to reconcile. Do not assume a timed-out request failed.");
    return 1;
}

sealed class LocalEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "InventoryClientProbe";
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
