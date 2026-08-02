Console.Title = "ARES Agent";

Console.WriteLine("==================================");
Console.WriteLine("        ARES AGENT v0.1");
Console.WriteLine("==================================");
Console.WriteLine();

Console.WriteLine($"Equipo: {Environment.MachineName}");
Console.WriteLine($"Usuario: {Environment.UserName}");
Console.WriteLine($"Sistema: {Environment.OSVersion}");

Console.WriteLine();
Console.WriteLine("Esperando instrucciones...");
Console.WriteLine();

while (true)
{
    Thread.Sleep(1000);
}