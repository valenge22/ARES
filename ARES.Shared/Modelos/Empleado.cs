namespace ARES.Shared.Modelos
{
    public class Empleado
    {
        public string Nombre { get; set; } = "";
        public string Grupo { get; set; } = "Grupo 1";

        public Computadora Computadora { get; set; } =
            new Computadora();
    }
}
