using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

internal sealed class AresPersistence
{
    private readonly string? connectionString;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    public bool UsesDatabase => !string.IsNullOrWhiteSpace(connectionString);

    public AresPersistence(IConfiguration configuration)
    {
        string? configured = configuration["SUPABASE_DB_CONNECTION"]
            ?? Environment.GetEnvironmentVariable("SUPABASE_DB_CONNECTION");
        connectionString = NormalizeConnectionString(configured);
    }

    private static string? NormalizeConnectionString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return value;

        var uri = new Uri(value);
        string[] userInfo = uri.UserInfo.Split(':', 2);
        if (userInfo.Length != 2)
            throw new InvalidOperationException("La URL de Supabase no contiene usuario y contraseña.");

        return new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = Uri.UnescapeDataString(userInfo[1]),
            SslMode = SslMode.Require,
            Pooling = true
        }.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        if (!UsesDatabase) return;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            create table if not exists ares_state (
                state_key text primary key,
                state_value jsonb not null,
                updated_at timestamptz not null default now()
            );

            create table if not exists ares_admin_users (
                user_id uuid primary key,
                display_name varchar(80) not null,
                role varchar(20) not null,
                enabled boolean not null default true,
                created_at timestamptz not null default now(),
                updated_at timestamptz not null default now(),
                constraint ck_ares_admin_users_role
                    check (role in ('Owner', 'Administrator', 'Supervisor', 'Viewer'))
            );

            alter table ares_state enable row level security;
            alter table ares_admin_users enable row level security;

            revoke all on table ares_state from anon, authenticated;
            revoke all on table ares_admin_users from anon, authenticated;
            """;
        await command.ExecuteNonQueryAsync();
    }

    public async Task EnsureOwnerAsync(string? userId, string? displayName)
    {
        if (!UsesDatabase || string.IsNullOrWhiteSpace(userId)) return;
        if (!Guid.TryParse(userId, out Guid ownerId))
            throw new InvalidOperationException("ARES_OWNER_USER_ID no contiene un UUID valido.");

        string name = string.IsNullOrWhiteSpace(displayName) ? "ADMINISTRADOR" : displayName.Trim();
        if (name.Length > 80) name = name[..80];

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            insert into ares_admin_users (user_id, display_name, role, enabled, updated_at)
            values (@userId, @displayName, 'Owner', true, now())
            on conflict (user_id) do update
            set display_name = excluded.display_name,
                role = 'Owner', enabled = true, updated_at = now()
            """;
        command.Parameters.AddWithValue("userId", ownerId);
        command.Parameters.AddWithValue("displayName", name);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T?> LoadAsync<T>(string key)
    {
        if (!UsesDatabase) return default;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select state_value::text from ares_state where state_key = @key";
        command.Parameters.AddWithValue("key", key);
        object? result = await command.ExecuteScalarAsync();
        return result is string json ? JsonSerializer.Deserialize<T>(json, jsonOptions) : default;
    }

    public async Task SaveAsync<T>(string key, T value)
    {
        if (!UsesDatabase) return;

        string json = JsonSerializer.Serialize(value, jsonOptions);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            insert into ares_state (state_key, state_value, updated_at)
            values (@key, @value, now())
            on conflict (state_key) do update
            set state_value = excluded.state_value, updated_at = now()
            """;
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("value", NpgsqlDbType.Jsonb, json);
        await command.ExecuteNonQueryAsync();
    }
}
