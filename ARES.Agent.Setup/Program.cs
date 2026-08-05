using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace ARES.Agent.Setup;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string package = GetArgument(args, "--package") ?? Path.Combine(AppContext.BaseDirectory, "package");
        using var form = new SetupForm(package);
        Application.Run(form);
        Environment.ExitCode = form.Installed ? 0 : 2;
    }

    private static string? GetArgument(string[] args, string name)
    {
        int index = Array.FindIndex(args, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}

internal sealed class SetupForm : Form
{
    private const string ServerUrl = "https://ares-3bic.onrender.com";
    private readonly string packageDirectory;
    private readonly TextBox linkCode = Field();
    private readonly TextBox employee = Field();
    private readonly TextBox employeePassword = Field(true);
    private readonly TextBox employeeConfirmation = Field(true);
    private readonly CheckBox useExisting = new() { Text = "Usar una cuenta estándar que ya existe (no crear otra)", AutoSize = true, ForeColor = Color.FromArgb(30, 64, 175) };
    private readonly TextBox adminPassword = Field(true);
    private readonly TextBox adminConfirmation = Field(true);
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.FromArgb(71, 85, 105) };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Top, Height = 8, Style = ProgressBarStyle.Marquee, Visible = false };
    private readonly Button install = new() { Text = "Instalar ARES Agent", Width = 180, Height = 42, BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    private readonly Button cancel = new() { Text = "Cancelar", Width = 100, Height = 42, DialogResult = DialogResult.Cancel };
    private bool busy;
    public bool Installed { get; private set; }

    public SetupForm(string packageDirectory)
    {
        this.packageDirectory = Path.GetFullPath(packageDirectory);
        Text = "ARES Agent — Configuración";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(620, 650);
        BackColor = Color.FromArgb(248, 250, 252);
        Font = new Font("Segoe UI", 9.5F);
        CancelButton = cancel;

        var header = new Panel { Dock = DockStyle.Top, Height = 102, BackColor = Color.FromArgb(15, 23, 42) };
        header.Controls.Add(new Label { Text = "ARES", ForeColor = Color.White, Font = new Font("Segoe UI", 25F, FontStyle.Bold), AutoSize = true, Location = new Point(30, 18) });
        header.Controls.Add(new Label { Text = "Configuración segura del equipo del empleado", ForeColor = Color.FromArgb(186, 230, 253), Font = new Font("Segoe UI", 10F), AutoSize = true, Location = new Point(33, 65) });

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(32, 22, 32, 18), ColumnCount = 2, RowCount = 9 };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        for (int i = 1; i < 7; i++) body.RowStyles.Add(new RowStyle(SizeType.Absolute, 67));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddFull(body, 0, "Cuenta administradora detectada", new TextBox { Text = Environment.UserName, ReadOnly = true, Dock = DockStyle.Top, BackColor = Color.FromArgb(226, 232, 240) });
        AddFull(body, 1, "Código temporal de vinculación ARES", linkCode);
        AddFull(body, 2, "Nombre de la cuenta del empleado", employee);
        AddPair(body, 3, "Contraseña inicial del empleado", employeePassword, "Confirmar contraseña", employeeConfirmation);
        AddPair(body, 4, "Nueva contraseña privada del administrador", adminPassword, "Confirmar contraseña", adminConfirmation);

        var accountOptions = new Panel { Dock = DockStyle.Fill };
        useExisting.Location = new Point(0, 2);
        var explanation = new Label {
            Text = "La cuenta actual conservará sus permisos administrativos. La nueva cuenta del empleado será estándar y será la única administrada por ARES.",
            AutoSize = false, ForeColor = Color.FromArgb(71, 85, 105), Location = new Point(0, 27), Size = new Size(535, 38)
        };
        accountOptions.Controls.Add(useExisting); accountOptions.Controls.Add(explanation);
        body.Controls.Add(accountOptions, 0, 5); body.SetColumnSpan(accountOptions, 2);
        body.Controls.Add(status, 0, 6); body.SetColumnSpan(status, 2);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        install.FlatAppearance.BorderSize = 0;
        actions.Controls.Add(install); actions.Controls.Add(cancel);
        body.Controls.Add(actions, 0, 7); body.SetColumnSpan(actions, 2);

        Controls.Add(body); Controls.Add(progress); Controls.Add(header);
        install.Click += async (_, _) => await InstallAsync();
        useExisting.CheckedChanged += (_, _) =>
        {
            employeePassword.Enabled = employeeConfirmation.Enabled = !useExisting.Checked;
            if (useExisting.Checked) { employeePassword.Clear(); employeeConfirmation.Clear(); }
            explanation.Text = useExisting.Checked
                ? "ARES conservará la contraseña y el perfil de esa cuenta. La instalación se cancelará si el usuario no existe o tiene permisos administrativos."
                : "La cuenta actual conservará sus permisos administrativos. ARES creará una cuenta estándar nueva para el empleado.";
        };
        FormClosing += (_, e) => { if (busy && !Installed) e.Cancel = true; };
    }

    private async Task InstallAsync()
    {
        EnrollmentResponse? enrollment = null;
        string? validation = ValidateInput();
        if (validation is not null) { ShowError(validation); return; }
        string script = Path.Combine(packageDirectory, "Instalar-ARES-Agent.ps1");
        if (!File.Exists(script) || !File.Exists(Path.Combine(packageDirectory, "app", "ARES.Agent.exe")))
        { ShowError("El instalador está incompleto. Volvé a descargar ARES-Agent-Setup.exe."); return; }

        SetBusy(true, "Creando las cuentas y configurando ARES Agent…");
        try
        {
            var start = new ProcessStartInfo("powershell.exe") {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = packageDirectory
            };
            start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-ExecutionPolicy"); start.ArgumentList.Add("Bypass");
            start.ArgumentList.Add("-File"); start.ArgumentList.Add(script);
            start.ArgumentList.Add("-ServerUrl"); start.ArgumentList.Add(ServerUrl);
            start.ArgumentList.Add("-ManagedUser"); start.ArgumentList.Add(employee.Text.Trim());
            start.ArgumentList.Add("-InstallerAdminUser"); start.ArgumentList.Add(Environment.UserName);
            start.ArgumentList.Add("-ProvisionStandardUser"); start.ArgumentList.Add("-NonInteractiveProvisioning");
            start.ArgumentList.Add("-LogPath"); start.ArgumentList.Add(Path.Combine(Path.GetTempPath(), "ARES-Agent-Install.log"));
            if (!useExisting.Checked)
                start.Environment["ARES_SETUP_EMPLOYEE_PASSWORD"] = employeePassword.Text;
            start.Environment["ARES_SETUP_ADMIN_PASSWORD"] = adminPassword.Text;
            enrollment = await EnrollAsync();
            start.Environment["ARES_SETUP_DEVICE_CREDENTIAL"] = enrollment.Credential;

            using Process process = Process.Start(start) ?? throw new InvalidOperationException("No se pudo iniciar el configurador.");
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            string error = await errorTask; _ = await outputTask;
            ClearPasswords();
            if (process.ExitCode != 0)
            {
                string logPath = Path.Combine(Path.GetTempPath(), "ARES-Agent-Install.log");
                string detail = File.Exists(logPath) ? File.ReadAllText(logPath) : error;
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail) ? "La instalación no pudo completarse." : detail);
            }
            MessageBox.Show("ARES Agent se instaló correctamente. Cerrá la sesión administradora e ingresá con la nueva cuenta del empleado.", "ARES", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Installed = true;
            SetBusy(false, "Instalación completada correctamente.");
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            if (enrollment is not null) await CancelEnrollmentAsync(enrollment.Credential);
            ShowError(ex.Message); SetBusy(false, "La instalación no pudo completarse.");
        }
    }

    private string? ValidateInput()
    {
        if (!linkCode.Text.Trim().StartsWith("ARES-PC-", StringComparison.OrdinalIgnoreCase)) return "Ingresá un código de vinculación ARES válido.";
        string name = employee.Text.Trim();
        if (name.Length is < 1 or > 20 || name.IndexOfAny("\\/[]:;|=,+*?<>@\"".ToCharArray()) >= 0 || name.EndsWith('.')) return "El nombre del empleado no es válido o supera 20 caracteres.";
        if (name.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase)) return "La cuenta del empleado debe ser diferente de la administradora.";
        if (!useExisting.Checked && employeePassword.Text.Length < 8) return "La contraseña del empleado debe tener al menos 8 caracteres.";
        if (!useExisting.Checked && employeePassword.Text != employeeConfirmation.Text) return "Las contraseñas del empleado no coinciden.";
        if (adminPassword.Text.Length < 10) return "La contraseña administrativa debe tener al menos 10 caracteres.";
        if (adminPassword.Text != adminConfirmation.Text) return "Las contraseñas administrativas no coinciden.";
        if (!useExisting.Checked && adminPassword.Text == employeePassword.Text) return "La contraseña administrativa debe ser diferente de la del empleado.";
        return null;
    }

    private void SetBusy(bool busy, string message)
    {
        this.busy = busy;
        install.Enabled = !busy; cancel.Enabled = !busy; progress.Visible = busy; status.Text = message;
        UseWaitCursor = busy;
    }

    private async Task<EnrollmentResponse> EnrollAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using HttpResponseMessage response = await http.PostAsJsonAsync($"{ServerUrl}/api/agents/enroll", new
        {
            code = linkCode.Text.Trim(), deviceId = DeviceId(), machineName = Environment.MachineName
        });
        if (!response.IsSuccessStatusCode)
        {
            string detail = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"No se pudo vincular el equipo. {detail}");
        }
        return await response.Content.ReadFromJsonAsync<EnrollmentResponse>() ?? throw new InvalidDataException("El servidor no devolvió la credencial del equipo.");
    }

    private static async Task CancelEnrollmentAsync(string credential)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.Add("X-ARES-Device", credential);
            using HttpResponseMessage _ = await http.PostAsync($"{ServerUrl}/api/agents/enroll/cancel", null);
        }
        catch { /* No ocultar el error original si también falla la recuperación. */ }
    }

    private static string DeviceId()
    {
        string machineGuid = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", "")?.ToString() ?? "";
        string source = string.IsNullOrWhiteSpace(machineGuid) ? Environment.MachineName : machineGuid;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..24];
    }
    private void ShowError(string message) => MessageBox.Show(message, "ARES", MessageBoxButtons.OK, MessageBoxIcon.Error);
    private void ClearPasswords() { employeePassword.Clear(); employeeConfirmation.Clear(); adminPassword.Clear(); adminConfirmation.Clear(); }
    private static TextBox Field(bool password = false) => new() { Dock = DockStyle.Top, UseSystemPasswordChar = password, MaxLength = 128 };
    private static void AddFull(TableLayoutPanel panel, int row, string title, Control field)
    {
        var box = Box(title, field); panel.Controls.Add(box, 0, row); panel.SetColumnSpan(box, 2);
    }
    private static void AddPair(TableLayoutPanel panel, int row, string leftTitle, Control left, string rightTitle, Control right)
    { panel.Controls.Add(Box(leftTitle, left), 0, row); panel.Controls.Add(Box(rightTitle, right), 1, row); }
    private static Control Box(string title, Control field)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 10, 5) };
        field.Dock = DockStyle.Bottom;
        field.Height = 30;
        panel.Controls.Add(field);
        panel.Controls.Add(new Label { Text = title, Dock = DockStyle.Top, Height = 24, ForeColor = Color.FromArgb(30, 41, 59) });
        return panel;
    }
}

internal sealed class EnrollmentResponse
{
    public string Credential { get; set; } = "";
    public Guid OrganizationId { get; set; }
    public string Group { get; set; } = "Grupo 1";
}
