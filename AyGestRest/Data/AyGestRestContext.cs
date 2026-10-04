using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System;
using System.IO;

namespace AyGestRest.Data
{
    public class AyGestRestContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public DbSet<RestaurantTable> Tables { get; set; }
        public DbSet<Promotion> Promotions { get; set; }
        public DbSet<StockMovement> StockMovements { get; set; }
        public DbSet<FuncionarioTurno> FuncionarioTurnos { get; set; }
        public DbSet<VFDConfig> VFDConfigs { get; set; }
        public DbSet<TaxaIVA> TaxasIVA { get; set; }
        public DbSet<RestaurantTable> RestaurantTables { get; set; }
        public DbSet<DailyClosing> DailyClosings { get; set; }
        public DbSet<ProductCategory> ProductCategories { get; set; }
        public DbSet<DailyStockSnapshot> DailyStockSnapshots { get; set; }
        public DbSet<WhatsAppApiConfig> WhatsAppApiConfigs { get; set; }
        public DbSet<Reservation> Reservations { get; set; }
        public DbSet<Cliente> Clientes { get; set; }
        public DbSet<Entregador> Entregadores { get; set; }
        public DbSet<Ingredient> Ingredients { get; set; }
        public DbSet<Turno> Turnos { get; set; }
        public DbSet<ProductIngredient> ProductIngredients { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<RestaurantConfig> RestaurantConfigs { get; set; }
        public DbSet<HorarioUsuario> HorarioUsuarios { get; set; }
        public DbSet<InventoryMovement> InventoryMovements { get; set; }
        public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<PurchaseOrder> PurchaseOrders { get; set; }
        public DbSet<PurchaseOrderItem> PurchaseOrderItems { get; set; }

        public AyGestRestContext() { }

        public AyGestRestContext(DbContextOptions<AyGestRestContext> options) : base(options) { }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (optionsBuilder.IsConfigured) return;

            string dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AyGestRest",
                "AyGestRest.db");

            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

            optionsBuilder
                .UseSqlite($"Data Source={dbPath};Cache=Shared;Foreign Keys=True;Default Timeout=30")
                .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Username).IsUnique();
                entity.Property(e => e.NomeOrdenacao)
                    .HasComputedColumnSql("CASE WHEN FullName IS NOT NULL AND FullName != '' THEN FullName ELSE Username END");
            });

            modelBuilder.Entity<RestaurantConfig>(entity => entity.HasKey(e => e.Id));
            modelBuilder.Entity<ProductCategory>(entity => entity.HasKey(e => e.Id));

            // Índices que suportam as operações centrais do POS sem alterar os dados existentes.
            modelBuilder.Entity<Product>(entity =>
            {
                entity.HasIndex(e => e.CodigoBarras);
                entity.HasIndex(e => e.CategoriaId);
            });

            modelBuilder.Entity<Order>(entity =>
            {
                entity.HasIndex(e => e.DataHora);
                entity.HasIndex(e => e.Status);
            });

            modelBuilder.Entity<Payment>(entity =>
            {
                entity.HasIndex(e => e.OrderId);
                entity.HasIndex(e => e.DataPagamento);
            });

            modelBuilder.Entity<InventoryMovement>(entity =>
            {
                entity.HasIndex(e => e.ProductId);
                entity.HasIndex(e => e.DataMovimento);
            });

            modelBuilder.Entity<StockMovement>(entity =>
            {
                entity.HasIndex(e => e.ProductId);
                entity.HasIndex(e => e.DataMovimento);
            });

            modelBuilder.Entity<PurchaseOrder>(entity =>
            {
                entity.HasIndex(e => e.SupplierId);
                entity.HasIndex(e => e.DataPedido);
            });
        }
    }
}
