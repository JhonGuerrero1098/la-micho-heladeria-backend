using FerreteriaAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace FerreteriaAPI.Services;

// Registro atómico: "validar que no existe" + "insertar" dentro de una transacción que primero toma
// un bloqueo de Postgres (pg_advisory_xact_lock) sobre una clave lógica, p. ej. "clientes:<cédula>".
// Dos peticiones idénticas casi simultáneas (doble clic, listener duplicado, reintento de red) ya no
// pasan ambas la validación previa: la segunda espera a que la primera confirme y entonces ve el
// registro ya creado. El bloqueo se libera solo al terminar la transacción.
public static class RegistroAtomico
{
    public static Task<T> EjecutarAsync<T>(FerreteriaContext context, string claveBloqueo, Func<Task<T>> accion)
    {
        var estrategia = context.Database.CreateExecutionStrategy();

        return estrategia.ExecuteAsync(async () =>
        {
            // Cada reintento de la estrategia arranca con el contexto limpio
            context.ChangeTracker.Clear();

            await using var transaccion = await context.Database.BeginTransactionAsync();
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({claveBloqueo}))");

            var resultado = await accion();

            await transaccion.CommitAsync();
            return resultado;
        });
    }
}
