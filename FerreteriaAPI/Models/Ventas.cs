using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FerreteriaAPI.Models;

[Table("ventas")]
public partial class Ventas
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("fecha")]
    public DateTime Fecha { get; set; }

    [Column("valortotal")]
    public decimal ValorTotal { get; set; }

    [Column("idcliente")]
    public int? IdCliente { get; set; }

    [Column("metodopago")]
    public string? MetodoPago { get; set; }

    public virtual ICollection<DetalleVenta> DetalleVenta { get; set; } = new List<DetalleVenta>();

    public virtual Clientes? IdClienteNavigation { get; set; }
}