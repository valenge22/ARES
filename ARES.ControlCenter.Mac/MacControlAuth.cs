using ARES.Shared.Servicios;
using System.Diagnostics;

namespace ARES.ControlCenter.Mac;

internal static class MacControlAuth
{
    public static ControlAuthClient Client { get; } = new(
        () => MacSettings.Load().ServerUrl,
        new MacKeychainRefreshTokenStore());
}

internal sealed class MacKeychainRefreshTokenStore : IRefreshTokenStore
{
    private const string Service = "com.ares.controlcenter";
    private static string Account => Environment.UserName;

    public string? Load()
    {
        try { return Run("find-generic-password", "-s", Service, "-a", Account, "-w").Trim() is { Length: > 0 } value ? value : null; }
        catch { return null; }
    }

    public void Save(string token)
    {
        Run("add-generic-password", "-U", "-s", Service, "-a", Account, "-w", token);
    }

    public void Delete()
    {
        try { Run("delete-generic-password", "-s", Service, "-a", Account); } catch { }
    }

    private static string Run(params string[] arguments)
    {
        var info = new ProcessStartInfo { FileName = "/usr/bin/security", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using Process process = Process.Start(info) ?? throw new InvalidOperationException("No se pudo abrir Keychain.");
        string output = process.StandardOutput.ReadToEnd(); process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(process.StandardError.ReadToEnd());
        return output;
    }
}
