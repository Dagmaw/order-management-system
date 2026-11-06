using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderManagement.Infrastructure.Data;
using System.Security.Cryptography;

namespace OrderManagement.API.Tests.Infrastructure;

public class TestApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    private readonly string _signingKey =
        Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(32));

    public TestApiFactory()
    {
        _connection = new SqliteConnection(
            "Data Source=:memory:");

        _connection.Open();
    }

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(
            (_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Jwt:Issuer"] =
                            "OrderManagement.Tests",

                        ["Jwt:Audience"] =
                            "OrderManagement.TestClient",

                        ["Jwt:SigningKeyBase64"] =
                            _signingKey,

                        ["Jwt:AccessTokenMinutes"] =
                            "15",

                        ["BootstrapAdmin:Enabled"] =
                            "false"
                    });
            });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ApplicationDbContext>();

            services.RemoveAll<
                DbContextOptions<ApplicationDbContext>>();

            services.RemoveAll<
                IDbContextOptionsConfiguration<ApplicationDbContext>>();

            services.AddDbContext<ApplicationDbContext>(
                options => options.UseSqlite(_connection));

            var databaseOptions =
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseSqlite(_connection)
                    .Options;

            using var db = new ApplicationDbContext(
                databaseOptions);

            db.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}