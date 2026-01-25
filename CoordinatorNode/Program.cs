using CoordinatorNode.Hubs;
using CoordinatorNode.Services;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Rejestracja serwisów
builder.Services.AddSignalR();
builder.Services.AddHttpClient(); 

builder.Services.AddSingleton<TransactionCoordinatorService>(); 

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowUI", policy =>
    {
        policy.WithOrigins("http://localhost:5001", "http://127.0.0.1:5001") 
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials(); 
    });
});

var app = builder.Build();

app.UseCors("AllowUI");

// Mapowanie Huba
app.MapHub<CoordinatorHub>("/hubs/transaction");

// Endpointy Biznesowe
app.MapPost("/update", async ([FromBody] UpdateRequest request, TransactionCoordinatorService coordinator) =>
{
    bool success = await coordinator.PerformTwoPhaseCommitAsync(request.Value);
    return success ? Results.Ok(new { status = "COMMITTED" }) : Results.BadRequest(new { status = "ABORTED" });
});

// Endpointy Sterujące (Symulacja Błędów)

app.MapPost("/restore", async (TransactionCoordinatorService coordinator) => 
{
    await coordinator.SetErrorStateAsync("None"); // Bez błędów
    return Results.Ok("Restored");
});

app.MapPost("/fail1", async (TransactionCoordinatorService coordinator) => 
{
    await coordinator.SetErrorStateAsync("Timeout"); // Np. Timeout
    return Results.Ok("Injected Error 1");
});

app.MapPost("/fail2", async (TransactionCoordinatorService coordinator) => 
{
    await coordinator.SetErrorStateAsync("Crash"); // Np. Crash
    return Results.Ok("Injected Error 2");
});

app.MapPost("/fail3", async (TransactionCoordinatorService coordinator) => 
{
    await coordinator.SetErrorStateAsync("DbError"); // Np. Błąd zapisu
    return Results.Ok("Injected Error 3");
});

app.Run();

public record UpdateRequest(string Value);