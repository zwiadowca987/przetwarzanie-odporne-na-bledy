using Microsoft.AspNetCore.Mvc;
using ServerNode.Services;

var builder = WebApplication.CreateBuilder(args);

// Rejestracja serwisu jako Singleton
builder.Services.AddSingleton<TransactionClientService>();

var app = builder.Build();

// Automatyczny start połączenia SignalR przy starcie aplikacji
var transactionService = app.Services.GetRequiredService<TransactionClientService>();
_ = Task.Run(async () => await transactionService.StartAsync());


// --- ENDPOINTY 2PC ---

app.MapPost("/prepare", async ([FromBody] string value, TransactionClientService service) =>
{
    var result = await service.PrepareAsync(value);
    return result ? Results.Ok("VOTE_COMMIT") : Results.BadRequest("VOTE_ABORT");
});

app.MapPost("/commit", async ([FromBody] string value, TransactionClientService service) =>
{
    var result = await service.CommitAsync(value);
    return result ? Results.Ok("COMMITTED") : Results.StatusCode(500);
});

app.MapPost("/abort", (TransactionClientService service) =>
{
    service.Abort();
    return Results.Ok("ABORTED");
});


// --- ENDPOINTY STERUJĄCE (FAULTS) ---

app.MapPost("/restore", async (TransactionClientService service) =>
{
    await service.SetErrorStateAsync("None");
    return Results.Ok("OK");
});

app.MapPost("/fail/timeout", async (TransactionClientService service) =>
{
    await service.SetErrorStateAsync("Timeout");
    return Results.Ok("OK");
});

app.MapPost("/fail/crash", async (TransactionClientService service) =>
{
    await service.SetErrorStateAsync("Crash");
    return Results.Ok("OK");
});

app.MapPost("/fail/dberror", async (TransactionClientService service) =>
{
    await service.SetErrorStateAsync("DbError");
    return Results.Ok("OK");
});

app.MapGet("/status", (TransactionClientService service) =>
{
    return Results.Ok(service.GetStatus());
});

app.Run();