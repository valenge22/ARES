using ARES.Shared.Servicios;
using System.Security.Cryptography;
using System.Text;

namespace AdministracionEmpleados;

internal static class AresControlAuth
{
    public static ControlAuthClient Client { get; } = new(
        () => AresSettings.Cargar().ServerUrl,
        new WindowsRefreshTokenStore());
}

internal sealed class WindowsRefreshTokenStore : IRefreshTokenStore
{
    private static string TokenPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ARES", "control.session");

    public string? Load()
    {
        try
        {
            if (!File.Exists(TokenPath)) return null;
            byte[] protectedToken = File.ReadAllBytes(TokenPath);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedToken, null, DataProtectionScope.CurrentUser));
        }
        catch { Delete(); return null; }
    }

    public void Save(string token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!);
        byte[] protectedToken = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(TokenPath, protectedToken);
    }

    public void Delete()
    {
        try { if (File.Exists(TokenPath)) File.Delete(TokenPath); } catch { }
    }
}
