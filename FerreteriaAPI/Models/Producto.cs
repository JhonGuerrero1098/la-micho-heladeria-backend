using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FerreteriaAPI.Models;

[Table("productos")]
public partial class Producto
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("nombre")]
    public string Nombre { get; set; } = null!;

    [Column("precio")]
    public decimal Precio { get; set; }

    [Column("stock")]
    public int Stock { get; set; }

    [Column("descripcion")]
    public string? Descripcion { get; set; }

    [Column("idcategoria")]
    public int? IdCategoria { get; set; }

    [Column("idproveedor")]
    public int? IdProveedor { get; set; }

    [Column("codigobarras")]
    public string? CodigoBarras { get; set; }

    [Column("activo")]
    public bool Activo { get; set; } = true;

    public virtual ICollection<DetalleVenta> DetalleVenta { get; set; } = new List<DetalleVenta>();

    public virtual Categorias? IdCategoriaNavigation { get; set; }

    public virtual Proveedor? IdProveedorNavigation { get; set; }
}