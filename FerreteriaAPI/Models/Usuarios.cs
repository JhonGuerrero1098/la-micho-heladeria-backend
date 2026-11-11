using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace FerreteriaAPI.Models;

[Table("usuarios")]
public partial class Usuarios
{
    [Key]
    [Column("idusuario")]
    public int IdUsuario { get; set; }

    [Column("nombrecompleto")]
    public string NombreCompleto { get; set; } = null!;

    [Column("email")]
    public string Email { get; set; } = null!;

    [JsonIgnore] // Nunca exponer el hash en las respuestas de la API
    [Column("contrasenahash")]
    public string ContrasenaHash { get; set; } = null!;

    [Column("idrol")]
    public int IdRol { get; set; }

    [Column("estado")]
    public bool? Estado { get; set; }

    [Column("fecharegistro")]
    public DateTime? FechaRegistro { get; set; }

    public virtual Roles IdRolNavigation { get; set; } = null!;
}