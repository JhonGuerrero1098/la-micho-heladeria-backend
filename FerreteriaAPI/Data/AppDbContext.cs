using FerreteriaAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace FerreteriaAPI.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Categorias> Categorias { get; set; }
        public DbSet<Producto> Productos { get; set; }
        public DbSet<Clientes> Clientes { get; set; }
        public DbSet<DetalleVenta> DetalleVenta { get; set; }
        public DbSet<Proveedor> Proveedor { get; set; }
        public DbSet<Roles> Roles { get; set; }
        public DbSet<Usuarios> Usuarios { get; set; }
        public DbSet<Ventas> Ventas { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Mapeo exacto de todas las tablas en minúsculas para Supabase / PostgreSQL
            modelBuilder.Entity<Categorias>(entity => { entity.ToTable("categoria"); });
            modelBuilder.Entity<Producto>(entity => { entity.ToTable("productos"); });
            modelBuilder.Entity<Clientes>(entity => { entity.ToTable("clientes"); });
            modelBuilder.Entity<DetalleVenta>(entity => { entity.ToTable("detalleventa"); });
            modelBuilder.Entity<Proveedor>(entity => { entity.ToTable("proveedores"); });
            modelBuilder.Entity<Roles>(entity => { entity.ToTable("roles"); });
            modelBuilder.Entity<Usuarios>(entity => { entity.ToTable("usuarios"); });
            modelBuilder.Entity<Ventas>(entity => { entity.ToTable("ventas"); });
        }
    }
}