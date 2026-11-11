using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FerreteriaAPI.Models;

[Table("roles")]
public partial class Roles
{
    [Key]
    [Column("idrol")]
    public int IdRol { get; set; }

    [Column("nombrerol")]
    public string NombreRol { get; set; } = null!;

    [Column("descripcion")]
    public string? Descripcion { get; set; }

    public virtual ICollection<Usuarios> Usuarios { get; set; } = new List<Usuarios>();
}