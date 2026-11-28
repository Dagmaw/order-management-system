using Npgsql;

internal static class InventoryDatabaseBootstrap
{
    private const string Marker = "ManagedBy=order-management-system;Service=inventory";

    public static async Task InitializeAsync(NpgsqlConnectionStringBuilder settings,
        string username, string password)
    {
        if (settings.Database != "postgres" || username != "inventory_app"
            || password.Length < 32 || settings.Username == username)
            throw new InvalidOperationException("Invalid Inventory bootstrap configuration.");

        var adminSettings = new NpgsqlConnectionStringBuilder(settings.ConnectionString)
        {
            Pooling = false
        };
        await using var admin = new NpgsqlConnection(adminSettings.ConnectionString);
        await admin.OpenAsync();
        // A dedicated physical connection releases the session lock even on failure.
        await Execute(admin, "SELECT pg_advisory_lock(1302, 1)");
        await using var ownerCommand = new NpgsqlCommand(
            "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = 'inventory'", admin);
        var owner = await ownerCommand.ExecuteScalarAsync() as string;
        if (owner is not null && owner != username)
            throw new InvalidOperationException("Inventory database has an unexpected owner.");

        await using var roleCommand = new NpgsqlCommand("""
            SELECT rolcanlogin AND NOT rolsuper AND NOT rolcreaterole AND NOT rolcreatedb
                AND NOT rolreplication AND NOT rolbypassrls,
                shobj_description(oid, 'pg_authid')
            FROM pg_roles WHERE rolname = 'inventory_app'
            """, admin);
        bool exists;
        await using (var reader = await roleCommand.ExecuteReaderAsync())
        {
            exists = await reader.ReadAsync();
            if (exists && (!reader.GetBoolean(0) || reader.IsDBNull(1)
                || reader.GetString(1) != Marker))
                throw new InvalidOperationException("Inventory role is not a managed application login.");
        }
        // Refuse inherited privileges or membership that would break service isolation.
        await using var membership = new NpgsqlCommand("""
            SELECT EXISTS (SELECT 1 FROM pg_auth_members m
                JOIN pg_roles r ON r.oid = m.member WHERE r.rolname = 'inventory_app')
            """, admin);
        if ((bool)(await membership.ExecuteScalarAsync())!)
            throw new InvalidOperationException("Inventory login has unexpected role memberships.");

        using var identifiers = new NpgsqlCommandBuilder();
        var adminRole = identifiers.QuoteIdentifier(settings.Username!);
        await using var transaction = await admin.BeginTransactionAsync();
        if (!exists)
            await Execute(admin, "CREATE ROLE inventory_app LOGIN", transaction);
        await using var literalCommand = new NpgsqlCommand("SELECT quote_literal(@password)", admin, transaction);
        literalCommand.Parameters.AddWithValue("password", password);
        var literal = (string)(await literalCommand.ExecuteScalarAsync())!;
        await Execute(admin, $"ALTER ROLE inventory_app PASSWORD {literal}", transaction);
        await Execute(admin, $"COMMENT ON ROLE inventory_app IS '{Marker}'", transaction);
        // PostgreSQL requires SET ROLE permission to create a database owned by another role.
        await Execute(admin, $"GRANT inventory_app TO {adminRole}", transaction);
        await transaction.CommitAsync();

        if (owner is null)
            await Execute(admin, "CREATE DATABASE inventory OWNER inventory_app");
        await Execute(admin, "REVOKE ALL ON DATABASE inventory FROM PUBLIC");
        var appSettings = new NpgsqlConnectionStringBuilder(settings.ConnectionString)
        {
            Database = "inventory", Username = username, Password = password, Pooling = false
        };
        await using var app = new NpgsqlConnection(appSettings.ConnectionString);
        await app.OpenAsync();
        Console.WriteLine("Inventory database and dedicated application login verified.");
    }

    private static async Task Execute(NpgsqlConnection connection, string sql,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }
}
