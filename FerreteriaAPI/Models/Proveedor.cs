using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FerreteriaAPI.Models;

[Table("proveedores")]
public partial class Proveedor
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("nombreproveedor")]
    public string? NombreProveedor { get; set; }

    [Column("nombrecontacto")]
    public string? NombreContacto { get; set; }

    [Column("telefono")]
    public string? Telefono { get; set; }

    [Column("email")]
    public string? Email { get; set; }

    [Column("direccion")]
    public string? Direccion { get; set; }

    public virtual ICollection<Producto> Productos { get; set; } = new List<Producto>();
}