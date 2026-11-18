using FerreteriaAPI.Models;
using FerreteriaAPI.Services;
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

// Compresión Brotli/Gzip de las respuestas JSON (menos bytes entre Render y el navegador)
builder.Services.AddResponseCompression(options => options.EnableForHttps = true);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Política CORS para permitir peticiones desde el frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularPolicy", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod()
              // Permite que el frontend lea si la venta fue una repetición idempotente
              .WithExposedHeaders(IdempotenciaVentas.CabeceraRepeticion);
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
            npgsqlOptions.CommandTimeout(50); // Margen de segundos para evitar timeouts de red con Supabase
            npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount:5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorCodesToAdd: null

            );
        }
    ));

// Hash de contraseñas (PBKDF2 con HMAC-SHA512, sal aleatoria por usuario)
builder.Services.AddScoped<IPasswordHasher<Usuarios>, PasswordHasher<Usuarios>>();

// Limpieza periódica de las claves de idempotencia de ventas (vencen a los 15 minutos)
builder.Services.AddHostedService<LimpiezaIdempotenciaService>();

var app = builder.Build();

app.UseResponseCompression();

// Las respuestas de la API nunca deben salir de una caché (navegador, proxy de Render/CDN):
// tras guardar, el frontend vuelve a pedir los listados y debe recibir siempre el estado actual.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            context.Response.Headers.Pragma = "no-cache";
            return Task.CompletedTask;
        });
    }
    await next();
});

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

// Crea la tabla idempotencia_ventas en Supabase si aún no existe
await IdempotenciaVentas.AsegurarTablaAsync(app.Services, app.Logger);

app.Run();