using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FerreteriaAPI.Models;

[Table("detalleventa")]
public partial class DetalleVenta
{
    [Key]
    [Column("iddetalle")]
    public int IdDetalle { get; set; }

    [Column("idventa")]
    public int IdVenta { get; set; }

    [Column("idproducto")]
    public int IdProducto { get; set; }

    [Column("cantidad")]
    public int Cantidad { get; set; }

    [Column("preciounitario")]
    public decimal PrecioUnitario { get; set; }

    public virtual Producto? IdProductoNavigation { get; set; }

    public virtual Ventas? IdVentaNavigation { get; set; }
}