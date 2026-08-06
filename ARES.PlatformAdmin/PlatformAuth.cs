using ARES.Shared.Servicios;
using System.Security.Cryptography;
using System.Text;

namespace ARES.PlatformAdmin;

internal static class PlatformAuth
{
    public const string ServerUrl = "https://ares-3bic.onrender.com";
    public static ControlAuthClient Client { get; } = new(() => ServerUrl, new PlatformTokenStore());
}

internal sealed class PlatformTokenStore : IRefreshTokenStore
{
    private static string TokenPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ARES", "platform-admin.session");
    public string? Load() { try { return File.Exists(TokenPath) ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(TokenPath), null, DataProtectionScope.CurrentUser)) : null; } catch { Delete(); return null; } }
    public void Save(string token) { Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!); File.WriteAllBytes(TokenPath, ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser)); }
    public void Delete() { try { if (File.Exists(TokenPath)) File.Delete(TokenPath); } catch { } }
}
