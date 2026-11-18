using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FerreteriaAPI.Models;

[Route("api/[controller]")]
[ApiController]
public class VentasController : ControllerBase
{
    private readonly FerreteriaContext _context;

    public VentasController(FerreteriaContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Ventas>>> GetVentas()
    {
        return await _context.Ventas
            .AsNoTracking()
            .Include(v => v.IdClienteNavigation)
            .Include(v => v.DetalleVenta)
                .ThenInclude(d => d.IdProductoNavigation)
            .ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Ventas>> GetVenta(int id)
    {
        var venta = await _context.Ventas
            .AsNoTracking()
            .Include(v => v.IdClienteNavigation)
            .Include(v => v.DetalleVenta)
                .ThenInclude(d => d.IdProductoNavigation)
            .FirstOrDefaultAsync(v => v.Id == id);

        if (venta == null)
        {
            return NotFound();
        }

        return venta;
    }
    [HttpPost]
    public async Task<ActionResult<Ventas>> PostVenta([FromBody] VentaCreateDto dto)
    {
        // Validación inicial de los detalles
        if (dto.DetalleVenta == null || !dto.DetalleVenta.Any())
        {
            return BadRequest(new { exito = false, mensaje = "La venta debe incluir al menos un producto." });
        }

        try
        {
            // 1. Validar si el cliente existe (si se envió un IdCliente)
            if (dto.IdCliente.HasValue)
            {
                var clienteExiste = await _context.Clientes.AnyAsync(c => c.Id == dto.IdCliente.Value);
                if (!clienteExiste)
                {
                    return NotFound(new { exito = false, mensaje = $"El cliente con ID {dto.IdCliente.Value} no existe en la base de datos." });
                }
            }

            // 2. Validación preventiva de duplicados (últimos 5 segundos)
            var hacePoco = DateTime.UtcNow.AddSeconds(-5);
            bool ventaDuplicada = await _context.Ventas
                .AnyAsync(v => v.IdCliente == dto.IdCliente && v.Fecha >= hacePoco);

            if (ventaDuplicada)
            {
                return Conflict(new { exito = false, mensaje = "Se ha detectado una venta idéntica procesada hace un momento. Por favor verifique el inventario antes de reintentar." });
            }

            // Normalizamos el método de pago a uno de los tres valores soportados
            var metodoPago = NormalizarMetodoPago(dto.MetodoPago);
            if (metodoPago == null)
            {
                return BadRequest(new { exito = false, mensaje = $"Método de pago no válido: '{dto.MetodoPago}'. Use Efectivo, Transferencia o Datáfono." });
            }

            var productoIds = dto.DetalleVenta.Select(d => d.IdProducto).Distinct().OrderBy(id => id).ToArray();

            // =========================================================================
            // PROTECCIÓN DE NPGSQL & RESILIENCIA:
            // Cada reintento de la estrategia arranca desde cero (contexto limpio, productos
            // recién leídos) para no descontar el stock dos veces ni insertar la venta duplicada.
            // Las filas de productos se bloquean (FOR UPDATE) hasta el commit para que dos cajas
            // vendiendo a la vez no descuenten el mismo stock.
            // =========================================================================
            var strategy = _context.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();

                await using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    // Una sola consulta para todos los productos (anti N+1), ordenada por id para evitar deadlocks
                    var productosDb = await _context.Productos
                        .FromSqlInterpolated($"SELECT * FROM productos WHERE id = ANY({productoIds}) ORDER BY id FOR UPDATE")
                        .ToListAsync();

                    foreach (var itemDto in dto.DetalleVenta)
                    {
                        var producto = productosDb.FirstOrDefault(p => p.Id == itemDto.IdProducto);

                        if (producto == null || !producto.Activo)
                        {
                            await RollbackSeguroAsync(transaction);
                            return (ActionResult<Ventas>)NotFound(new { exito = false, mensaje = $"El producto con ID {itemDto.IdProducto} no existe." });
                        }

                        if (itemDto.Cantidad <= 0)
                        {
                            await RollbackSeguroAsync(transaction);
                            return (ActionResult<Ventas>)BadRequest(new { exito = false, mensaje = $"La cantidad para el producto {producto.Nombre} debe ser mayor a cero." });
                        }

                        // Suma las líneas repetidas del mismo producto para no vender más de lo que hay
                        var cantidadSolicitada = dto.DetalleVenta.Where(d => d.IdProducto == itemDto.IdProducto).Sum(d => d.Cantidad);
                        if (producto.Stock < cantidadSolicitada)
                        {
                            await RollbackSeguroAsync(transaction);
                            return (ActionResult<Ventas>)BadRequest(new { exito = false, mensaje = $"Stock insuficiente para el producto {producto.Nombre}. Stock actual: {producto.Stock}, solicitado: {cantidadSolicitada}" });
                        }
                    }

                    var nuevaVenta = new Ventas
                    {
                        IdCliente = dto.IdCliente,
                        MetodoPago = metodoPago,
                        Fecha = DateTime.UtcNow,
                        DetalleVenta = new List<DetalleVenta>()
                    };

                    decimal valorTotalCalculado = 0;

                    foreach (var itemDto in dto.DetalleVenta)
                    {
                        var producto = productosDb.First(p => p.Id == itemDto.IdProducto);

                        valorTotalCalculado += producto.Precio * itemDto.Cantidad;

                        // Descontamos el stock localmente (el change tracker lo detectará)
                        producto.Stock -= itemDto.Cantidad;

                        if (producto.Stock <= 10)
                        {
                            EnviarSmsAlAdmin(producto.Nombre, producto.Stock);
                        }

                        nuevaVenta.DetalleVenta.Add(new DetalleVenta
                        {
                            IdProducto = itemDto.IdProducto,
                            Cantidad = itemDto.Cantidad,
                            PrecioUnitario = producto.Precio
                        });
                    }

                    nuevaVenta.ValorTotal = valorTotalCalculado;

                    _context.Ventas.Add(nuevaVenta);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return CreatedAtAction(nameof(GetVenta), new { id = nuevaVenta.Id }, new
                    {
                        exito = true,
                        mensaje = "Venta procesada con éxito.",
                        data = nuevaVenta
                    });
                }
                catch
                {
                    await RollbackSeguroAsync(transaction);
                    throw; // Propaga la excepción original para que el strategy la reintente si es transitoria
                }
            });
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
        {
            return BadRequest(new
            {
                exito = false,
                mensaje = "Error de base de datos al procesar la venta.",
                errorInterno = dbEx.InnerException?.Message ?? dbEx.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                exito = false,
                mensaje = "Ocurrió un error inesperado al procesar la venta.",
                errorInterno = ex.InnerException?.Message ?? ex.Message,
                stackTrace = ex.StackTrace
            });
        }
    }

    // GET: api/Ventas/PorFecha?fecha=2026-09-30

    [HttpGet("PorFecha")]
    public async Task<ActionResult> GetVentasPorFecha([FromQuery] string fecha)
    {
        try
        {
            // Intentamos parsear la fecha enviada desde Swagger / URL
            if (!DateTime.TryParse(fecha, out var parsedDate))
            {
                return BadRequest(new
                {
                    exito = false,
                    mensaje = "Formato de fecha inválido. Utilice el formato YYYY-MM-DD (ej: 2026-11-12)."
                });
            }

            // Forzamos estrictamente el Kind a UTC para evitar el error de Npgsql
            var inicioDia = DateTime.SpecifyKind(parsedDate.Date, DateTimeKind.Utc);
            var finDia = DateTime.SpecifyKind(inicioDia.AddDays(1), DateTimeKind.Utc);

            var ventas = await _context.Ventas
                .AsNoTracking()
                .Include(v => v.IdClienteNavigation)
                .Include(v => v.DetalleVenta)
                    .ThenInclude(d => d.IdProductoNavigation)
                .Where(v => v.Fecha >= inicioDia && v.Fecha < finDia)
                .ToListAsync();

            return Ok(new
            {
                exito = true,
                totalRegistros = ventas.Count,
                data = ventas
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR REAL EN DB: {ex.InnerException?.Message ?? ex.Message}");
            return StatusCode(500, new
            {
                exito = false,
                mensaje = "Ocurrió un error al consultar las ventas por fecha.",
                detalle = ex.InnerException?.Message ?? ex.Message
            });
        }
    }
    // GET: api/Ventas/ProductoMasVendido (Histórico General)
    [HttpGet("ProductoMasVendido")]
    public async Task<ActionResult> GetProductoMasVendido()
    {
        var productoMasVendido = await _context.DetalleVenta
            .AsNoTracking()
            .GroupBy(d => d.IdProducto)
            .Select(g => new
            {
                IdProducto = g.Key,
                TotalCantidadVendida = g.Sum(d => d.Cantidad)
            })
            .OrderByDescending(x => x.TotalCantidadVendida)
            .FirstOrDefaultAsync();

        if (productoMasVendido == null)
        {
            return NotFound("No hay registros de ventas suficientes para determinar el producto más vendido.");
        }

        var productoInfo = await _context.Productos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productoMasVendido.IdProducto);

        return Ok(new
        {
            mensaje = "Producto más vendido de la ferretería (General)",
            producto = productoInfo,
            totalUnidadesVendidas = productoMasVendido.TotalCantidadVendida
        });
    }

    // GET: api/Ventas/ProductoMasVendidoDiario?fecha=2026-09-30
    [HttpGet("ProductoMasVendidoDiario")]
    public async Task<ActionResult> GetProductoMasVendidoDiario([FromQuery] DateTime? fecha)
    {
        DateTime fechaBuscada = fecha ?? DateTime.Today;
        // Npgsql exige Kind=UTC para columnas timestamptz (igual que en PorFecha)
        var inicioDia = DateTime.SpecifyKind(fechaBuscada.Date, DateTimeKind.Utc);
        var finDia = inicioDia.AddDays(1).AddTicks(-1);

        var productoMasVendido = await _context.DetalleVenta
            .AsNoTracking()
            .Join(_context.Ventas,
                  d => d.IdVenta,
                  v => v.Id,
                  (d, v) => new { Detalle = d, Venta = v })
            .Where(x => x.Venta.Fecha >= inicioDia && x.Venta.Fecha <= finDia)
            .GroupBy(x => x.Detalle.IdProducto)
            .Select(g => new
            {
                IdProducto = g.Key,
                TotalCantidadVendida = g.Sum(x => x.Detalle.Cantidad)
            })
            .OrderByDescending(x => x.TotalCantidadVendida)
            .FirstOrDefaultAsync();

        if (productoMasVendido == null)
        {
            return NotFound("No hay registros de ventas suficientes para la fecha seleccionada.");
        }

        var productoInfo = await _context.Productos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productoMasVendido.IdProducto);

        return Ok(new
        {
            mensaje = "Producto más vendido del día",
            fechaConsulta = fechaBuscada.ToString("yyyy-MM-dd"),
            producto = productoInfo,
            totalUnidadesVendidas = productoMasVendido.TotalCantidadVendida
        });
    }
    // Si la conexión ya se cayó (timeout de red con Supabase), Npgsql dispone la transacción y
    // RollbackAsync lanza ObjectDisposedException, ocultando el error real. Postgres revierte
    // por su cuenta al perder la conexión, así que aquí basta con ignorar ese fallo.
    private static async Task RollbackSeguroAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Rollback no ejecutado (conexión ya cerrada): {ex.Message}");
        }
    }

    // Devuelve el nombre canónico del método de pago, o null si no es uno de los soportados.
    // Sin método de pago se asume Efectivo (comportamiento anterior).
    private static string? NormalizarMetodoPago(string? metodoPago)
    {
        if (string.IsNullOrWhiteSpace(metodoPago))
        {
            return "Efectivo";
        }

        // Quita tildes (incluida la forma descompuesta "a" + acento) y pasa a minúsculas
        var valor = new string(metodoPago.Trim().ToLowerInvariant()
            .Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray());

        // Se compara por prefijo para tolerar texto mal codificado (ej. "DatÃ¡fono" si el
        // navegador leyó app.js con otro charset). Mismas reglas que el reporte del frontend.
        if (valor.StartsWith("efect")) return "Efectivo";
        if (valor.StartsWith("transf")) return "Transferencia";
        if (valor.StartsWith("dat")) return "Datáfono";
        return null;
    }

    private void EnviarSmsAlAdmin (string nombreProducto, int stockActual)
    {
        string mensaje = $"Stock insuficiente para el producto {nombreProducto}";
    }
}

// DTOs limpios sin duplicidad de nombres
public class VentaCreateDto
{
    public int? IdCliente { get; set; }
    public string? MetodoPago { get; set; }
    public List<DetalleVentaCreateDto> DetalleVenta { get; set; } = new();
}

public class DetalleVentaCreateDto
{
    public int IdProducto { get; set; }
    public int Cantidad { get; set; }
}