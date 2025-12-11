using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

bool isFailed = false; 
string storedValue = "";

app.MapPost("/prepare", ([FromBody]string value) =>
{
    if (isFailed) return Results.BadRequest("Server failed.");
    // Symulacja: zawsze odpowiada YES w fazie prepare
    return Results.Ok("YES");
});

app.MapPost("/commit", ([FromBody] string value) =>
{
    if (isFailed) return Results.BadRequest("Server failed.");
    storedValue = value;
    return Results.Ok("COMMIT_OK");
});

app.MapPost("/abort", () =>
{
    return Results.Ok("ABORTED");
});

app.MapPost("/fail/{type}", (string type) =>
{
    isFailed = true;
    return Results.Ok($"Server failed with type: {type}");
});

app.MapPost("/recover", () =>
{
    isFailed = false;
    return Results.Ok("Server recovered.");
});

app.MapGet("/status", () =>
{
    return new { Failed = isFailed, Value = storedValue };
});

app.Run();
