using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FerreteriaAPI.Models;
using System.Runtime.CompilerServices;
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
    public async Task<ActionResult<IEnumerable<Clientes>>> GetClientes()
    {
        return await _context.Clientes.ToListAsync();
    }

    // GET: api/Clientes/5
    [HttpGet("{id}")]
    public async Task<ActionResult<Clientes>> GetCliente(int id)
    {
        var cliente = await _context.Clientes.FindAsync(id);
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
        return NoContent();
    }

    // POST: api/Clientes
    [HttpPost]
    public async Task<ActionResult<Clientes>> PostCliente(Clientes cliente)
    {
        try
        {
            // 1. Validación preventiva en memoria para evitar duplicados de cédula
            bool existe = await _context.Clientes
                .AnyAsync(c => c.Cedula == cliente.Cedula);

            if (existe)
            {
                // Retornamos 409 Conflict indicando explícitamente que no se creó por duplicidad
                return Conflict(new
                {
                    exito = false,
                    mensaje = "Ya existe un cliente registrado con esa cédula."
                });
            }

            _context.Clientes.Add(cliente);
            await _context.SaveChangesAsync();

            // 2. Éxito rotundo: Retornamos 201 Created con estructura clara para el cliente/frontend
            return CreatedAtAction(nameof(GetCliente), new { id = cliente.Id }, new
            {
                exito = true,
                mensaje = "Cliente registrado con éxito.",
                data = cliente
            });
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
        {
            // 3. Red de seguridad: Si la BD frena el duplicado por concurrencia (Unique Constraint)
            return Conflict(new
            {
                exito = false,
                mensaje = "Cliente registrado con éxito.",
                detalle = dbEx.InnerException?.Message
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

    private bool ClienteExists(int id)
    {
        return _context.Clientes.Any(e => e.Id == id);
    }
}
