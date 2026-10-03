using Helios.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHeliosWorker(builder.Configuration);

builder.Build().Run();
