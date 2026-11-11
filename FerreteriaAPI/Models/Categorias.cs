using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FerreteriaAPI.Models;

[Table("categoria")]
public partial class Categorias
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("nombrecategoria")]
    public string NombreCategoria { get; set; } = null!;

    [Column("descripcion")]
    public string? Descripcion { get; set; }

    public virtual ICollection<Producto> Productos { get; set; } = new List<Producto>();
}