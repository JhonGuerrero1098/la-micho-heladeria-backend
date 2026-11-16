using FerreteriaAPI.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Configuración para que Render asigne el puerto dinámico correctamente
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Configuración de controladores y manejo de ciclos de referencias JSON (Evita bucles infinitos en .Include)
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Política CORS para permitir peticiones desde el frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularPolicy", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// ==========================================
// REGISTRO DEL CONTEXTO
// (Sin EnableRetryOnFailure para permitir transacciones manuales limpias con Npgsql)
// ==========================================
builder.Services.AddDbContext<FerreteriaContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsqlOptions =>
        {
            npgsqlOptions.CommandTimeout(30); // Margen de segundos para evitar timeouts de red con Supabase
            npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount:5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorCodesToAdd: null

            );
        }
    ));

// Hash de contraseñas (PBKDF2 con HMAC-SHA512, sal aleatoria por usuario)
builder.Services.AddScoped<IPasswordHasher<Usuarios>, PasswordHasher<Usuarios>>();

var app = builder.Build();

// Configure the HTTP request pipeline.
// Habilitamos Swagger de manera global para que funcione perfecto en Render
app.UseSwagger();
app.UseSwaggerUI();

// Redirige la raíz "/" directamente a la interfaz de Swagger
app.MapGet("/", async context =>
{
    context.Response.Redirect("/swagger");
    await Task.CompletedTask;
});


app.UseHttpsRedirection();
app.UseCors("AngularPolicy");
app.UseAuthorization();
app.MapControllers();

app.Run();