using UI.Services;
using UI.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Coordinator communication service
builder.Services.AddHttpClient<CoordinatorService>(client =>
{
    client.BaseAddress = new Uri("http://localhost:5000"); // CoordinatorNode
});

// Remove HTTPS – Coordinator is HTTP only
// app.UseHttpsRedirection();
// app.UseHsts();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
