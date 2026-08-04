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
        connectionString = configuration["SUPABASE_DB_CONNECTION"]
            ?? Environment.GetEnvironmentVariable("SUPABASE_DB_CONNECTION");
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
            """;
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
