using EsdCleaningSystem.Models;
using EsdCleaningSystem.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure binding: Allow Azure App Service to manage port binding when deployed, or bind to 0.0.0.0:5000 locally
string? isAzure = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME");
if (string.IsNullOrEmpty(isAzure))
{
    string port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddHttpClient();

// Register App Configuration & Services (Register SupabaseService before StorageService for DI)
var supabaseSettings = new SupabaseSettings();
builder.Services.AddSingleton(supabaseSettings);
builder.Services.AddSingleton<SupabaseService>();
builder.Services.AddSingleton<StorageService>();
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
