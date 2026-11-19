using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderManagement.Infrastructure.Data;

try
{
    var rootCertificate = Required("PGSSLROOTCERT");

    if (!File.Exists(rootCertificate))
    {
        throw new InvalidOperationException(
            "The RDS CA certificate file is missing.");
    }

    var connection = new NpgsqlConnectionStringBuilder
    {
        Host = Required("PGHOST"),
        Port = 5432,
        Database = Required("PGDATABASE"),
        Username = Required("PGUSER"),
        Password = Required("PGPASSWORD"),
        SslMode = SslMode.VerifyFull,
        RootCertificate = rootCertificate,
        Timeout = 30,
        CommandTimeout = 300,
        IncludeErrorDetail = false
    };

    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(connection.ConnectionString)
        .Options;

    await using var database = new ApplicationDbContext(options);

    Console.WriteLine("Checking pending database migrations.");

    var pending = (
        await database.Database.GetPendingMigrationsAsync()
    ).ToArray();

    Console.WriteLine($"Pending migrations: {pending.Length}");

    await database.Database.MigrateAsync();

    Console.WriteLine("Database migrations completed successfully.");

    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"Database migration failed ({exception.GetType().Name}).");

    if (exception is PostgresException postgres)
    {
        Console.Error.WriteLine(
            $"PostgreSQL SQLSTATE: {postgres.SqlState}");
    }

    return 1;
}

static string Required(string name)
{
    var value = Environment.GetEnvironmentVariable(name);

    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException(
            $"Required environment variable '{name}' is missing.");
    }

    return value;
}