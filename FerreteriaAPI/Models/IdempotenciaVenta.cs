using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FerreteriaAPI.Models;

// Registro de una clave de idempotencia de POST api/Ventas (tabla idempotencia_ventas).
// Se inserta en la MISMA transacción que la venta: si la venta se confirma, la clave queda
// guardada con la respuesta; si se revierte, la clave desaparece con ella.
[Table("idempotencia_ventas")]
public class IdempotenciaVenta
{
    [Key]
    [Column("clave")]
    public Guid Clave { get; set; }

    // Hash del contenido de la petición (cliente, método de pago y productos/cantidades): la misma
    // clave con un carrito distinto se rechaza en lugar de devolver la venta anterior.
    [Column("huella")]
    public string Huella { get; set; } = null!;

    [Column("id_venta")]
    public int? IdVenta { get; set; }

    [Column("codigo_estado")]
    public int? CodigoEstado { get; set; }

    // Cuerpo JSON exacto que se respondió la primera vez
    [Column("respuesta")]
    public string? Respuesta { get; set; }

    [Column("creado_en")]
    public DateTime CreadoEn { get; set; }
}

public partial class FerreteriaContext
{
    public virtual DbSet<IdempotenciaVenta> IdempotenciaVentas { get; set; }
}
