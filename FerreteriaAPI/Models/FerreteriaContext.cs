using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using FerreteriaAPI.Models;

namespace FerreteriaAPI.Models;

public partial class FerreteriaContext : DbContext
{
    public FerreteriaContext()
    {
    }

    public FerreteriaContext(DbContextOptions<FerreteriaContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Categorias> Categoria { get; set; }
    public virtual DbSet<Clientes> Clientes { get; set; }
    public virtual DbSet<DetalleVenta> DetalleVenta { get; set; }
    public virtual DbSet<Producto> Productos { get; set; }
    public virtual DbSet<Proveedor> Proveedores { get; set; }
    public virtual DbSet<Roles> Roles { get; set; }
    public virtual DbSet<Usuarios> Usuarios { get; set; }
    public virtual DbSet<Ventas> Ventas { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Mapeo estricto de nombres de tablas en minúsculas para PostgreSQL (Supabase)

        modelBuilder.Entity<Categorias>(entity =>
        {
            entity.ToTable("categoria"); // <-- Forzamos minúscula
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.NombreCategoria).HasColumnName("nombrecategoria").HasMaxLength(100).IsUnicode(false);
            entity.Property(e => e.Descripcion).HasColumnName("descripcion").HasMaxLength(255).IsUnicode(false);
        });

        modelBuilder.Entity<Clientes>(entity =>
        {
            entity.ToTable("clientes"); // <-- Forzamos minúscula
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Cedula).HasColumnName("cedula").HasMaxLength(20).IsUnicode(false);
            entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(100).IsUnicode(false);
            entity.Property(e => e.Nombre).HasColumnName("nombre").HasMaxLength(100).IsUnicode(false);
            entity.Property(e => e.Telefono).HasColumnName("telefono").HasMaxLength(20).IsUnicode(false);
        });

        modelBuilder.Entity<DetalleVenta>(entity =>
        {
            entity.ToTable("detalleventa"); // <-- Forzamos minúscula
            entity.HasKey(e => e.IdDetalle);

            entity.Property(e => e.IdDetalle).HasColumnName("iddetalle");
            entity.Property(e => e.IdVenta).HasColumnName("idventa");
            entity.Property(e => e.IdProducto).HasColumnName("idproducto");
            entity.Property(e => e.Cantidad).HasColumnName("cantidad");
            entity.Property(e => e.PrecioUnitario).HasColumnName("preciounitario").HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.IdProductoNavigation).WithMany(p => p.DetalleVenta)
                .HasForeignKey(d => d.IdProducto)
                .OnDelete(DeleteBehavior.ClientSetNull);

            entity.HasOne(d => d.IdVentaNavigation).WithMany(p => p.DetalleVenta)
                .HasForeignKey(d => d.IdVenta);
        });

        modelBuilder.Entity<Producto>(entity =>
        {
            entity.ToTable("productos"); // <-- Forzamos minúscula
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Nombre).HasColumnName("nombre").HasMaxLength(100).IsUnicode(false);
            entity.Property(e => e.Precio).HasColumnName("precio").HasColumnType("decimal(18, 2)");
            entity.Property(e => e.Stock).HasColumnName("stock");
            entity.Property(e => e.Descripcion).HasColumnName("descripcion").HasMaxLength(250).IsUnicode(false);
            entity.Property(e => e.IdCategoria).HasColumnName("idcategoria");
            entity.Property(e => e.IdProveedor).HasColumnName("idproveedor");
            entity.Property(e => e.CodigoBarras).HasColumnName("codigobarras").HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.Activo).HasColumnName("activo");

            entity.HasOne(d => d.IdCategoriaNavigation).WithMany(p => p.Productos)
                .HasForeignKey(d => d.IdCategoria);

            entity.HasOne(d => d.IdProveedorNavigation).WithMany(p => p.Productos)
                .HasForeignKey(d => d.IdProveedor);
        });

        modelBuilder.Entity<Proveedor>(entity =>
        {
            entity.ToTable("proveedores"); // <-- Forzamos minúscula
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.NombreProveedor).HasColumnName("nombreproveedor").HasMaxLength(100).IsUnicode(false);
            entity.Property(e => e.NombreContacto).HasColumnName("nombrecontacto").HasMaxLength(100).IsUnicode(false);
            entity.Property(e => e.Telefono).HasColumnName("telefono").HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(100).IsUnicode(false);
            entity.Property(e => e.Direccion).HasColumnName("direccion").HasMaxLength(150).IsUnicode(false);
        });

        modelBuilder.Entity<Roles>(entity =>
        {
            entity.ToTable("roles"); // <-- Forzamos minúscula
            entity.HasKey(e => e.IdRol);

            entity.Property(e => e.IdRol).HasColumnName("idrol");
            entity.Property(e => e.NombreRol).HasColumnName("nombrerol").HasMaxLength(150).IsUnicode(false);
            entity.Property(e => e.Descripcion).HasColumnName("descripcion").HasMaxLength(150).IsUnicode(false);
        });

        modelBuilder.Entity<Usuarios>(entity =>
        {
            entity.ToTable("usuarios"); // <-- Forzamos minúscula
            entity.HasKey(e => e.IdUsuario);

            entity.Property(e => e.IdUsuario).HasColumnName("idusuario");
            entity.Property(e => e.NombreCompleto).HasColumnName("nombrecompleto").HasMaxLength(100).IsUnicode(false);
            entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(100).IsUnicode(false);
            entity.Property(e => e.ContrasenaHash).HasColumnName("contrasenahash").HasMaxLength(250).IsUnicode(false);
            entity.Property(e => e.IdRol).HasColumnName("idrol");
            entity.Property(e => e.Estado).HasColumnName("estado");
            entity.Property(e => e.FechaRegistro).HasColumnName("fecharegistro");

            entity.HasOne(d => d.IdRolNavigation).WithMany(p => p.Usuarios)
                .HasForeignKey(d => d.IdRol)
                .OnDelete(DeleteBehavior.ClientSetNull);
        });

        modelBuilder.Entity<Ventas>(entity =>
        {
            entity.ToTable("ventas"); // <-- Forzamos minúscula
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Fecha).HasColumnName("fecha");
            entity.Property(e => e.ValorTotal).HasColumnName("valortotal").HasColumnType("decimal(18, 2)");
            entity.Property(e => e.IdCliente).HasColumnName("idcliente");
            entity.Property(e => e.MetodoPago).HasColumnName("metodopago");

            entity.HasOne(d => d.IdClienteNavigation).WithMany(p => p.Venta)
                .HasForeignKey(d => d.IdCliente)
                .OnDelete(DeleteBehavior.ClientSetNull);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}