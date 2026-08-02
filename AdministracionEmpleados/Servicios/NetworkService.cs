using System.Net.Sockets;

namespace AdministracionEmpleados.Servicios
{
    public class NetworkService
    {
        public string Conectar()
        {
            try
            {
                using TcpClient cliente = new TcpClient();

                Task tareaConexion = cliente.ConnectAsync("127.0.0.1", 5000);
                if (!tareaConexion.Wait(TimeSpan.FromSeconds(2)))
                    return "ERROR: Tiempo de conexión agotado.";

                using NetworkStream flujo = cliente.GetStream();
                using StreamReader lector = new StreamReader(flujo);
                using StreamWriter escritor = new StreamWriter(flujo);

                escritor.AutoFlush = true;

                escritor.WriteLine("Hola Agent");

                string? respuesta = lector.ReadLine();

                return respuesta ?? "El Agent no envió una respuesta.";
            }
            catch (Exception ex)
            {
                return $"ERROR: {ex.Message}";
            }
        }
    }
}
