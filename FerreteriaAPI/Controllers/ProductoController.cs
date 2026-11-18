using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FerreteriaAPI.Models;
using FerreteriaAPI.Services;
using Npgsql;
using System.Linq.Expressions;

[Route("api/[controller]")]
[ApiController]
public class ProductosController : ControllerBase
{
    private readonly FerreteriaContext _context;

    public ProductosController(FerreteriaContext context)
    {
        _context = context;
    }

    // Proyección de lectura: solo columnas del producto y el nombre de su categoría
    private static readonly Expression<Func<Producto, ProductoDto>> ProyeccionProducto = p => new ProductoDto
    {
        Id = p.Id,
        Nombre = p.Nombre,
        Precio = p.Precio,
        Stock = p.Stock,
        Descripcion = p.Descripcion,
        IdCategoria = p.IdCategoria,
        IdProveedor = p.IdProveedor,
        CodigoBarras = p.CodigoBarras,
        Activo = p.Activo,
        IdCategoriaNavigation = p.IdCategoriaNavigation == null ? null : new CategoriaResumenDto
        {
            Id = p.IdCategoriaNavigation.Id,
            NombreCategoria = p.IdCategoriaNavigation.NombreCategoria,
            Descripcion = p.IdCategoriaNavigation.Descripcion
        }
    };

    // La misma proyección aplicada en memoria, para responder a POST/PUT con el producto ya guardado
    private static readonly Func<Producto, ProductoDto> AProductoDto = ProyeccionProducto.Compile();

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProductoDto>>> GetProductos()
    {
        // Solo retorna los productos que estén activos (Activo == true)
        return await _context.Productos
            .AsNoTracking()
            .Where(p => p.Activo)
            .Select(ProyeccionProducto)
            .ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ProductoDto>> GetProducto(int id)
    {
        var producto = await _context.Productos
            .AsNoTracking()
            .Where(p => p.Id == id && p.Activo)
            .Select(ProyeccionProducto)
            .FirstOrDefaultAsync();

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

        var existente = await _context.Productos.FindAsync(id);
        if (existente == null)
        {
            return NotFound();
        }

        // Se actualizan solo los campos del formulario de edición. CodigoBarras e IdProveedor no
        // vienen en ese formulario: si llegan en null se conservan los valores guardados
        // (antes cada edición los borraba). Un código de barras vacío ("") sí lo limpia.
        existente.Nombre = producto.Nombre;
        existente.Precio = producto.Precio;
        existente.Stock = producto.Stock;
        existente.Descripcion = producto.Descripcion;
        existente.IdCategoria = producto.IdCategoria;
        existente.Activo = producto.Activo;

        if (producto.CodigoBarras != null)
        {
            existente.CodigoBarras = string.IsNullOrWhiteSpace(producto.CodigoBarras) ? null : producto.CodigoBarras.Trim();
        }

        if (producto.IdProveedor.HasValue)
        {
            existente.IdProveedor = producto.IdProveedor;
        }

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return BadRequest(new
            {
                exito = false,
                mensaje = "El registro ya existe: el nombre o el código de barras pertenecen a otro producto."
            });
        }

        // 200 con el producto actualizado (antes 204 sin cuerpo): el frontend puede refrescar la fila al instante
        return Ok(new
        {
            exito = true,
            mensaje = "Producto actualizado con éxito.",
            data = AProductoDto(existente)
        });
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

            // 1. Validación previa de duplicados (nombre y código de barras)
            var nombreLimpio = producto.Nombre.Trim().ToLower();
            bool nombreExiste = await _context.Productos
                .AnyAsync(p => p.Nombre.Trim().ToLower() == nombreLimpio);

            if (nombreExiste)
            {
                return BadRequest(new
                {
                    exito = false,
                    mensaje = "El registro ya existe: hay un producto registrado con ese nombre."
                });
            }

            if (producto.CodigoBarras != null)
            {
                producto.CodigoBarras = producto.CodigoBarras.Trim();
                bool codigoExiste = await _context.Productos
                    .AnyAsync(p => p.CodigoBarras == producto.CodigoBarras);

                if (codigoExiste)
                {
                    return BadRequest(new
                    {
                        exito = false,
                        mensaje = "El registro ya existe: hay un producto registrado con ese código de barras."
                    });
                }
            }

            producto.Id = 0; // El id siempre lo genera la base
            _context.Productos.Add(producto);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException dbEx) when (SecuenciasIds.EsLlavePrimariaDuplicada(dbEx))
            {
                // Secuencia de productos.id por detrás de los datos: se sincroniza y se reintenta una vez
                await SecuenciasIds.SincronizarAsync(_context, "productos");
                _context.Entry(producto).State = EntityState.Detached;
                producto.Id = 0;
                _context.Productos.Add(producto);
                await _context.SaveChangesAsync();
            }

            // 2. Respuesta estructurada y limpia (201 Created)
            return CreatedAtAction(nameof(GetProducto), new { id = producto.Id }, new
            {
                exito = true,
                mensaje = "Producto creado con éxito.",
                data = AProductoDto(producto)
            });
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // 3. Red de seguridad (23505): doble clic o reintento de red que ya había insertado el producto
            return BadRequest(new
            {
                exito = false,
                mensaje = "El registro ya existe: el nombre o el código de barras ya se encuentran registrados."
            });
        }
        catch (DbUpdateException dbEx)
        {
            return BadRequest(new
            {
                exito = false,
                mensaje = "No se pudo registrar el producto.",
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
        await _context.SaveChangesAsync();

        return Ok(new { exito = true, mensaje = "Producto eliminado con éxito.", id });
    }

    private bool ProductoExists(int id)
    {
        return _context.Productos.Any(e => e.Id == id && e.Activo);
    }
}