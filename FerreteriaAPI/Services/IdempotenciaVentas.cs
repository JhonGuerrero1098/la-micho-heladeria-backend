using FerreteriaAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace FerreteriaAPI.Services;

public static class IdempotenciaVentas
{
    // Cabecera que envía el frontend con un UUID por cada venta
    public const string Cabecera = "X-Idempotency-Key";

    // Cabecera de respuesta que indica que se devolvió el resultado guardado (no se ejecutó de nuevo)
    public const string CabeceraRepeticion = "Idempotent-Replayed";

    // Tiempo durante el cual una clave sigue siendo válida; después se ignora y se borra
    public static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(15);

    // Mismo esquema que Database/idempotencia_ventas.sql. Se ejecuta al arrancar (IF NOT EXISTS),
    // así el despliegue en Render crea la tabla en Supabase sin pasos manuales.
    public const string SqlCrearTabla = """
        CREATE TABLE IF NOT EXISTS idempotencia_ventas (
            clave          uuid        PRIMARY KEY,
            huella         text        NOT NULL,
            id_venta       integer     NULL REFERENCES ventas(id) ON DELETE CASCADE,
            codigo_estado  integer     NULL,
            respuesta      text        NULL,
            creado_en      timestamptz NOT NULL DEFAULT now()
        );
        CREATE INDEX IF NOT EXISTS ix_idempotencia_ventas_creado_en ON idempotencia_ventas (creado_en);
        """;

    public static async Task AsegurarTablaAsync(IServiceProvider servicios, ILogger logger)
    {
        try
        {
            using var scope = servicios.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<FerreteriaContext>();
            await context.Database.ExecuteSqlRawAsync(SqlCrearTabla);
        }
        catch (Exception ex)
        {
            // No se detiene la API: sin la tabla, PostVenta con clave fallará y lo dirá en su respuesta
            logger.LogError(ex, "No se pudo crear/verificar la tabla idempotencia_ventas");
        }
    }
}

// Borra cada 5 minutos las claves con más de 15 minutos para que la tabla no crezca sin límite.
// (Las claves vencidas además se ignoran en PostVenta aunque esta limpieza aún no haya pasado.)
public class LimpiezaIdempotenciaService : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LimpiezaIdempotenciaService> _logger;

    public LimpiezaIdempotenciaService(IServiceScopeFactory scopeFactory, ILogger<LimpiezaIdempotenciaService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var temporizador = new PeriodicTimer(Intervalo);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<FerreteriaContext>();
                var limite = DateTime.UtcNow - IdempotenciaVentas.Vigencia;

                var borradas = await context.IdempotenciaVentas
                    .Where(i => i.CreadoEn < limite)
                    .ExecuteDeleteAsync(stoppingToken);

                if (borradas > 0)
                {
                    _logger.LogInformation("Limpieza de idempotencia: {Borradas} claves vencidas eliminadas", borradas);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falló la limpieza de claves de idempotencia; se reintentará");
            }
        }
        while (await temporizador.WaitForNextTickAsync(stoppingToken));
    }
}
