using Microsoft.Win32;
using QRCoder;
using System.Drawing.Imaging;

namespace ARES.Agent;

internal static class LockscreenBranding
{
    public static void GenerarYAplicar(AgentSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.RequestToken)) return;
        string plantilla = Path.Combine(AppContext.BaseDirectory, "Assets", "lockscreen-template-v3.png");
        if (!File.Exists(plantilla)) return;

        string carpeta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ARES");
        Directory.CreateDirectory(carpeta);
        string salida = Path.Combine(carpeta, "lockscreen.png");
        string url = $"{settings.ServerUrl.TrimEnd('/')}/solicitar/{settings.RequestToken}";
        string codigo = settings.RequestToken[..Math.Min(8, settings.RequestToken.Length)].ToUpperInvariant();

        using var fondo = new Bitmap(plantilla);
        byte[] qrBytes = PngByteQRCodeHelper.GetQRCode(url, QRCodeGenerator.ECCLevel.Q, 12);
        using var qrStream = new MemoryStream(qrBytes);
        using var qr = new Bitmap(qrStream);
        using Graphics g = Graphics.FromImage(fondo);
        g.DrawImage(qr, new Rectangle(148, 535, 260, 260));
        using var titulo = new Font("Segoe UI", 18, FontStyle.Bold);
        using var texto = new Font("Segoe UI", 13, FontStyle.Regular);
        using var pincel = new SolidBrush(Color.White);
        using var pincelSuave = new SolidBrush(Color.FromArgb(203, 213, 225));
        g.DrawString("SOLICITAR DESBLOQUEO", titulo, pincel, 115, 810);
        g.DrawString($"Equipo: {Environment.MachineName}", texto, pincelSuave, 115, 850);
        g.DrawString($"Código: {codigo}", texto, pincelSuave, 115, 880);
        fondo.Save(salida, ImageFormat.Png);

        using RegistryKey personalizacion = Registry.LocalMachine.CreateSubKey(
            @"SOFTWARE\Policies\Microsoft\Windows\Personalization", true);
        personalizacion.SetValue("LockScreenImage", salida, RegistryValueKind.String);
        personalizacion.SetValue("NoChangingLockScreen", 1, RegistryValueKind.DWord);

    }

    public static void ConfigurarAviso(AgentSettings settings, bool bloqueado)
    {
        using RegistryKey inicio = Registry.LocalMachine.CreateSubKey(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", true);
        if (!bloqueado)
        {
            inicio.DeleteValue("LegalNoticeCaption", false);
            inicio.DeleteValue("LegalNoticeText", false);
            return;
        }
        string codigo = settings.RequestToken[..Math.Min(8, settings.RequestToken.Length)].ToUpperInvariant();
        inicio.SetValue("LegalNoticeCaption", "Equipo bloqueado por ARES", RegistryValueKind.String);
        inicio.SetValue("LegalNoticeText",
            $"Solicitá el desbloqueo desde otro dispositivo:\r\n{settings.ServerUrl.TrimEnd('/')}/solicitar/{settings.RequestToken}\r\nCódigo: {codigo}",
            RegistryValueKind.String);
    }
}
