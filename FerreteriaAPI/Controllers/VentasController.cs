using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using FerreteriaAPI.Models;
using FerreteriaAPI.Services;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

[Route("api/[controller]")]
[ApiController]
public class VentasController : ControllerBase
{
    private readonly FerreteriaContext _context;
    private readonly ColaSms _colaSms;
    private readonly IConfiguration _config;
    private readonly ILogger<VentasController> _logger;

    // Límites de una venta: evitan cuerpos gigantes y desbordes al multiplicar precio × cantidad
    private const int MaxLineasPorVenta = 200;
    private const int MaxCantidadPorLinea = 100_000;
    // Umbral de la alerta de stock bajo (SMS al administrador)
    private const int UmbralStockBajo = 10;

    public VentasController(FerreteriaContext context, ColaSms colaSms, IConfiguration config, ILogger<VentasController> logger)
    {
        _context = context;
        _colaSms = colaSms;
        _config = config;
        _logger = logger;
    }

    // Proyección compartida por los GET de ventas: una sola consulta que trae solo las columnas
    // que se muestran (sin entidades completas de cliente/producto). Fecha en hora de Colombia.
    private static readonly Expression<Func<Ventas, VentaDto>> ProyeccionVenta = v => new VentaDto
    {
        Id = v.Id,
        Fecha = AHoraColombia(v.Fecha),
        ValorTotal = v.ValorTotal,
        IdCliente = v.IdCliente,
        MetodoPago = v.MetodoPago,
        IdClienteNavigation = v.IdClienteNavigation == null ? null : new ClienteResumenDto
        {
            Id = v.IdClienteNavigation.Id,
            Nombre = v.IdClienteNavigation.Nombre,
            Cedula = v.IdClienteNavigation.Cedula
        },
        DetalleVenta = v.DetalleVenta.Select(d => new DetalleVentaDto
        {
            IdDetalle = d.IdDetalle,
            IdVenta = d.IdVenta,
            IdProducto = d.IdProducto,
            Cantidad = d.Cantidad,
            PrecioUnitario = d.PrecioUnitario,
            IdProductoNavigation = d.IdProductoNavigation == null ? null : new ProductoResumenDto
            {
                Id = d.IdProductoNavigation.Id,
                Nombre = d.IdProductoNavigation.Nombre,
                Precio = d.IdProductoNavigation.Precio
            }
        }).ToList()
    };

    [HttpGet]
    public async Task<ActionResult<IEnumerable<VentaDto>>> GetVentas()
    {
        return await _context.Ventas
            .AsNoTracking()
            .Select(ProyeccionVenta)
            .ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<VentaDto>> GetVenta(int id)
    {
        var venta = await _context.Ventas
            .AsNoTracking()
            .Where(v => v.Id == id)
            .Select(ProyeccionVenta)
            .FirstOrDefaultAsync();

        if (venta == null)
        {
            return NotFound();
        }

        return venta;
    }
    // POST: api/Ventas
    // Cabecera opcional X-Idempotency-Key (UUID). Si esa clave ya produjo una venta en los últimos
    // 15 minutos, se devuelve la misma respuesta guardada (con Idempotent-Replayed: true) sin volver
    // a ejecutar la transacción: un reenvío por fallo de red nunca duplica la venta ni el descuento de stock.
    [HttpPost]
    public async Task<ActionResult<VentaDto>> PostVenta(
        [FromBody] VentaCreateDto dto,
        [FromHeader(Name = IdempotenciaVentas.Cabecera)] string? claveIdempotenciaTexto)
    {
        // Validación inicial de los detalles
        if (dto.DetalleVenta == null || !dto.DetalleVenta.Any())
        {
            return BadRequest(new { exito = false, mensaje = "La venta debe incluir al menos un producto." });
        }
        if (dto.DetalleVenta.Count > MaxLineasPorVenta)
        {
            return BadRequest(new { exito = false, mensaje = $"Una venta admite como máximo {MaxLineasPorVenta} líneas." });
        }
        // Cantidades validadas ANTES de tocar la base de datos (sin abrir transacción por un error del cliente)
        if (dto.DetalleVenta.Any(d => d.Cantidad <= 0 || d.Cantidad > MaxCantidadPorLinea))
        {
            return BadRequest(new { exito = false, mensaje = $"Cada cantidad debe estar entre 1 y {MaxCantidadPorLinea}." });
        }

        Guid? claveIdempotencia = null;
        if (!string.IsNullOrWhiteSpace(claveIdempotenciaTexto))
        {
            if (!Guid.TryParse(claveIdempotenciaTexto, out var claveParseada) || claveParseada == Guid.Empty)
            {
                return BadRequest(new { exito = false, mensaje = $"La cabecera {IdempotenciaVentas.Cabecera} debe ser un UUID válido." });
            }
            claveIdempotencia = claveParseada;
        }

        // Normalizamos el método de pago a uno de los tres valores soportados
        var metodoPago = NormalizarMetodoPago(dto.MetodoPago);
        if (metodoPago == null)
        {
            return BadRequest(new { exito = false, mensaje = $"Método de pago no válido: '{dto.MetodoPago}'. Use Efectivo, Transferencia o Datáfono." });
        }

        var firmaSolicitud = FirmaDetalle(dto.DetalleVenta.Select(d => (d.IdProducto, d.Cantidad)));
        var huellaSolicitud = CalcularHuella(dto.IdCliente, metodoPago, firmaSolicitud);

        try
        {
            // (Ya no hay consulta previa de "camino rápido" para la clave de idempotencia: el INSERT ...
            //  ON CONFLICT dentro de la transacción detecta igual la repetición, y así cada venta normal
            //  se ahorra un viaje de ida y vuelta a Supabase.)

            // 1. Validar si el cliente existe (si se envió un IdCliente). Se guarda su resumen para
            //    devolverlo en la respuesta, y su teléfono para el SMS, sin otra consulta.
            ClienteResumenDto? clienteResumen = null;
            string? telefonoCliente = null;
            if (dto.IdCliente.HasValue)
            {
                var cliente = await _context.Clientes
                    .AsNoTracking()
                    .Where(c => c.Id == dto.IdCliente.Value)
                    .Select(c => new { c.Id, c.Nombre, c.Cedula, c.Telefono })
                    .FirstOrDefaultAsync();
                if (cliente == null)
                {
                    return NotFound(new { exito = false, mensaje = $"El cliente con ID {dto.IdCliente.Value} no existe en la base de datos." });
                }
                clienteResumen = new ClienteResumenDto { Id = cliente.Id, Nombre = cliente.Nombre, Cedula = cliente.Cedula };
                telefonoCliente = cliente.Telefono;
            }

            // 2. Solo para clientes que NO envían clave (versiones anteriores del frontend): heurística de
            //    duplicados de 5 segundos con el mismo cliente y los mismos productos/cantidades.
            //    Con clave, la idempotencia ya cubre los reenvíos y esta regla bloquearía ventas legítimas.
            if (!claveIdempotencia.HasValue)
            {
                var hacePoco = DateTime.UtcNow.AddSeconds(-5);
                var detallesRecientes = await _context.Ventas
                    .AsNoTracking()
                    .Where(v => v.IdCliente == dto.IdCliente && v.Fecha >= hacePoco)
                    .Select(v => v.DetalleVenta.Select(d => new { d.IdProducto, d.Cantidad }).ToList())
                    .ToListAsync();
                bool ventaDuplicada = detallesRecientes
                    .Any(r => FirmaDetalle(r.Select(d => (d.IdProducto, d.Cantidad))) == firmaSolicitud);

                if (ventaDuplicada)
                {
                    return Conflict(new { exito = false, mensaje = "Se ha detectado una venta idéntica procesada hace un momento. Por favor verifique el inventario antes de reintentar." });
                }
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

            // Datos para los SMS: solo se llenan justo después de un COMMIT exitoso. Una repetición
            // idempotente o un error lo dejan en null: nunca se notifica una venta no guardada ni se
            // duplica el SMS de una venta repetida.
            (int IdVenta, decimal Total, List<(string Nombre, int Stock)> StockBajo)? ventaConfirmada = null;

            Func<Task<ActionResult<VentaDto>>> procesarVenta = async () =>
            {
                _context.ChangeTracker.Clear();
                ventaConfirmada = null;

                await using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    // Reserva de la clave dentro de la MISMA transacción de la venta:
                    // - Si otra petición con la misma clave está en curso, este INSERT espera a que termine.
                    // - Si esa venta ya se confirmó (incluido un reintento de la estrategia después de un
                    //   commit cuya confirmación se perdió por la red), el INSERT no inserta nada y se
                    //   devuelve la respuesta guardada en lugar de vender otra vez.
                    if (claveIdempotencia.HasValue)
                    {
                        // Una sola sentencia (antes DELETE + INSERT, dos viajes): inserta la clave, o reutiliza
                        // la fila si está vencida. Si la clave sigue vigente no toca nada y devuelve 0.
                        var ahora = DateTime.UtcNow;
                        var limite = ahora - IdempotenciaVentas.Vigencia;
                        var reservada = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                            INSERT INTO idempotencia_ventas (clave, huella, creado_en)
                            VALUES ({claveIdempotencia.Value}, {huellaSolicitud}, {ahora})
                            ON CONFLICT (clave) DO UPDATE
                                SET huella = EXCLUDED.huella, creado_en = EXCLUDED.creado_en,
                                    id_venta = NULL, codigo_estado = NULL, respuesta = NULL
                                WHERE idempotencia_ventas.creado_en < {limite}");

                        if (reservada == 0)
                        {
                            await RollbackSeguroAsync(transaction);
                            var registroPrevio = await BuscarClaveVigenteAsync(claveIdempotencia.Value);
                            return registroPrevio != null
                                ? RepetirRespuesta(registroPrevio, huellaSolicitud)
                                : (ActionResult<VentaDto>)Conflict(new { exito = false, mensaje = "Esta venta se está procesando en otra petición. Espere un momento y verifique en Ventas del Día." });
                        }
                    }

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
                            return (ActionResult<VentaDto>)NotFound(new { exito = false, mensaje = $"El producto con ID {itemDto.IdProducto} no existe." });
                        }

                        // Suma las líneas repetidas del mismo producto para no vender más de lo que hay
                        var cantidadSolicitada = dto.DetalleVenta.Where(d => d.IdProducto == itemDto.IdProducto).Sum(d => d.Cantidad);
                        if (producto.Stock < cantidadSolicitada)
                        {
                            await RollbackSeguroAsync(transaction);
                            return (ActionResult<VentaDto>)BadRequest(new { exito = false, mensaje = $"Stock insuficiente para el producto {producto.Nombre}. Stock actual: {producto.Stock}, solicitado: {cantidadSolicitada}" });
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
                    var stockInicial = productosDb.ToDictionary(p => p.Id, p => p.Stock);

                    foreach (var itemDto in dto.DetalleVenta)
                    {
                        var producto = productosDb.First(p => p.Id == itemDto.IdProducto);

                        // Precio SIEMPRE de la base de datos (fila bloqueada FOR UPDATE), nunca del frontend
                        valorTotalCalculado += producto.Precio * itemDto.Cantidad;

                        // Descontamos el stock localmente (el change tracker lo detectará)
                        producto.Stock -= itemDto.Cantidad;

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

                    // Respuesta armada en memoria, misma forma que GET api/Ventas/{id}, en lugar de
                    // serializar la entidad rastreada con todo su grafo de navegaciones.
                    var ventaRespuesta = new VentaDto
                    {
                        Id = nuevaVenta.Id,
                        Fecha = AHoraColombia(nuevaVenta.Fecha),
                        ValorTotal = nuevaVenta.ValorTotal,
                        IdCliente = nuevaVenta.IdCliente,
                        MetodoPago = nuevaVenta.MetodoPago,
                        IdClienteNavigation = clienteResumen,
                        DetalleVenta = nuevaVenta.DetalleVenta.Select(d =>
                        {
                            var producto = productosDb.First(p => p.Id == d.IdProducto);
                            return new DetalleVentaDto
                            {
                                IdDetalle = d.IdDetalle,
                                IdVenta = nuevaVenta.Id,
                                IdProducto = d.IdProducto,
                                Cantidad = d.Cantidad,
                                PrecioUnitario = d.PrecioUnitario,
                                IdProductoNavigation = new ProductoResumenDto { Id = producto.Id, Nombre = producto.Nombre, Precio = producto.Precio }
                            };
                        }).ToList()
                    };

                    var cuerpoRespuesta = new
                    {
                        exito = true,
                        mensaje = "Venta procesada con éxito.",
                        data = ventaRespuesta,
                        // Stock ya descontado: el POS lo aplica de inmediato sin esperar a recargar productos
                        productosActualizados = productosDb.Select(p => new { id = p.Id, stock = p.Stock }).ToList()
                    };

                    // Se guarda la respuesta junto a la clave ANTES del commit: venta y clave se confirman juntas
                    if (claveIdempotencia.HasValue)
                    {
                        var jsonRespuesta = JsonSerializer.Serialize(cuerpoRespuesta, OpcionesJson);
                        await _context.Database.ExecuteSqlInterpolatedAsync(
                            $"UPDATE idempotencia_ventas SET id_venta = {nuevaVenta.Id}, codigo_estado = {StatusCodes.Status201Created}, respuesta = {jsonRespuesta} WHERE clave = {claveIdempotencia.Value}");
                    }

                    await transaction.CommitAsync();

                    // Alerta solo para los productos que CRUZARON el umbral con esta venta (no en cada venta
                    // posterior), para no saturar al administrador con el mismo aviso.
                    var stockBajo = productosDb
                        .Where(p => p.Stock <= UmbralStockBajo && stockInicial[p.Id] > UmbralStockBajo)
                        .Select(p => (p.Nombre, p.Stock))
                        .ToList();
                    ventaConfirmada = (nuevaVenta.Id, nuevaVenta.ValorTotal, stockBajo);

                    return CreatedAtAction(nameof(GetVenta), new { id = nuevaVenta.Id }, cuerpoRespuesta);
                }
                catch
                {
                    await RollbackSeguroAsync(transaction);
                    throw; // Propaga la excepción original para que el strategy la reintente si es transitoria
                }
            };

            ActionResult<VentaDto> resultado;
            try
            {
                resultado = await strategy.ExecuteAsync(procesarVenta);
            }
            catch (DbUpdateException dbEx) when (SecuenciasIds.EsLlavePrimariaDuplicada(dbEx))
            {
                // Secuencia de ventas.id o detalleventa.iddetalle por detrás de los datos migrados: la
                // transacción ya se revirtió por completo (venta, detalle, stock y clave de idempotencia),
                // así que se sincronizan ambas secuencias y se procesa la venta una sola vez más.
                await SecuenciasIds.SincronizarAsync(_context, "ventas");
                await SecuenciasIds.SincronizarAsync(_context, "detalleventa");
                resultado = await strategy.ExecuteAsync(procesarVenta);
            }

            // La venta ya está confirmada en la base: los SMS solo se ENCOLAN (no se espera a Twilio)
            if (ventaConfirmada.HasValue)
            {
                var (idVenta, total, stockBajo) = ventaConfirmada.Value;
                EncolarSmsDeVenta(idVenta, total, metodoPago, telefonoCliente, stockBajo);
            }
            return resultado;
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
        {
            // El detalle técnico (tablas, columnas, restricciones) va a los logs de Render, no al navegador
            _logger.LogError(dbEx, "Error de base de datos al procesar la venta");
            return BadRequest(new { exito = false, mensaje = "Error de base de datos al procesar la venta." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al procesar la venta");
            return StatusCode(500, new { exito = false, mensaje = "Ocurrió un error inesperado al procesar la venta." });
        }
    }

    // Nunca lanza: un fallo aquí no debe convertir en error una venta que ya se guardó
    private void EncolarSmsDeVenta(int idVenta, decimal total, string metodoPago, string? telefonoCliente,
                                   List<(string Nombre, int Stock)> stockBajo)
    {
        try
        {
            var totalTexto = total.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("es-CO"));
            if (!string.IsNullOrWhiteSpace(telefonoCliente))
            {
                _colaSms.Encolar(telefonoCliente,
                    $"La Micho Heladeria: pago recibido. Factura #{idVenta} por ${totalTexto} ({metodoPago}). Gracias por tu compra!",
                    $"confirmación venta #{idVenta}");
            }
            else
            {
                _logger.LogInformation("Venta #{IdVenta} sin cliente o sin teléfono: no se envía SMS de confirmación", idVenta);
            }

            var telefonoAdmin = _config["Sms:TelefonoAdmin"];
            if (stockBajo.Count > 0 && !string.IsNullOrWhiteSpace(telefonoAdmin))
            {
                var lista = string.Join(", ", stockBajo.Select(p => $"{p.Nombre} ({p.Stock})"));
                var texto = $"La Micho: stock bajo tras la venta #{idVenta}: {lista}";
                _colaSms.Encolar(telefonoAdmin, texto.Length > 300 ? texto[..297] + "..." : texto, "alerta stock bajo");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudieron encolar los SMS de la venta #{IdVenta}", idVenta);
        }
    }

    // GET: api/Ventas/PorFecha?fecha=2026-09-30  (sin fecha = hoy en Colombia)
    // Devuelve un arreglo plano de ventas (vacío si no hay), que es lo que espera el frontend.
    [HttpGet("PorFecha")]
    public async Task<ActionResult> GetVentasPorFecha([FromQuery] string? fecha)
    {
        try
        {
            DateTime diaLocal;
            if (string.IsNullOrWhiteSpace(fecha))
            {
                diaLocal = HoyEnColombia();
            }
            else if (!DateTime.TryParse(fecha, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out diaLocal))
            {
                return BadRequest(new
                {
                    exito = false,
                    mensaje = "Formato de fecha inválido. Utilice el formato YYYY-MM-DD (ej: 2026-11-12)."
                });
            }

            var (inicioDia, finDia) = RangoDiaUtc(diaLocal);

            // Proyección directa: solo las columnas que usa el reporte, sin cargar entidades completas
            var ventas = await _context.Ventas
                .AsNoTracking()
                .Where(v => v.Fecha >= inicioDia && v.Fecha < finDia)
                .OrderBy(v => v.Fecha)
                .Select(ProyeccionVenta)
                .ToListAsync();

            return Ok(ventas);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al consultar las ventas por fecha");
            return StatusCode(500, new
            {
                exito = false,
                mensaje = "Ocurrió un error al consultar las ventas por fecha."
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
            .Take(1)
            // Se une con productos en la misma consulta (antes eran dos viajes a la base)
            .Join(_context.Productos,
                  x => x.IdProducto,
                  p => p.Id,
                  (x, p) => new
                  {
                      Producto = new ProductoResumenDto { Id = p.Id, Nombre = p.Nombre, Precio = p.Precio },
                      x.TotalCantidadVendida
                  })
            .FirstOrDefaultAsync();

        if (productoMasVendido == null)
        {
            return NotFound("No hay registros de ventas suficientes para determinar el producto más vendido.");
        }

        return Ok(new
        {
            mensaje = "Producto más vendido de La Micho Heladería (General)",
            producto = productoMasVendido.Producto,
            totalUnidadesVendidas = productoMasVendido.TotalCantidadVendida
        });
    }

    // GET: api/Ventas/ProductoMasVendidoDiario?fecha=2026-09-30
    [HttpGet("ProductoMasVendidoDiario")]
    public async Task<ActionResult> GetProductoMasVendidoDiario([FromQuery] DateTime? fecha)
    {
        DateTime fechaBuscada = fecha ?? HoyEnColombia();
        var (inicioDia, finDiaExclusivo) = RangoDiaUtc(fechaBuscada);
        var finDia = finDiaExclusivo.AddTicks(-1);

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
            .Take(1)
            // Se une con productos en la misma consulta (antes eran dos viajes a la base)
            .Join(_context.Productos,
                  x => x.IdProducto,
                  p => p.Id,
                  (x, p) => new
                  {
                      Producto = new ProductoResumenDto { Id = p.Id, Nombre = p.Nombre, Precio = p.Precio },
                      x.TotalCantidadVendida
                  })
            .FirstOrDefaultAsync();

        if (productoMasVendido == null)
        {
            return NotFound("No hay registros de ventas suficientes para la fecha seleccionada.");
        }

        return Ok(new
        {
            mensaje = "Producto más vendido del día",
            fechaConsulta = fechaBuscada.ToString("yyyy-MM-dd"),
            producto = productoMasVendido.Producto,
            totalUnidadesVendidas = productoMasVendido.TotalCantidadVendida
        });
    }
    // =========================================================================
    // ZONA HORARIA: las ventas se guardan en UTC, pero "el día" del negocio es el de Colombia
    // (UTC-5, sin horario de verano). Si el sistema no trae la base de zonas, se usa -5 fijo.
    // =========================================================================
    private static readonly TimeZoneInfo ZonaColombia = ObtenerZonaColombia();

    private static TimeZoneInfo ObtenerZonaColombia()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");
        }
        catch (Exception)
        {
            return TimeZoneInfo.CreateCustomTimeZone("Colombia", TimeSpan.FromHours(-5), "Colombia", "Colombia");
        }
    }

    private static DateTime HoyEnColombia() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ZonaColombia).Date;

    // Convierte un día local de Colombia al rango UTC [inicio, fin) que Npgsql acepta (Kind=UTC)
    private static (DateTime inicioUtc, DateTime finUtc) RangoDiaUtc(DateTime diaLocal)
    {
        var inicioLocal = DateTime.SpecifyKind(diaLocal.Date, DateTimeKind.Unspecified);
        var inicioUtc = TimeZoneInfo.ConvertTimeToUtc(inicioLocal, ZonaColombia);
        return (inicioUtc, inicioUtc.AddDays(1));
    }

    // Hora local de Colombia para mostrar en el reporte (el frontend toma la hora del texto de la fecha)
    private static DateTime AHoraColombia(DateTime fechaUtc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(fechaUtc, DateTimeKind.Utc), ZonaColombia);

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

    // =========================================================================
    // IDEMPOTENCIA
    // =========================================================================
    private JsonSerializerOptions OpcionesJson =>
        HttpContext.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;

    // Registro con respuesta ya guardada y dentro de la vigencia (las claves vencidas se ignoran
    // aunque la limpieza periódica todavía no las haya borrado)
    private Task<IdempotenciaVenta?> BuscarClaveVigenteAsync(Guid clave)
    {
        var limite = DateTime.UtcNow - IdempotenciaVentas.Vigencia;
        return _context.IdempotenciaVentas
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Clave == clave && i.CreadoEn >= limite && i.CodigoEstado != null);
    }

    private ActionResult RepetirRespuesta(IdempotenciaVenta registro, string huellaSolicitud)
    {
        // Misma clave con otro carrito/cliente/método de pago: es un error del cliente, no un reenvío
        if (registro.Huella != huellaSolicitud)
        {
            return UnprocessableEntity(new
            {
                exito = false,
                mensaje = $"La clave {IdempotenciaVentas.Cabecera} ya se usó para una venta con otros datos. Genere una clave nueva para esta venta."
            });
        }

        Response.Headers[IdempotenciaVentas.CabeceraRepeticion] = "true";
        if (registro.IdVenta.HasValue)
        {
            Response.Headers.Location = Url.Action(nameof(GetVenta), new { id = registro.IdVenta.Value });
        }

        return new ContentResult
        {
            StatusCode = registro.CodigoEstado,
            ContentType = "application/json; charset=utf-8",
            Content = registro.Respuesta
        };
    }

    private static string CalcularHuella(int? idCliente, string metodoPago, string firmaDetalle)
    {
        var texto = $"{idCliente?.ToString() ?? "-"}|{metodoPago}|{firmaDetalle}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto)));
    }

    // Firma de una venta: productos con sus cantidades sumadas, ordenados (independiente del orden de las líneas)
    private static string FirmaDetalle(IEnumerable<(int IdProducto, int Cantidad)> lineas) =>
        string.Join(";", lineas
            .GroupBy(l => l.IdProducto)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key}x{g.Sum(l => l.Cantidad)}"));
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