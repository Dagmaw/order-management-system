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
        GssEncryptionMode = GssEncryptionMode.Disable,
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

    await ConfigureApplicationLoginAsync(connection);

    Console.WriteLine("Application login configured.");

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

static async Task ConfigureApplicationLoginAsync(
    NpgsqlConnectionStringBuilder settings)
{
    var username = Required("PGAPPUSER");
    var password = Required("PGAPPPASSWORD");

    if (username != "order_app")
    {
        throw new InvalidOperationException("Unexpected username.");
    }

    using var builder = new NpgsqlCommandBuilder();
    var role = builder.QuoteIdentifier(username);
    var database = builder.QuoteIdentifier(settings.Database!);
    var passwordLiteral = "E'"
        + password.Replace("\\", "\\\\").Replace("'", "''")
        + "'";

    await using var connection = new NpgsqlConnection(settings.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    await using var exists = new NpgsqlCommand(
        "SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @name)",
        connection, transaction);
    exists.Parameters.AddWithValue("name", username);

    if (!(bool)(await exists.ExecuteScalarAsync())!)
    {
        await using var create = new NpgsqlCommand(
            $"CREATE ROLE {role} LOGIN",
            connection, transaction);
        await create.ExecuteNonQueryAsync();
    }

    await using var command = new NpgsqlCommand($"""
        ALTER ROLE {role} LOGIN PASSWORD {passwordLiteral};
        GRANT CONNECT ON DATABASE {database} TO {role};
        GRANT USAGE ON SCHEMA public TO {role};
        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {role};
        GRANT USAGE ON ALL SEQUENCES IN SCHEMA public TO {role};
        REVOKE ALL ON TABLE public."__EFMigrationsHistory" FROM {role};
        ALTER DEFAULT PRIVILEGES IN SCHEMA public
            GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {role};
        ALTER DEFAULT PRIVILEGES IN SCHEMA public
            GRANT USAGE ON SEQUENCES TO {role};
        """, connection, transaction);

    await command.ExecuteNonQueryAsync();
    await transaction.CommitAsync();

    var appSettings = new NpgsqlConnectionStringBuilder(settings.ConnectionString)
    {
        Username = username,
        Password = password
    };

    await using var app = new NpgsqlConnection(appSettings.ConnectionString);
    await app.OpenAsync();
}