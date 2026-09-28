var builder = Host.CreateApplicationBuilder(args);

// Durable API job execution is implemented in P2 of HELIOS-IMPLEMENTATION-PLAN.md.
// Do not register a no-op processor that could be mistaken for a working queue.
var host = builder.Build();
host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Helios.Worker")
    .LogWarning("HELIOS worker has no job processor configured. Platform implementation is pending.");
host.Run();
