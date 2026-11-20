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

        var errorValores = ValidarPrecioYStock(producto);
        if (errorValores != null) return errorValores;

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
    public async Task<ActionResult<ProductoDto>> PostProducto(Producto producto)
    {
        producto.Activo = true; // Asegurar que nazca activo por defecto
        producto.Nombre = (producto.Nombre ?? string.Empty).Trim();
        // Código de barras vacío se guarda como NULL para que no cause conflictos
        producto.CodigoBarras = string.IsNullOrWhiteSpace(producto.CodigoBarras) ? null : producto.CodigoBarras.Trim();

        if (string.IsNullOrEmpty(producto.Nombre))
        {
            return BadRequest(new { exito = false, mensaje = "El nombre del producto es obligatorio." });
        }

        var errorValores = ValidarPrecioYStock(producto);
        if (errorValores != null) return errorValores;

        var nombreLimpio = producto.Nombre.ToLower();

        // Validación + inserción atómicas por nombre: una segunda petición idéntica casi simultánea
        // espera a que la primera confirme y entonces encuentra el producto ya creado.
        Func<Task<ActionResult<ProductoDto>>> registrar = async () =>
        {
            // Validación previa de duplicados (nombre y código de barras): consulta proyectada, sin rastreo
            var existente = await _context.Productos
                .AsNoTracking()
                .Where(p => p.Nombre.Trim().ToLower() == nombreLimpio
                         || (producto.CodigoBarras != null && p.CodigoBarras == producto.CodigoBarras))
                .Select(ProyeccionProducto)
                .FirstOrDefaultAsync();

            if (existente != null)
            {
                return RespuestaProductoExistente(existente, producto);
            }

            producto.Id = 0; // El id siempre lo genera la base
            _context.Productos.Add(producto);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetProducto), new { id = producto.Id }, new
            {
                exito = true,
                mensaje = "Producto creado con éxito.",
                data = AProductoDto(producto)
            });
        };

        try
        {
            try
            {
                return await RegistroAtomico.EjecutarAsync(_context, $"productos:{nombreLimpio}", registrar);
            }
            catch (DbUpdateException dbEx) when (SecuenciasIds.EsLlavePrimariaDuplicada(dbEx))
            {
                // Secuencia de productos.id por detrás de los datos: la transacción ya se revirtió;
                // se sincroniza la secuencia y se reintenta una vez
                await SecuenciasIds.SincronizarAsync(_context, "productos");
                return await RegistroAtomico.EjecutarAsync(_context, $"productos:{nombreLimpio}", registrar);
            }
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Red de seguridad (23505): un reintento de conexión que repitió un INSERT ya confirmado, o un
            // código de barras que choca con otro producto
            _context.ChangeTracker.Clear();
            var existente = await _context.Productos
                .AsNoTracking()
                .Where(p => p.Nombre.Trim().ToLower() == nombreLimpio
                         || (producto.CodigoBarras != null && p.CodigoBarras == producto.CodigoBarras))
                .Select(ProyeccionProducto)
                .FirstOrDefaultAsync();

            if (existente != null)
            {
                return RespuestaProductoExistente(existente, producto);
            }

            return Conflict(new
            {
                exito = false,
                mensaje = "El registro ya existe: el nombre o el código de barras ya se encuentran registrados."
            });
        }
        catch (DbUpdateException dbEx)
        {
            // El detalle técnico va a los logs de Render, no al navegador
            HttpContext.RequestServices.GetRequiredService<ILogger<ProductosController>>()
                .LogError(dbEx, "Error de base de datos al registrar producto");
            return BadRequest(new { exito = false, mensaje = "No se pudo registrar el producto." });
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<ProductosController>>()
                .LogError(ex, "Error inesperado al registrar producto");
            return StatusCode(500, new { exito = false, mensaje = "Ocurrió un error inesperado al registrar el producto." });
        }
    }

    // Precio y stock nunca negativos: un stock negativo permitiría vender lo que no existe
    private ActionResult? ValidarPrecioYStock(Producto producto)
    {
        if (producto.Precio < 0)
        {
            return BadRequest(new { exito = false, mensaje = "El precio no puede ser negativo." });
        }
        if (producto.Stock < 0)
        {
            return BadRequest(new { exito = false, mensaje = "El stock no puede ser negativo." });
        }
        return null;
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

    // Petición repetida con los mismos datos (doble envío): 200 con el producto ya guardado, sin error.
    // Mismo nombre/código con otros datos, o producto eliminado: 409 con un mensaje claro.
    private ActionResult<ProductoDto> RespuestaProductoExistente(ProductoDto existente, Producto solicitado)
    {
        bool mismosDatos = existente.Activo
            && string.Equals(existente.Nombre.Trim(), solicitado.Nombre, StringComparison.OrdinalIgnoreCase)
            && existente.Precio == solicitado.Precio
            && existente.IdCategoria == solicitado.IdCategoria;

        if (mismosDatos)
        {
            return Ok(new
            {
                exito = true,
                yaExistia = true,
                mensaje = $"El producto \"{existente.Nombre}\" ya estaba registrado.",
                data = existente
            });
        }

        string motivo;
        if (!existente.Activo)
            motivo = $"hay un producto eliminado (inactivo) con el nombre \"{existente.Nombre}\"";
        else if (solicitado.CodigoBarras != null && existente.CodigoBarras == solicitado.CodigoBarras
                 && !string.Equals(existente.Nombre.Trim(), solicitado.Nombre, StringComparison.OrdinalIgnoreCase))
            motivo = $"el código de barras pertenece al producto \"{existente.Nombre}\"";
        else
            motivo = $"ya hay un producto registrado con el nombre \"{existente.Nombre}\"";

        return Conflict(new { exito = false, mensaje = $"El registro ya existe: {motivo}.", data = existente });
    }

    private bool ProductoExists(int id)
    {
        return _context.Productos.Any(e => e.Id == id && e.Activo);
    }
}