var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

string[] nodes = 
{
    "http://localhost:5010",
    "http://localhost:5011",
    "http://localhost:5012",
    "http://localhost:5013",
    "http://localhost:5014",
    "http://localhost:5015",
};

app.MapPost("/update", async (UpdateRequest request) =>
{
    using var client = new HttpClient();
    
    string value = request.Value; 

    Console.WriteLine($"[COORDINATOR] Otrzymano żądanie aktualizacji do wartości: {value}");

    // PHASE 1 — PREPARE
    foreach (var n in nodes)
    {
        var resp = await client.PostAsJsonAsync($"{n}/prepare", value);
        if (!resp.IsSuccessStatusCode)
        {
            // ABORT
            foreach (var n2 in nodes)
                await client.PostAsync($"{n2}/abort", null);
            return Results.BadRequest("ABORTED");
        }
    }

    // PHASE 2 — COMMIT
    foreach (var n in nodes)
    {
        await client.PostAsJsonAsync($"{n}/commit", value);
    }

    return Results.Ok("COMMITTED");
});

app.Run();
public record UpdateRequest(string Value);
