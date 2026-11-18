using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FerreteriaAPI.Models;
using System.Runtime.CompilerServices;
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

        // Validación al actualizar: Verificar que la cédula no pertenezca a OTRO cliente distinto
        var clienteExistente = await _context.Clientes
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Cedula == cliente.Cedula && c.Id != id);

        if (clienteExistente != null)
        {
            return BadRequest("Ya existe otro cliente registrado con el número de cédula ingresado.");
        }

        _context.Entry(cliente).State = EntityState.Modified;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return BadRequest(new { exito = false, mensaje = "Ya existe otro cliente registrado con el número de cédula ingresado." });
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
    [HttpPost]
    public async Task<ActionResult<Clientes>> PostCliente(Clientes cliente)
    {
        try
        {
            // 1. Validación previa para evitar duplicados de cédula
            cliente.Cedula = cliente.Cedula.Trim();
            bool existe = await _context.Clientes
                .AnyAsync(c => c.Cedula == cliente.Cedula);

            if (existe)
            {
                return BadRequest(new
                {
                    exito = false,
                    mensaje = "El registro ya existe: hay un cliente registrado con esa cédula."
                });
            }

            _context.Clientes.Add(cliente);
            await _context.SaveChangesAsync();

            // 2. Éxito rotundo: Retornamos 201 Created con estructura clara para el cliente/frontend
            return CreatedAtAction(nameof(GetCliente), new { id = cliente.Id }, new
            {
                exito = true,
                mensaje = "Cliente creado con éxito.",
                data = AClienteDto(cliente)
            });
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // 3. Red de seguridad (23505): doble clic o reintento de red que ya había insertado el cliente
            return BadRequest(new
            {
                exito = false,
                mensaje = "El registro ya existe: hay un cliente registrado con esos datos."
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
                mensaje = ex.Message,
                detalle = ex.InnerException?.Message
            });
        }
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
