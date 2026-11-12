using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FerreteriaAPI.Models;

[Route("api/[controller]")]
[ApiController]
public class ProductosController : ControllerBase
{
    private readonly FerreteriaContext _context;

    public ProductosController(FerreteriaContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Producto>>> GetProductos()
    {
        // Solo retorna los productos que estén activos (Activo == true)
        return await _context.Productos
            .Where(p => p.Activo)
            .Include(p => p.IdCategoriaNavigation)
            .ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Producto>> GetProducto(int id)
    {
        var producto = await _context.Productos
            .Include(p => p.IdCategoriaNavigation)
            .FirstOrDefaultAsync(p => p.Id == id && p.Activo);

        if (producto == null)
        {
            return NotFound();
        }

        return producto;
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> PutProducto(int id, Producto producto)
    {
        if (id != producto.Id)
        {
            return BadRequest();
        }

        _context.Entry(producto).State = EntityState.Modified;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!ProductoExists(id))
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

    // POST: api/Productos
    [HttpPost]
    public async Task<ActionResult<Producto>> PostProducto(Producto producto)
    {
        try
        {
            producto.Activo = true; // Asegurar que nazca activo por defecto

            // Si viene un código de barras vacío, lo guardamos como NULL para que no cause conflictos
            if (string.IsNullOrWhiteSpace(producto.CodigoBarras))
            {
                producto.CodigoBarras = null;
            }

            _context.Productos.Add(producto);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetProducto", new { id = producto.Id }, producto);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
        {
            // Si Supabase frena un código de barras repetido por el índice único
            return BadRequest(new { mensaje = "No se pudo registrar el producto. El código de barras ya se encuentra registrado.", detalle = dbEx.InnerException?.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { mensaje = ex.Message, detalle = ex.InnerException?.Message });
        }
    }

    // SOFT DELETE: En lugar de borrar de la BD, marcamos Activo = false
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProducto(int id)
    {
        var producto = await _context.Productos.FindAsync(id);
        if (producto == null)
        {
            return NotFound();
        }

        // Cambiamos el estado a inactivo en lugar de removerlo físicamente
        producto.Activo = false;
        _context.Entry(producto).State = EntityState.Modified;
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private bool ProductoExists(int id)
    {
        return _context.Productos.Any(e => e.Id == id && e.Activo);
    }
}