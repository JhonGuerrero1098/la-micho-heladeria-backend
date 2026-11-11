using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FerreteriaAPI.Models;
using FerreteriaAPI.Data; // <-- Importante para usar AppDbContext

[Route("api/[controller]")]
[ApiController]
public class CategoriasController : ControllerBase
{
    private readonly AppDbContext _context;

    public CategoriasController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Categorias>>> GetCategorias()
    {
        return await _context.Categorias.ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Categorias>> GetCategorias(int id)
    {
        var categorias = await _context.Categorias.FindAsync(id);

        if (categorias == null)
        {
            return NotFound();
        }

        return categorias;
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> PutCategorias(int id, Categorias categorias)
    {
        if (id != categorias.Id)
        {
            return BadRequest();
        }

        _context.Entry(categorias).State = EntityState.Modified;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!CategoriasExists(id))
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

    [HttpPost]
    public async Task<ActionResult<Categorias>> PostCategorias(Categorias categorias)
    {
        try
        {
            _context.Categorias.Add(categorias);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetCategorias", new { id = categorias.Id }, categorias);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { mensaje = ex.Message, detalle = ex.InnerException?.Message });
        }
    }

    private bool CategoriasExists(int id)
    {
        return _context.Categorias.Any(e => e.Id == id);
    }
}