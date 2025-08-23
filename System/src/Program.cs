using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Monitrix.System.Services.System.CPU;
using Monitrix.System.Services.System.GPU;
using Monitrix.System.Services.System.Network;
using Monitrix.System.Services.System.ProcessInfo;
using Monitrix.System.Services.System.RAM;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddScoped<ICpuMonitoringService, CpuMonitoringService>();
builder.Services.AddScoped<IRamMonitoringService, RamMonitoringService>();
builder.Services.AddScoped<IProcessMonitoringService, ProcessMonitoringService>();
builder.Services.AddScoped<IGpuMonitoringService, GpuMonitoringService>();
builder.Services.AddScoped<INetworkMonitoringService, NetworkMonitoringService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/", () => "Welcome to Monitrix System API!");

app.MapGet("/cpu", async (ICpuMonitoringService cpuMonitoringService) =>
{
    var cpuSnapshot = await cpuMonitoringService.ReadCpuSnapshotAsync();
    return Results.Ok(cpuSnapshot);
});

app.MapGet("/cpu-info", async (ICpuMonitoringService cpuMonitoringService) =>
{
    var cpuInfo = await cpuMonitoringService.ReadCpuInfoAsync();
    return Results.Ok(cpuInfo);
});

app.MapGet("/cpu-usage", async (ICpuMonitoringService cpuMonitoringService) =>
{
    var cpuUsage = await cpuMonitoringService.ReadCpuUsageAsync();
    return Results.Ok(cpuUsage);
});

app.MapGet("/ram", async (IRamMonitoringService ramMonitoringService) =>
{
    var ramUsage = await ramMonitoringService.ReadRamUsageAsync();
    return Results.Ok(ramUsage);
});

app.MapGet("/processes", async (IProcessMonitoringService processMonitoringService) =>
{
    var processes = await processMonitoringService.ListProcessesAsync();
    return Results.Ok(processes);
});

app.MapGet("/gpu", async (IGpuMonitoringService gpuMonitoringService) =>
{
    var gpuSnapshot = await gpuMonitoringService.GetGpuSnapshotAsync();
    return Results.Ok(gpuSnapshot);
});

app.MapGet("/gpu-info", async (IGpuMonitoringService gpuMonitoringService) =>
{
    var gpus = await gpuMonitoringService.ListGpusAsync();
    return Results.Ok(gpus);
});

app.MapGet("/gpu-usage", async (IGpuMonitoringService gpuMonitoringService) =>
{
    var gpuUsage = await gpuMonitoringService.GetGpuUsageAsync();
    return Results.Ok(gpuUsage);
});

app.MapGet("/network-info", async (INetworkMonitoringService networkMonitoringService) =>
{
    var networkInterfaces = await networkMonitoringService.ListNetworkInterfacesAsync();
    return Results.Ok(networkInterfaces);
});

app.Run();
