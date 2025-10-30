using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.StrataConfig_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.StrataConfig_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
