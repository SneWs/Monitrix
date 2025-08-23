using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Monitrix.System.Services.System.CPU;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddScoped<ICpuMonitoringService, CpuMonitoringService>();

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

app.Run();
