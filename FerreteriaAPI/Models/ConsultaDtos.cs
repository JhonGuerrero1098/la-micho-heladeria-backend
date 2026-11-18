namespace FerreteriaAPI.Models
{
    // DTOs ligeros de solo lectura para los GET. Se llenan con proyecciones .Select() para que
    // la base devuelva únicamente estas columnas. Los nombres de propiedades coinciden con los de
    // las entidades, así el JSON conserva las mismas claves que ya usa el frontend.

    public class ProductoResumenDto
    {
        public int Id { get; init; }
        public string Nombre { get; init; } = null!;
        public decimal Precio { get; init; }
    }

    public class ClienteResumenDto
    {
        public int Id { get; init; }
        public string Nombre { get; init; } = null!;
        public string Cedula { get; init; } = null!;
    }

    public class CategoriaResumenDto
    {
        public int Id { get; init; }
        public string NombreCategoria { get; init; } = null!;
        public string? Descripcion { get; init; }
    }

    public class ProductoDto
    {
        public int Id { get; init; }
        public string Nombre { get; init; } = null!;
        public decimal Precio { get; init; }
        public int Stock { get; init; }
        public string? Descripcion { get; init; }
        public int? IdCategoria { get; init; }
        public int? IdProveedor { get; init; }
        public string? CodigoBarras { get; init; }
        public bool Activo { get; init; }
        public CategoriaResumenDto? IdCategoriaNavigation { get; init; }
    }

    public class ClienteDto
    {
        public int Id { get; init; }
        public string Nombre { get; init; } = null!;
        public string? Telefono { get; init; }
        public string? Email { get; init; }
        public string Cedula { get; init; } = null!;
    }

    public class UsuarioDto
    {
        public int IdUsuario { get; init; }
        public string NombreCompleto { get; init; } = null!;
        public string Email { get; init; } = null!;
        public int IdRol { get; init; }
        public bool? Estado { get; init; }
        public DateTime? FechaRegistro { get; init; }
    }

    public class DetalleVentaDto
    {
        public int IdDetalle { get; init; }
        public int IdVenta { get; init; }
        public int IdProducto { get; init; }
        public int Cantidad { get; init; }
        public decimal PrecioUnitario { get; init; }
        public ProductoResumenDto? IdProductoNavigation { get; init; }
    }

    public class VentaDto
    {
        public int Id { get; init; }
        public DateTime Fecha { get; init; }
        public decimal ValorTotal { get; init; }
        public int? IdCliente { get; init; }
        public string? MetodoPago { get; init; }
        public ClienteResumenDto? IdClienteNavigation { get; init; }
        public List<DetalleVentaDto> DetalleVenta { get; init; } = new();
    }
}
