using KubeOps.Abstractions.Builder;
using KubeOps.Operator;
using McOperator.Controllers;
using McOperator.Entities;
using McOperator.Finalizers;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.Services
    .AddKubernetesOperator(settings =>
    {
        settings.Name = OperatorConstants.OperatorName;
        settings.LeaderElectionType = LeaderElectionType.Single;
    })
    .AddController<MinecraftServerController, MinecraftServer>()
    .AddFinalizer<MinecraftServerFinalizer, MinecraftServer>(OperatorConstants.ServerFinalizer)
    .AddController<MinecraftServerClusterController, MinecraftServerCluster>()
    .AddFinalizer<MinecraftServerClusterFinalizer, MinecraftServerCluster>(
        OperatorConstants.ClusterFinalizer);

// AddControllers registers webhook endpoints (they are ASP.NET Core controllers)
builder.Services.AddControllers();

var app = builder.Build();

app.UseRouting();
app.MapControllers();

await app.RunAsync();
