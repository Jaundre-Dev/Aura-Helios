using Helios.Worker.Agents;

var builder = Host.CreateApplicationBuilder(args);

// TODO Phase 0: builder.Services.AddHeliosInfrastructure(builder.Configuration);
// TODO Phase 0: builder.Services.AddHeliosApplication();

builder.Services.AddHostedService<AgentRunWorker>();

// TODO Phase 2+: WorkflowWorker, IngestionWorker, EvaluationWorker, MaintenanceWorker.

var host = builder.Build();
host.Run();
