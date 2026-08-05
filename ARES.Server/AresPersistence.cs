using System.Text.Json;
using System.Text;
using System.Globalization;
using Npgsql;
using NpgsqlTypes;

internal sealed class AresPersistence
{
    public static readonly Guid DefaultOrganizationId = Guid.Parse("00000000-0000-0000-0000-000000000001");
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
            create table if not exists ares_organizations (
                organization_id uuid primary key,
                name varchar(120) not null,
                slug varchar(80) not null unique,
                enabled boolean not null default true,
                created_at timestamptz not null default now(),
                updated_at timestamptz not null default now()
            );
            insert into ares_organizations(organization_id,name,slug)
            values ('00000000-0000-0000-0000-000000000001','Organización principal','principal')
            on conflict (organization_id) do nothing;
            alter table ares_organizations add column if not exists onboarding_completed boolean not null default false;
            update ares_organizations set onboarding_completed=true where organization_id='00000000-0000-0000-0000-000000000001';

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
            alter table ares_admin_users add column if not exists organization_id uuid;
            update ares_admin_users set organization_id='00000000-0000-0000-0000-000000000001' where organization_id is null;
            alter table ares_admin_users alter column organization_id set not null;

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
            alter table ares_registration_requests add column if not exists organization_id uuid;
            alter table ares_registration_requests add column if not exists invited_role varchar(20) not null default 'Viewer';
            update ares_registration_requests set organization_id='00000000-0000-0000-0000-000000000001' where organization_id is null;
            alter table ares_registration_requests alter column organization_id set not null;

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
            alter table ares_invitation_codes add column if not exists organization_id uuid;
            alter table ares_invitation_codes add column if not exists invited_role varchar(20) not null default 'Viewer';
            update ares_invitation_codes set organization_id='00000000-0000-0000-0000-000000000001' where organization_id is null;
            alter table ares_invitation_codes alter column organization_id set not null;

            create table if not exists ares_device_enrollment_codes (
                enrollment_id uuid primary key,
                organization_id uuid not null,
                code_hash bytea not null unique,
                code_prefix varchar(18) not null,
                assigned_group varchar(60) not null default 'General',
                max_uses integer not null default 1,
                used_count integer not null default 0,
                expires_at timestamptz not null,
                revoked boolean not null default false,
                created_by uuid not null,
                created_at timestamptz not null default now()
            );

            create table if not exists ares_devices (
                organization_id uuid not null,
                device_id varchar(64) not null,
                machine_name varchar(100) not null,
                assigned_group varchar(60) not null default 'General',
                credential_hash bytea not null unique,
                previous_credential_hash bytea,
                enabled boolean not null default true,
                enrollment_id uuid,
                enrolled_at timestamptz not null default now(),
                last_seen_at timestamptz,
                primary key (organization_id, device_id)
            );
            alter table ares_device_enrollment_codes alter column assigned_group type varchar(60);
            alter table ares_devices add column if not exists assigned_group varchar(60) not null default 'General';
            alter table ares_devices alter column assigned_group type varchar(60);
            alter table ares_devices add column if not exists rotation_requested boolean not null default false;
            alter table ares_devices add column if not exists previous_credential_hash bytea;

            alter table ares_state enable row level security;
            alter table ares_admin_users enable row level security;
            alter table ares_registration_requests enable row level security;
            alter table ares_invitation_codes enable row level security;
            alter table ares_organizations enable row level security;
            alter table ares_device_enrollment_codes enable row level security;
            alter table ares_devices enable row level security;

            revoke all on table ares_state from anon, authenticated;
            revoke all on table ares_admin_users from anon, authenticated;
            revoke all on table ares_registration_requests from anon, authenticated;
            revoke all on table ares_invitation_codes from anon, authenticated;
            revoke all on table ares_organizations from anon, authenticated;
            revoke all on table ares_device_enrollment_codes from anon, authenticated;
            revoke all on table ares_devices from anon, authenticated;
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
            insert into ares_admin_users (user_id, organization_id, display_name, role, enabled, updated_at)
            values (@userId, @organizationId, @displayName, 'Owner', true, now())
            on conflict (user_id) do update
            set display_name = excluded.display_name,
                organization_id = excluded.organization_id, role = 'Owner', enabled = true, updated_at = now()
            """;
        command.Parameters.AddWithValue("userId", ownerId);
        command.Parameters.AddWithValue("organizationId", DefaultOrganizationId);
        command.Parameters.AddWithValue("displayName", name);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<Guid> CreateOrganizationOwnerAsync(Guid userId, string email, string displayName, string organizationName)
    {
        Guid organizationId = Guid.NewGuid();
        string baseSlug = new string(organizationName.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray()).Trim('-');
        while (baseSlug.Contains("--", StringComparison.Ordinal)) baseSlug = baseSlug.Replace("--", "-", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(baseSlug)) baseSlug = "organizacion";
        if (baseSlug.Length > 60) baseSlug = baseSlug[..60].TrimEnd('-');
        string slug = $"{baseSlug}-{organizationId.ToString("N")[..8]}";

        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "insert into ares_organizations(organization_id,name,slug,enabled) values(@organization,@name,@slug,true)";
        command.Parameters.AddWithValue("organization", organizationId); command.Parameters.AddWithValue("name", organizationName);
        command.Parameters.AddWithValue("slug", slug); await command.ExecuteNonQueryAsync();
        command.Parameters.Clear();
        command.CommandText = """
            insert into ares_admin_users(user_id,organization_id,email,display_name,role,enabled,updated_at)
            values(@user,@organization,@email,@display,'Owner',true,now())
            on conflict(user_id) do nothing
            """;
        command.Parameters.AddWithValue("user", userId); command.Parameters.AddWithValue("organization", organizationId);
        command.Parameters.AddWithValue("email", email); command.Parameters.AddWithValue("display", displayName);
        if (await command.ExecuteNonQueryAsync() != 1)
        {
            await transaction.RollbackAsync();
            throw new InvalidOperationException("La cuenta ya pertenece a una organización ARES.");
        }
        await transaction.CommitAsync(); return organizationId;
    }

    public async Task<OrganizationSetupInfo?> GetOrganizationSetupAsync(Guid organizationId)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select organization_id,name,slug,onboarding_completed from ares_organizations where organization_id=@id and enabled=true";
        command.Parameters.AddWithValue("id", organizationId);
        await using (var reader = await command.ExecuteReaderAsync())
            if (await reader.ReadAsync()) return new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3));

        string slug = $"org-{organizationId.ToString("N")[..8]}";
        await using var repair = connection.CreateCommand();
        repair.CommandText = """
            insert into ares_organizations(organization_id,name,slug,enabled,onboarding_completed)
            values(@id,'Organización ARES',@slug,true,false)
            on conflict(organization_id) do update set enabled=true
            returning organization_id,name,slug,onboarding_completed
            """;
        repair.Parameters.AddWithValue("id", organizationId); repair.Parameters.AddWithValue("slug", slug);
        await using var repaired = await repair.ExecuteReaderAsync();
        return await repaired.ReadAsync() ? new(repaired.GetGuid(0), repaired.GetString(1), repaired.GetString(2), repaired.GetBoolean(3)) : null;
    }

    public async Task CompleteOrganizationSetupAsync(Guid organizationId)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "update ares_organizations set onboarding_completed=true,updated_at=now() where organization_id=@id";
        command.Parameters.AddWithValue("id", organizationId); await command.ExecuteNonQueryAsync();
    }

    public async Task<List<Guid>> GetOrganizationIdsAsync()
    {
        if (!UsesDatabase) return [DefaultOrganizationId];
        var result = new List<Guid>();
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "select organization_id from ares_organizations where enabled=true";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(reader.GetGuid(0));
        return result;
    }

    public async Task<AdminUser?> GetAdminAsync(Guid userId)
    {
        if (!UsesDatabase) return null;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select user_id, organization_id, coalesce(email, ''), display_name, role, enabled from ares_admin_users where user_id = @userId";
        command.Parameters.AddWithValue("userId", userId);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new AdminUser(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetBoolean(5))
            : null;
    }

    public async Task UpdateAdminEmailAsync(Guid userId, string email)
    {
        if (!UsesDatabase || string.IsNullOrWhiteSpace(email)) return;
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "update ares_admin_users set email=@email where user_id=@id and (email is null or email <> @email)";
        command.Parameters.AddWithValue("id", userId); command.Parameters.AddWithValue("email", email); await command.ExecuteNonQueryAsync();
    }

    public async Task RegisterPendingAsync(Guid userId, Guid organizationId, string invitedRole, string email, string displayName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            insert into ares_registration_requests (user_id, organization_id, invited_role, email, display_name, status, requested_at)
            values (@id, @organization, @role, @email, @name, 'Pending', now())
            on conflict (user_id) do update set email=excluded.email, display_name=excluded.display_name,
                organization_id=excluded.organization_id, invited_role=excluded.invited_role,
                status='Pending', requested_at=now(), reviewed_at=null, reviewed_by=null
            """;
        command.Parameters.AddWithValue("id", userId); command.Parameters.AddWithValue("organization", organizationId); command.Parameters.AddWithValue("role", invitedRole);
        command.Parameters.AddWithValue("email", email); command.Parameters.AddWithValue("name", displayName);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<List<RegistrationRequestInfo>> GetRegistrationsAsync(Guid organizationId)
    {
        var result = new List<RegistrationRequestInfo>();
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select user_id,email,display_name,status,requested_at,reviewed_at from ares_registration_requests where organization_id=@organization order by requested_at desc";
        command.Parameters.AddWithValue("organization", organizationId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4), reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5)));
        return result;
    }

    public async Task<List<AdminUser>> GetAdminsAsync(Guid organizationId)
    {
        var result = new List<AdminUser>();
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "select user_id,organization_id,coalesce(email,''),display_name,role,enabled from ares_admin_users where organization_id=@organization order by display_name";
        command.Parameters.AddWithValue("organization", organizationId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetBoolean(5)));
        return result;
    }

    public async Task<bool> ApproveAsync(Guid userId, Guid organizationId, string role, Guid reviewer)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(); await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            insert into ares_admin_users(user_id,organization_id,email,display_name,role,enabled,updated_at)
            select user_id,organization_id,email,display_name,@role,true,now() from ares_registration_requests
            where user_id=@id and organization_id=@organization and status='Pending'
            on conflict(user_id) do update set organization_id=excluded.organization_id,email=excluded.email,display_name=excluded.display_name,role=excluded.role,enabled=true,updated_at=now()
            """;
        command.Parameters.AddWithValue("id", userId); command.Parameters.AddWithValue("organization", organizationId); command.Parameters.AddWithValue("role", role);
        int changed = await command.ExecuteNonQueryAsync();
        command.Parameters.Clear(); command.CommandText = "update ares_registration_requests set status='Approved',reviewed_at=now(),reviewed_by=@reviewer where user_id=@id and organization_id=@organization";
        command.Parameters.AddWithValue("id", userId); command.Parameters.AddWithValue("organization", organizationId); command.Parameters.AddWithValue("reviewer", reviewer); await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync(); return changed > 0;
    }

    public async Task ReviewRegistrationAsync(Guid userId, Guid organizationId, string status, Guid reviewer)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "update ares_registration_requests set status=@status,reviewed_at=now(),reviewed_by=@reviewer where user_id=@id and organization_id=@organization";
        command.Parameters.AddWithValue("id", userId); command.Parameters.AddWithValue("organization", organizationId); command.Parameters.AddWithValue("status", status); command.Parameters.AddWithValue("reviewer", reviewer); await command.ExecuteNonQueryAsync();
    }

    public async Task<bool> UpdateAdminAsync(Guid userId, Guid organizationId, string role, bool enabled)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "update ares_admin_users set role=@role,enabled=@enabled,updated_at=now() where user_id=@id and organization_id=@organization and role <> 'Owner'";
        command.Parameters.AddWithValue("id", userId); command.Parameters.AddWithValue("organization", organizationId); command.Parameters.AddWithValue("role", role); command.Parameters.AddWithValue("enabled", enabled); return await command.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> RemoveAdminAsync(Guid userId, Guid organizationId)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "delete from ares_admin_users where user_id=@id and organization_id=@organization and role <> 'Owner'";
        command.Parameters.AddWithValue("id", userId); command.Parameters.AddWithValue("organization", organizationId); return await command.ExecuteNonQueryAsync() > 0;
    }

    public async Task<InvitationInfo> CreateInvitationAsync(Guid organizationId, string invitedRole, byte[] codeHash, string prefix, int maxUses, DateTimeOffset expiresAt, Guid createdBy)
    {
        Guid id = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = """
            insert into ares_invitation_codes(invitation_id,organization_id,invited_role,code_hash,code_prefix,max_uses,expires_at,created_by)
            values(@id,@organization,@role,@hash,@prefix,@max,@expires,@creator)
            """;
        command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("organization", organizationId); command.Parameters.AddWithValue("role", invitedRole);
        command.Parameters.AddWithValue("hash", codeHash); command.Parameters.AddWithValue("prefix", prefix);
        command.Parameters.AddWithValue("max", maxUses); command.Parameters.AddWithValue("expires", expiresAt); command.Parameters.AddWithValue("creator", createdBy);
        await command.ExecuteNonQueryAsync(); return new(id, prefix, maxUses, 0, expiresAt, false, DateTimeOffset.UtcNow);
    }

    public async Task<List<InvitationInfo>> GetInvitationsAsync(Guid organizationId)
    {
        var result = new List<InvitationInfo>(); await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "select invitation_id,code_prefix,max_uses,used_count,expires_at,revoked,created_at from ares_invitation_codes where organization_id=@organization order by created_at desc limit 200";
        command.Parameters.AddWithValue("organization", organizationId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetFieldValue<DateTimeOffset>(4), reader.GetBoolean(5), reader.GetFieldValue<DateTimeOffset>(6)));
        return result;
    }

    public async Task<InvitationGrant?> ConsumeInvitationAsync(byte[] codeHash)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = """
            update ares_invitation_codes set used_count=used_count+1
            where code_hash=@hash and not revoked and expires_at > now() and used_count < max_uses
            returning invitation_id, organization_id, invited_role
            """;
        command.Parameters.AddWithValue("hash", codeHash);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? new InvitationGrant(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2)) : null;
    }

    public async Task RestoreInvitationUseAsync(Guid invitationId)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "update ares_invitation_codes set used_count=greatest(used_count-1,0) where invitation_id=@id";
        command.Parameters.AddWithValue("id", invitationId); await command.ExecuteNonQueryAsync();
    }

    public async Task RevokeInvitationAsync(Guid invitationId, Guid organizationId)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "update ares_invitation_codes set revoked=true where invitation_id=@id and organization_id=@organization";
        command.Parameters.AddWithValue("id", invitationId); command.Parameters.AddWithValue("organization", organizationId); await command.ExecuteNonQueryAsync();
    }

    public async Task<DeviceEnrollmentInfo> CreateDeviceEnrollmentAsync(Guid organizationId, byte[] codeHash, string prefix,
        string assignedGroup, int maxUses, DateTimeOffset expiresAt, Guid createdBy)
    {
        Guid id = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            insert into ares_device_enrollment_codes
            (enrollment_id,organization_id,code_hash,code_prefix,assigned_group,max_uses,expires_at,created_by)
            values(@id,@organization,@hash,@prefix,@group,@max,@expires,@creator)
            """;
        command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("organization", organizationId);
        command.Parameters.AddWithValue("hash", codeHash); command.Parameters.AddWithValue("prefix", prefix);
        command.Parameters.AddWithValue("group", assignedGroup); command.Parameters.AddWithValue("max", maxUses);
        command.Parameters.AddWithValue("expires", expiresAt); command.Parameters.AddWithValue("creator", createdBy);
        await command.ExecuteNonQueryAsync();
        return new(id, prefix, assignedGroup, maxUses, 0, expiresAt, false, DateTimeOffset.UtcNow);
    }

    public async Task<List<DeviceEnrollmentInfo>> GetDeviceEnrollmentsAsync(Guid organizationId)
    {
        var result = new List<DeviceEnrollmentInfo>();
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select enrollment_id,code_prefix,assigned_group,max_uses,used_count,expires_at,revoked,created_at from ares_device_enrollment_codes where organization_id=@organization order by created_at desc limit 200";
        command.Parameters.AddWithValue("organization", organizationId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetFieldValue<DateTimeOffset>(5), reader.GetBoolean(6), reader.GetFieldValue<DateTimeOffset>(7)));
        return result;
    }

    public async Task RevokeDeviceEnrollmentAsync(Guid enrollmentId, Guid organizationId)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "update ares_device_enrollment_codes set revoked=true where enrollment_id=@id and organization_id=@organization";
        command.Parameters.AddWithValue("id", enrollmentId); command.Parameters.AddWithValue("organization", organizationId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<DeviceEnrollmentGrant?> EnrollDeviceAsync(byte[] codeHash, string deviceId, string machineName, byte[] credentialHash)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            update ares_device_enrollment_codes set used_count=used_count+1
            where code_hash=@hash and not revoked and expires_at>now() and used_count<max_uses
            returning enrollment_id,organization_id,assigned_group
            """;
        command.Parameters.AddWithValue("hash", codeHash);
        Guid enrollmentId; Guid organizationId; string group;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync()) { await transaction.RollbackAsync(); return null; }
            enrollmentId = reader.GetGuid(0); organizationId = reader.GetGuid(1); group = reader.GetString(2);
        }
        command.Parameters.Clear();
        command.CommandText = """
            insert into ares_devices(organization_id,device_id,machine_name,assigned_group,credential_hash,enabled,enrollment_id,enrolled_at,last_seen_at)
            values(@organization,@device,@machine,@group,@credential,true,@enrollment,now(),now())
            on conflict(organization_id,device_id) do update set machine_name=excluded.machine_name,
                assigned_group=excluded.assigned_group,credential_hash=excluded.credential_hash,
                previous_credential_hash=null,rotation_requested=false,enabled=true,
                enrollment_id=excluded.enrollment_id,enrolled_at=now(),last_seen_at=now()
            """;
        command.Parameters.AddWithValue("organization", organizationId); command.Parameters.AddWithValue("device", deviceId);
        command.Parameters.AddWithValue("machine", machineName); command.Parameters.AddWithValue("credential", credentialHash);
        command.Parameters.AddWithValue("group", group);
        command.Parameters.AddWithValue("enrollment", enrollmentId); await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync(); return new(enrollmentId, organizationId, group);
    }

    public async Task<DeviceIdentity?> ValidateDeviceAsync(byte[] credentialHash)
    {
        if (!UsesDatabase) return null;
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select organization_id,device_id,assigned_group from ares_devices where enabled=true and (credential_hash=@hash or (rotation_requested and previous_credential_hash=@hash))";
        command.Parameters.AddWithValue("hash", credentialHash);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? new DeviceIdentity(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)) : null;
    }

    public async Task CancelDeviceEnrollmentAsync(Guid organizationId, string deviceId, byte[] credentialHash)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        Guid? enrollmentId = null;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "delete from ares_devices where organization_id=@organization and device_id=@device and credential_hash=@hash returning enrollment_id";
            command.Parameters.AddWithValue("organization", organizationId); command.Parameters.AddWithValue("device", deviceId);
            command.Parameters.AddWithValue("hash", credentialHash);
            object? result = await command.ExecuteScalarAsync();
            if (result is Guid id) enrollmentId = id;
        }
        if (enrollmentId.HasValue)
        {
            await using var restore = connection.CreateCommand(); restore.Transaction = transaction;
            restore.CommandText = "update ares_device_enrollment_codes set used_count=greatest(used_count-1,0) where enrollment_id=@id";
            restore.Parameters.AddWithValue("id", enrollmentId.Value); await restore.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    public async Task<bool> RequestDeviceRotationAsync(Guid organizationId, string deviceId)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "update ares_devices set rotation_requested=true where organization_id=@organization and device_id=@device and enabled=true";
        command.Parameters.AddWithValue("organization", organizationId); command.Parameters.AddWithValue("device", deviceId);
        return await command.ExecuteNonQueryAsync() == 1;
    }

    public async Task<bool> RotateDeviceCredentialAsync(Guid organizationId, string deviceId, byte[] currentHash, byte[] newHash)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            update ares_devices set previous_credential_hash=coalesce(previous_credential_hash,credential_hash),
                credential_hash=@new_hash,last_seen_at=now()
            where organization_id=@organization and device_id=@device
                and (credential_hash=@current_hash or previous_credential_hash=@current_hash)
                and enabled=true and rotation_requested=true
            """;
        command.Parameters.AddWithValue("organization", organizationId); command.Parameters.AddWithValue("device", deviceId);
        command.Parameters.AddWithValue("current_hash", currentHash); command.Parameters.AddWithValue("new_hash", newHash);
        return await command.ExecuteNonQueryAsync() == 1;
    }

    public async Task CompleteDeviceRotationAsync(byte[] credentialHash)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "update ares_devices set rotation_requested=false,previous_credential_hash=null where credential_hash=@hash and previous_credential_hash is not null and rotation_requested=true";
        command.Parameters.AddWithValue("hash", credentialHash); await command.ExecuteNonQueryAsync();
    }

    public async Task<bool> RevokeDeviceAsync(Guid organizationId, string deviceId)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "update ares_devices set enabled=false,rotation_requested=false,previous_credential_hash=null where organization_id=@organization and device_id=@device and enabled=true";
        command.Parameters.AddWithValue("organization", organizationId); command.Parameters.AddWithValue("device", deviceId);
        return await command.ExecuteNonQueryAsync() == 1;
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

internal sealed record AdminUser(Guid UserId, Guid OrganizationId, string Email, string DisplayName, string Role, bool Enabled);
internal sealed record OrganizationSetupInfo(Guid OrganizationId, string Name, string Slug, bool OnboardingCompleted);
internal sealed record InvitationGrant(Guid InvitationId, Guid OrganizationId, string InvitedRole);
internal sealed record DeviceEnrollmentInfo(Guid EnrollmentId, string CodePrefix, string AssignedGroup, int MaxUses, int UsedCount, DateTimeOffset ExpiresAt, bool Revoked, DateTimeOffset CreatedAt);
internal sealed record DeviceEnrollmentGrant(Guid EnrollmentId, Guid OrganizationId, string AssignedGroup);
internal sealed record DeviceIdentity(Guid OrganizationId, string DeviceId, string AssignedGroup);
internal sealed record RegistrationRequestInfo(Guid UserId, string Email, string DisplayName, string Status, DateTimeOffset RequestedAt, DateTimeOffset? ReviewedAt);
internal sealed record InvitationInfo(Guid InvitationId, string CodePrefix, int MaxUses, int UsedCount, DateTimeOffset ExpiresAt, bool Revoked, DateTimeOffset CreatedAt);
