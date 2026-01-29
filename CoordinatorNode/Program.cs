using CoordinatorNode.Hubs;
using CoordinatorNode.Services;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

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
app.MapHub<CoordinatorHub>("/hubs/transaction");

// --- ENDPOINT BIZNESOWY ---
app.MapPost("/update", async ([FromBody] UpdateRequest request, TransactionCoordinatorService coordinator) =>
{
    bool success = await coordinator.PerformTwoPhaseCommitAsync(request.Value);
    return success ? Results.Ok(new { status = "COMMITTED" }) : Results.BadRequest(new { status = "ABORTED" });
});

// --- ENDPOINTY STERUJĄCE (BŁĘDY) ---

app.MapPost("/restore", async (TransactionCoordinatorService coordinator) => 
{
    await coordinator.SetErrorStateAsync("None"); 
    return Results.Ok("Restored");
});

app.MapPost("/fail/{type}", async (string type, TransactionCoordinatorService service) =>
{
    await service.SetErrorStateAsync(type);
    return Results.Ok($"Set error: {type}");
});

app.MapPost("/fail1", async (TransactionCoordinatorService coordinator) => 
{
    await coordinator.SetErrorStateAsync("Timeout"); 
    return Results.Ok("Set Timeout Mode (Not fully impl in logic yet, but state set)");
});

// Specjalny stan do testowania awarii w trakcie
app.MapPost("/fail2", async (TransactionCoordinatorService coordinator) => 
{
    await coordinator.SetErrorStateAsync("Crash"); // Natychmiastowy zgon
    return Results.Ok("Crashed");
});

// Symulacja awarii tuż przed wysłaniem Commita (do testowania Recovery)
app.MapPost("/fail3", async (TransactionCoordinatorService coordinator) => 
{
    await coordinator.SetErrorStateAsync("CrashBeforeCommitSend"); 
    return Results.Ok("Armed CrashBeforeCommitSend");
});

app.Run();

public record UpdateRequest(string Value);