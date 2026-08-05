namespace ARES.Shared.Modelos
{
    public class Empleado
    {
        public string Nombre { get; set; } = "";
        public string Grupo { get; set; } = "General";

        public Computadora Computadora { get; set; } =
            new Computadora();
    }
}
