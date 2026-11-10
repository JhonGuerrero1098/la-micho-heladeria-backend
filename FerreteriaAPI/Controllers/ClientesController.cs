using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FerreteriaAPI.Models;
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
        // Validación al crear: Verificar si ya existe un cliente con la misma cédula
        var clienteExistente = await _context.Clientes
            .FirstOrDefaultAsync(c => c.Cedula == cliente.Cedula);

        if (clienteExistente != null)
        {
            return BadRequest("Ya existe un cliente registrado con el número de cédula ingresado.");
        }

        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetCliente), new { id = cliente.Id }, cliente);
    }

    // DELETE: api/Clientes/5
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCliente(int id)
    {
        var cliente = await _context.Clientes.FindAsync(id);
        if (cliente == null)
        {
            return NotFound();
        }

        _context.Clientes.Remove(cliente);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private bool ClienteExists(int id)
    {
        return _context.Clientes.Any(e => e.Id == id);
    }
}