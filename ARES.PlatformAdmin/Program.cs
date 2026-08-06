namespace ARES.PlatformAdmin;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        bool restored = false;
        try { restored = PlatformAuth.Client.RestoreAsync().GetAwaiter().GetResult(); } catch { }
        if (!restored)
        {
            using var login = new LoginForm();
            if (login.ShowDialog() != DialogResult.OK) return;
        }
        Application.Run(new MainForm());
    }
}
