using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FerreteriaAPI.Models;

[Table("clientes")]
public partial class Clientes
{
    [Key]
    public int Id { get; set; }

    public string Nombre { get; set; } = null!;

    public string? Telefono { get; set; }

    public string? Email { get; set; }

    public string Cedula { get; set; } = null!;

    public virtual ICollection<Ventas> Venta { get; set; } = new List<Ventas>();
}
