namespace AdministracionEmpleados
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            bool restored = false;
            try { restored = AresControlAuth.Client.RestoreAsync().GetAwaiter().GetResult(); } catch { }
            if (!restored)
            {
                using var login = new LoginForm();
                if (login.ShowDialog() != DialogResult.OK) return;
            }
            Application.Run(new MainForm());
        }
    }
}
