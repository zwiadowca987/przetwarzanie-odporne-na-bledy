using ServerNode.Services;
using Microsoft.AspNetCore.Mvc;
using ServerNode;
using ServerNode.dto;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<TransactionClientService>();
builder.Services.AddHttpClient();

var app = builder.Build();

var transactionService = app.Services.GetRequiredService<TransactionClientService>();
_ = Task.Run(async () => await transactionService.StartAsync());

// --- API 2PC ---

app.MapPost("/prepare", async ([FromBody] TransactionRequest req, TransactionClientService service) =>
{
    var result = await service.PrepareAsync(req.TransactionId, req.Value);
    return result ? Results.Ok("VOTE_COMMIT") : Results.BadRequest("VOTE_ABORT"); 
});

app.MapPost("/commit", async ([FromBody] TransactionRequest req, TransactionClientService service) =>
{
    var result = await service.CommitAsync(req.TransactionId, req.Value);
    return result ? Results.Ok("COMMITTED") : Results.StatusCode(500);
});

app.MapPost("/abort", async (TransactionClientService service) =>
{
    await service.AbortAsync();
    return Results.Ok("ABORTED");
});


// --- API STERUJĄCE (AWARIE) ---

app.MapPost("/restore", async (TransactionClientService service) =>
{
    await service.SetErrorStateAsync("None");
    return Results.Ok("Restored");
});

app.MapPost("/fail/{type}", async (string type, TransactionClientService service) =>
{
    // type: Fail, CrashBeforeVote, CrashAfterVote 
    await service.SetErrorStateAsync(type);
    return Results.Ok($"Set error: {type}");
});


// --- API STATUSOWE ---

app.MapGet("/status", async (TransactionClientService service) =>
{
    await service.BroadcastStatus();
    return Results.Ok("Status broadcasted via SignalR");
});


// --- API KOMUNIKACJA MIĘDZY SERWERAMI ---

app.MapGet("/ask-status/{transactionId}", (string transactionId, TransactionClientService service) =>
{
    // Inny węzeł pyta nas o stan transakcji
    var status = service.GetLocalStatus(transactionId);
    return Results.Ok(status);
});
app.Run();