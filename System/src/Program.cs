using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Monitrix.System.Services.System.CPU;
using Monitrix.System.Services.System.ProcessInfo;
using Monitrix.System.Services.System.RAM;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddScoped<ICpuMonitoringService, CpuMonitoringService>();
builder.Services.AddScoped<IRamMonitoringService, RamMonitoringService>();
builder.Services.AddScoped<IProcessMonitoringService, ProcessMonitoringService>();

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

app.Run();
