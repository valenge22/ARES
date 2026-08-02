using ARES.Agent;

if (args.Contains("--service", StringComparer.OrdinalIgnoreCase))
{
    await new SessionRestrictionService().RunAsync();
}
else
{
    ApplicationConfiguration.Initialize();
    Application.Run(new AgentApplicationContext());
}
