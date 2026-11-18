using FerreteriaAPI.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FerreteriaAPI.Services;

// Corrige secuencias de ids que quedaron por detrás de los datos (p. ej. tras migrar registros con
// id explícito a Supabase): en ese caso cada INSERT genera un id que ya existe y Postgres responde
// 23505 sobre la llave primaria, aunque los datos del registro sean nuevos.
public static class SecuenciasIds
{
    // Tablas/columnas permitidas: el SQL se arma con estos valores fijos, nunca con datos del usuario
    private static readonly Dictionary<string, string> ColumnasId = new()
    {
        ["clientes"] = "id",
        ["productos"] = "id",
        ["ventas"] = "id",
        ["detalleventa"] = "iddetalle",
    };

    // true si el error es una llave primaria duplicada (restricción <tabla>_pkey)
    public static bool EsLlavePrimariaDuplicada(DbUpdateException dbEx) =>
        dbEx.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && (pg.ConstraintName?.EndsWith("_pkey", StringComparison.OrdinalIgnoreCase) ?? false);

    // Lleva la secuencia/identidad de la tabla al siguiente valor libre (sirve para serial e identity)
    public static Task SincronizarAsync(FerreteriaContext context, string tabla)
    {
        if (!ColumnasId.TryGetValue(tabla, out var columna))
        {
            throw new ArgumentException($"Tabla no permitida para sincronizar secuencia: {tabla}", nameof(tabla));
        }

#pragma warning disable EF1002 // tabla y columna salen de la lista fija de arriba
        return context.Database.ExecuteSqlRawAsync(
            $"SELECT setval(pg_get_serial_sequence('{tabla}', '{columna}'), COALESCE((SELECT MAX({columna}) FROM {tabla}), 0) + 1, false)");
#pragma warning restore EF1002
    }
}
