namespace ARES.Shared.Modelos
{
    public class Empleado
    {
        public string Nombre { get; set; } = "";

        public Computadora Computadora { get; set; } =
            new Computadora();
    }
}