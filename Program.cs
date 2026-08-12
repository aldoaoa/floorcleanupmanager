using EsdCleaningSystem.Models;
using EsdCleaningSystem.Services;

var builder = WebApplication.CreateBuilder(args);

// Bind to 0.0.0.0:5000 so other PCs on the network can access the system
builder.WebHost.UseUrls("http://0.0.0.0:5000");

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddHttpClient();

// Register App Configuration & Services
var supabaseSettings = new SupabaseSettings();
builder.Services.AddSingleton(supabaseSettings);
builder.Services.AddSingleton<StorageService>();
builder.Services.AddSingleton<SupabaseService>();
builder.Services.AddSingleton<EsdEvaluationEngine>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.UseCors("AllowAll");
app.UseAuthorization();

app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
