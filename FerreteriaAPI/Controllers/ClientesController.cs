using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FerreteriaAPI.Models;
using FerreteriaAPI.Services;
using Npgsql;
//using Microsoft.IdentityModel.Tokens;

[Route("api/[controller]")]
[ApiController]
public class ClientesController : ControllerBase
{
    private readonly FerreteriaContext _context;

    public ClientesController(FerreteriaContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ClienteDto>>> GetClientes()
    {
        return await _context.Clientes
            .AsNoTracking()
            .Select(c => new ClienteDto { Id = c.Id, Nombre = c.Nombre, Telefono = c.Telefono, Email = c.Email, Cedula = c.Cedula })
            .ToListAsync();
    }

    // GET: api/Clientes/5
    [HttpGet("{id}")]
    public async Task<ActionResult<ClienteDto>> GetCliente(int id)
    {
        var cliente = await _context.Clientes
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new ClienteDto { Id = c.Id, Nombre = c.Nombre, Telefono = c.Telefono, Email = c.Email, Cedula = c.Cedula })
            .FirstOrDefaultAsync();
        if (cliente == null)
        {
            return NotFound();
        }
        return cliente;
    }

    // PUT: api/Clientes/5
    [HttpPut("{id}")]
    public async Task<IActionResult> PutCliente(int id, Clientes cliente)
    {
        if (id != cliente.Id)
        {
            return BadRequest();
        }

        NormalizarCliente(cliente);
        if (string.IsNullOrEmpty(cliente.Nombre) || string.IsNullOrEmpty(cliente.Cedula))
        {
            return BadRequest(new { exito = false, mensaje = "El nombre y la cédula del cliente son obligatorios." });
        }

        // Validación al actualizar: la cédula no debe pertenecer a OTRO cliente (consulta ligera, solo el nombre)
        var nombreOtroCliente = await _context.Clientes
            .AsNoTracking()
            .Where(c => c.Cedula == cliente.Cedula && c.Id != id)
            .Select(c => c.Nombre)
            .FirstOrDefaultAsync();

        if (nombreOtroCliente != null)
        {
            return Conflict(new { exito = false, mensaje = $"La cédula {cliente.Cedula} ya pertenece al cliente \"{nombreOtroCliente}\"." });
        }

        _context.Entry(cliente).State = EntityState.Modified;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            return Conflict(new { exito = false, mensaje = $"No se pudo actualizar: ya existe otro cliente con el mismo {DescribirRestriccion(pg.ConstraintName)}." });
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!ClienteExists(id))
            {
                return NotFound();
            }
            else
            {
                throw;
            }
        }

        // 200 con el cliente actualizado (antes 204 sin cuerpo)
        return Ok(new
        {
            exito = true,
            mensaje = "Cliente actualizado con éxito.",
            data = AClienteDto(cliente)
        });
    }

    // POST: api/Clientes
    // 201 con el cliente creado | 409 si la cédula ya pertenece a otro cliente (incluye ese cliente en
    // "data" para que el POS pueda seleccionarlo) | 400 si faltan datos.
    [HttpPost]
    public async Task<ActionResult<ClienteDto>> PostCliente(Clientes cliente)
    {
        NormalizarCliente(cliente);
        cliente.Id = 0; // El id siempre lo genera la base

        if (string.IsNullOrEmpty(cliente.Nombre) || string.IsNullOrEmpty(cliente.Cedula))
        {
            return BadRequest(new { exito = false, mensaje = "El nombre y la cédula del cliente son obligatorios." });
        }

        try
        {
            // 1. Validación previa: una sola consulta ligera (sin rastreo, solo 5 columnas, LIMIT 1)
            var existente = await BuscarPorCedulaAsync(cliente.Cedula);
            if (existente != null)
            {
                return ConflictoCedula(existente);
            }

            _context.Clientes.Add(cliente);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException dbEx) when (SecuenciasIds.EsLlavePrimariaDuplicada(dbEx))
            {
                // La secuencia del id quedó por detrás de los datos (p. ej. tras migrar registros con id
                // explícito a Supabase): cada INSERT generaba un id que ya existe y respondía "ya existe".
                // Se sincroniza la secuencia con el máximo id y se reintenta una sola vez.
                await SecuenciasIds.SincronizarAsync(_context, "clientes");
                _context.Entry(cliente).State = EntityState.Detached;
                cliente.Id = 0;
                _context.Clientes.Add(cliente);
                await _context.SaveChangesAsync();
            }

            return CreatedAtAction(nameof(GetCliente), new { id = cliente.Id }, new
            {
                exito = true,
                mensaje = "Cliente creado con éxito.",
                data = AClienteDto(cliente)
            });
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            _context.ChangeTracker.Clear();

            // Cédula duplicada que la validación previa no vio: o se registró en paralelo, o un reintento
            // de conexión (EnableRetryOnFailure) repitió un INSERT que ya se había confirmado.
            var existente = await BuscarPorCedulaAsync(cliente.Cedula);
            if (existente != null)
            {
                // Mismo nombre => es el mismo registro que acabamos de crear: se responde como creado
                if (string.Equals(existente.Nombre.Trim(), cliente.Nombre, StringComparison.OrdinalIgnoreCase))
                {
                    return CreatedAtAction(nameof(GetCliente), new { id = existente.Id }, new
                    {
                        exito = true,
                        mensaje = "Cliente creado con éxito.",
                        data = existente
                    });
                }
                return ConflictoCedula(existente);
            }

            // Otra restricción única (email, teléfono, id...): se dice cuál en lugar de un mensaje genérico
            return Conflict(new
            {
                exito = false,
                mensaje = $"No se pudo registrar: ya existe un cliente con el mismo {DescribirRestriccion(pg.ConstraintName)}.",
                restriccion = pg.ConstraintName
            });
        }
        catch (DbUpdateException dbEx)
        {
            return BadRequest(new
            {
                exito = false,
                mensaje = "No se pudo registrar el cliente.",
                detalle = dbEx.InnerException?.Message ?? dbEx.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                exito = false,
                mensaje = "Ocurrió un error inesperado al registrar el cliente.",
                detalle = ex.InnerException?.Message ?? ex.Message
            });
        }
    }

    // =========================================================================
    // AUXILIARES DE REGISTRO
    // =========================================================================

    // Recorta espacios; email/teléfono vacíos se guardan como NULL
    private static void NormalizarCliente(Clientes cliente)
    {
        cliente.Nombre = (cliente.Nombre ?? string.Empty).Trim();
        cliente.Cedula = (cliente.Cedula ?? string.Empty).Trim();
        cliente.Telefono = string.IsNullOrWhiteSpace(cliente.Telefono) ? null : cliente.Telefono.Trim();
        cliente.Email = string.IsNullOrWhiteSpace(cliente.Email) ? null : cliente.Email.Trim();
    }

    private Task<ClienteDto?> BuscarPorCedulaAsync(string cedula) =>
        _context.Clientes
            .AsNoTracking()
            .Where(c => c.Cedula == cedula)
            .Select(c => new ClienteDto { Id = c.Id, Nombre = c.Nombre, Telefono = c.Telefono, Email = c.Email, Cedula = c.Cedula })
            .FirstOrDefaultAsync();

    private ConflictObjectResult ConflictoCedula(ClienteDto existente) =>
        Conflict(new
        {
            exito = false,
            mensaje = $"El registro ya existe: la cédula {existente.Cedula} pertenece al cliente \"{existente.Nombre}\".",
            data = existente
        });

    private static string DescribirRestriccion(string? restriccion)
    {
        var nombre = restriccion?.ToLowerInvariant() ?? string.Empty;
        if (nombre.Contains("cedula")) return "número de cédula";
        if (nombre.Contains("email") || nombre.Contains("correo")) return "correo electrónico";
        if (nombre.Contains("telefono")) return "teléfono";
        if (nombre.EndsWith("_pkey")) return "identificador interno (id)";
        return $"dato único ({restriccion})";
    }

    private bool ClientesExists(int id)
    {
        return _context.Clientes.Any(e => e.Id == id);
    }
    // DELETE: api/Clientes/5
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCliente(int id)
    {
        var cliente = await _context.Clientes.FindAsync(id);
        if (cliente == null)
        {
            return NotFound(new { exito = false, mensaje = "Cliente no encontrado." });
        }

        // Borrado Lógico: En lugar de removerlo, cambiamos su estado a inactivo
        // cliente.Activo = false; // (Asumiendo que tienes una propiedad booleana de estado)

        // O si prefieres mantener tu estructura actual pero actualizando un campo:
        _context.Clientes.Update(cliente);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // Mismo formato que GET api/Clientes, para las respuestas de POST/PUT
    private static ClienteDto AClienteDto(Clientes c) =>
        new ClienteDto { Id = c.Id, Nombre = c.Nombre, Telefono = c.Telefono, Email = c.Email, Cedula = c.Cedula };

    private bool ClienteExists(int id)
    {
        return _context.Clientes.Any(e => e.Id == id);
    }
}
