using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FerreteriaAPI.Models;

[Table("clientes")]
public partial class Clientes
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("nombre")]
    public string Nombre { get; set; } = null!;

    [Column("telefono")]
    public string? Telefono { get; set; }

    [Column("email")]
    public string? Email { get; set; }

    [Column("cedula")]
    public string Cedula { get; set; } = null!;

    public virtual ICollection<Ventas> Venta { get; set; } = new List<Ventas>();
}