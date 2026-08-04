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
                email varchar(320),
                display_name varchar(80) not null,
                role varchar(20) not null,
                enabled boolean not null default true,
                created_at timestamptz not null default now(),
                updated_at timestamptz not null default now(),
                constraint ck_ares_admin_users_role
                    check (role in ('Owner', 'Administrator', 'Supervisor', 'Viewer'))
            );

            alter table ares_admin_users add column if not exists email varchar(320);

            create table if not exists ares_registration_requests (
                user_id uuid primary key,
                email varchar(320) not null,
                display_name varchar(80) not null,
                status varchar(20) not null default 'Pending',
                requested_at timestamptz not null default now(),
                reviewed_at timestamptz,
                reviewed_by uuid,
                constraint ck_ares_registration_status check (status in ('Pending', 'Approved', 'Rejected'))
            );

            create table if not exists ares_invitation_codes (
                invitation_id uuid primary key,
                code_hash bytea not null unique,
                code_prefix varchar(14) not null,
                max_uses integer not null,
                used_count integer not null default 0,
                expires_at timestamptz not null,
                revoked boolean not null default false,
                created_by uuid not null,
                created_at timestamptz not null default now(),
                constraint ck_ares_invitation_uses check (max_uses between 1 and 1000 and used_count >= 0)
            );

            alter table ares_state enable row level security;
            alter table ares_admin_users enable row level security;
            alter table ares_registration_requests enable row level security;
            alter table ares_invitation_codes enable row level security;

            revoke all on table ares_state from anon, authenticated;
            revoke all on table ares_admin_users from anon, authenticated;
            revoke all on table ares_registration_requests from anon, authenticated;
            revoke all on table ares_invitation_codes from anon, authenticated;
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

    public async Task<AdminUser?> GetAdminAsync(Guid userId)
    {
        if (!UsesDatabase) return null;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select user_id, coalesce(email, ''), display_name, role, enabled from ares_admin_users where user_id = @userId";
        command.Parameters.AddWithValue("userId", userId);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new AdminUser(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4))
            : null;
    }

    public async Task UpdateAdminEmailAsync(Guid userId, string email)
    {
        if (!UsesDatabase || string.IsNullOrWhiteSpace(email)) return;
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "update ares_admin_users set email=@email where user_id=@id and (email is null or email <> @email)";
        command.Parameters.AddWithValue("id", userId); command.Parameters.AddWithValue("email", email); await command.ExecuteNonQueryAsync();
    }

    public async Task RegisterPendingAsync(Guid userId, string email, string displayName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            insert into ares_registration_requests (user_id, email, display_name, status, requested_at)
            values (@id, @email, @name, 'Pending', now())
            on conflict (user_id) do update set email=excluded.email, display_name=excluded.display_name,
                status='Pending', requested_at=now(), reviewed_at=null, reviewed_by=null
            """;
        command.Parameters.AddWithValue("id", userId); command.Parameters.AddWithValue("email", email); command.Parameters.AddWithValue("name", displayName);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<List<RegistrationRequestInfo>> GetRegistrationsAsync()
    {
        var result = new List<RegistrationRequestInfo>();
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select user_id,email,display_name,status,requested_at,reviewed_at from ares_registration_requests order by requested_at desc";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4), reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5)));
        return result;
    }

    public async Task<List<AdminUser>> GetAdminsAsync()
    {
        var result = new List<AdminUser>();
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "select user_id,coalesce(email,''),display_name,role,enabled from ares_admin_users order by display_name";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4)));
        return result;
    }

    public async Task<bool> ApproveAsync(Guid userId, string role, Guid reviewer)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(); await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            insert into ares_admin_users(user_id,email,display_name,role,enabled,updated_at)
            select user_id,email,display_name,@role,true,now() from ares_registration_requests where user_id=@id and status='Pending'
            on conflict(user_id) do update set email=excluded.email,display_name=excluded.display_name,role=excluded.role,enabled=true,updated_at=now()
            """;
        command.Parameters.AddWithValue("id", userId); command.Parameters.AddWithValue("role", role);
        int changed = await command.ExecuteNonQueryAsync();
        command.Parameters.Clear(); command.CommandText = "update ares_registration_requests set status='Approved',reviewed_at=now(),reviewed_by=@reviewer where user_id=@id";
        command.Parameters.AddWithValue("id", userId); command.Parameters.AddWithValue("reviewer", reviewer); await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync(); return changed > 0;
    }

    public async Task ReviewRegistrationAsync(Guid userId, string status, Guid reviewer)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "update ares_registration_requests set status=@status,reviewed_at=now(),reviewed_by=@reviewer where user_id=@id";
        command.Parameters.AddWithValue("id", userId); command.Parameters.AddWithValue("status", status); command.Parameters.AddWithValue("reviewer", reviewer); await command.ExecuteNonQueryAsync();
    }

    public async Task<bool> UpdateAdminAsync(Guid userId, string role, bool enabled)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "update ares_admin_users set role=@role,enabled=@enabled,updated_at=now() where user_id=@id and role <> 'Owner'";
        command.Parameters.AddWithValue("id", userId); command.Parameters.AddWithValue("role", role); command.Parameters.AddWithValue("enabled", enabled); return await command.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> RemoveAdminAsync(Guid userId)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "delete from ares_admin_users where user_id=@id and role <> 'Owner'";
        command.Parameters.AddWithValue("id", userId); return await command.ExecuteNonQueryAsync() > 0;
    }

    public async Task<InvitationInfo> CreateInvitationAsync(byte[] codeHash, string prefix, int maxUses, DateTimeOffset expiresAt, Guid createdBy)
    {
        Guid id = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = """
            insert into ares_invitation_codes(invitation_id,code_hash,code_prefix,max_uses,expires_at,created_by)
            values(@id,@hash,@prefix,@max,@expires,@creator)
            """;
        command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("hash", codeHash); command.Parameters.AddWithValue("prefix", prefix);
        command.Parameters.AddWithValue("max", maxUses); command.Parameters.AddWithValue("expires", expiresAt); command.Parameters.AddWithValue("creator", createdBy);
        await command.ExecuteNonQueryAsync(); return new(id, prefix, maxUses, 0, expiresAt, false, DateTimeOffset.UtcNow);
    }

    public async Task<List<InvitationInfo>> GetInvitationsAsync()
    {
        var result = new List<InvitationInfo>(); await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "select invitation_id,code_prefix,max_uses,used_count,expires_at,revoked,created_at from ares_invitation_codes order by created_at desc limit 200";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetFieldValue<DateTimeOffset>(4), reader.GetBoolean(5), reader.GetFieldValue<DateTimeOffset>(6)));
        return result;
    }

    public async Task<Guid?> ConsumeInvitationAsync(byte[] codeHash)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = """
            update ares_invitation_codes set used_count=used_count+1
            where code_hash=@hash and not revoked and expires_at > now() and used_count < max_uses
            returning invitation_id
            """;
        command.Parameters.AddWithValue("hash", codeHash); object? value = await command.ExecuteScalarAsync(); return value is Guid id ? id : null;
    }

    public async Task RestoreInvitationUseAsync(Guid invitationId)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "update ares_invitation_codes set used_count=greatest(used_count-1,0) where invitation_id=@id";
        command.Parameters.AddWithValue("id", invitationId); await command.ExecuteNonQueryAsync();
    }

    public async Task RevokeInvitationAsync(Guid invitationId)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "update ares_invitation_codes set revoked=true where invitation_id=@id";
        command.Parameters.AddWithValue("id", invitationId); await command.ExecuteNonQueryAsync();
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

internal sealed record AdminUser(Guid UserId, string Email, string DisplayName, string Role, bool Enabled);
internal sealed record RegistrationRequestInfo(Guid UserId, string Email, string DisplayName, string Status, DateTimeOffset RequestedAt, DateTimeOffset? ReviewedAt);
internal sealed record InvitationInfo(Guid InvitationId, string CodePrefix, int MaxUses, int UsedCount, DateTimeOffset ExpiresAt, bool Revoked, DateTimeOffset CreatedAt);
