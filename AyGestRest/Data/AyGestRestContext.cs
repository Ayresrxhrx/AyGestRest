using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Reflection;
using System.Diagnostics;

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

        public AyGestRestContext()
        {
            // Construtor vazio para facilitar uso em code-behind
        }

        public AyGestRestContext(DbContextOptions<AyGestRestContext> options)
            : base(options)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                string dbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AyGestRest",
                    "AyGestRest.db");

                Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

                optionsBuilder
                    .UseSqlite($"Data Source={dbPath};Cache=Shared")
                    .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
                    .EnableSensitiveDataLogging()
                    .EnableDetailedErrors();
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ========== CONFIGURAÇÕES DE TABELAS ==========

            // Configuração da tabela Users
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Username).IsUnique();
                entity.Property(e => e.NomeOrdenacao)
                    .HasComputedColumnSql("CASE WHEN FullName IS NOT NULL AND FullName != '' THEN FullName ELSE Username END");
            });

            // Configuração da tabela RestaurantConfigs
            modelBuilder.Entity<RestaurantConfig>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            // Configuração da tabela ProductCategories
            modelBuilder.Entity<ProductCategory>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            // Configuração da tabela Ingredients
            modelBuilder.Entity<Ingredient>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            // Configuração da tabela Products
            modelBuilder.Entity<Product>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Code).IsUnique().HasFilter("[Code] IS NOT NULL");
                entity.HasOne(e => e.Category)
                    .WithMany()
                    .HasForeignKey(e => e.CategoryId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.Property(e => e.Active).HasDefaultValue(true);
                entity.Property(e => e.Stock).HasDefaultValue(0);
                entity.Property(e => e.IsComposite).HasDefaultValue(false);
                entity.Property(e => e.PrintToKitchen).HasDefaultValue(true);
            });

            // No método OnModelCreating, configure ProductIngredients assim:
            modelBuilder.Entity<ProductIngredient>(entity =>
            {
                entity.HasKey(e => new { e.ProductId, e.IngredientId }); // Chave composta (ProductId + IngredientId)
                entity.HasIndex(e => new { e.ProductId, e.IngredientId }).IsUnique();
                entity.HasOne(e => e.Product)
                    .WithMany(p => p.ProductIngredients)
                    .HasForeignKey(e => e.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Ingredient)
                    .WithMany(i => i.ProductIngredients)
                    .HasForeignKey(e => e.IngredientId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.Property(e => e.QuantityUsed).HasDefaultValue(1);
                entity.Navigation(e => e.Ingredient).AutoInclude();
            });
            // Configuração da tabela Suppliers
            modelBuilder.Entity<Supplier>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.NIF).IsUnique().HasFilter("[NIF] IS NOT NULL");
            });

            // Configuração da tabela PurchaseOrders
            modelBuilder.Entity<PurchaseOrder>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.PurchaseNumber).IsUnique();
                entity.HasOne(e => e.Supplier)
                    .WithMany(s => s.PurchaseOrders)
                    .HasForeignKey(e => e.SupplierId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Configuração da tabela PurchaseOrderItems
            modelBuilder.Entity<PurchaseOrderItem>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasOne(e => e.PurchaseOrder)
                    .WithMany(po => po.Items)
                    .HasForeignKey(e => e.PurchaseOrderId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Product)
                    .WithMany()
                    .HasForeignKey(e => e.ProductId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Configuração da tabela InventoryMovements
            modelBuilder.Entity<InventoryMovement>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.CreatedAt);
                entity.HasOne(e => e.Product)
                    .WithMany()
                    .HasForeignKey(e => e.ProductId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // Configuração da tabela Orders
            modelBuilder.Entity<Order>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasMany(e => e.Items)
                    .WithOne(i => i.Order)
                    .HasForeignKey(i => i.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Configuração da tabela OrderItems
            modelBuilder.Entity<OrderItem>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasOne(e => e.Product)
                    .WithMany()
                    .HasForeignKey(e => e.ProductId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Configuração das outras tabelas
            modelBuilder.Entity<RestaurantTable>(entity => entity.HasKey(e => e.Id));
            modelBuilder.Entity<Promotion>(entity => entity.HasKey(e => e.Id));
            modelBuilder.Entity<StockMovement>(entity => entity.HasKey(e => e.Id));
            modelBuilder.Entity<FuncionarioTurno>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
            modelBuilder.Entity<VFDConfig>(entity => entity.HasKey(e => e.Id));
            modelBuilder.Entity<TaxaIVA>(entity => entity.HasKey(e => e.Id));
            modelBuilder.Entity<DailyClosing>(entity => entity.HasKey(e => e.Id));
            modelBuilder.Entity<DailyStockSnapshot>(entity => entity.HasKey(e => e.Id));
            modelBuilder.Entity<WhatsAppApiConfig>(entity => entity.HasKey(e => e.Id));
            modelBuilder.Entity<Reservation>(entity => entity.HasKey(e => e.Id));
            modelBuilder.Entity<Cliente>(entity => entity.HasKey(e => e.Id));
            modelBuilder.Entity<Entregador>(entity => entity.HasKey(e => e.Id));
            modelBuilder.Entity<Turno>(entity => entity.HasKey(e => e.Id));
            modelBuilder.Entity<Payment>(entity => entity.HasKey(e => e.Id));
            modelBuilder.Entity<HorarioUsuario>(entity => entity.HasKey(e => e.Id));
        }

        /// <summary>
        /// Cria todas as tabelas do sistema manualmente se necessário
        /// </summary>
        public void CreateAllTables()
        {
            try
            {
                Debug.WriteLine("🔨 CRIANDO TODAS AS TABELAS...");

                // Primeiro, tentar o método padrão do EF
                Database.EnsureCreated();
                Debug.WriteLine("✅ Database.EnsureCreated() executado");

                // Verificar se as tabelas foram criadas
                var tables = GetExistingTables();
                Debug.WriteLine($"📊 Tabelas existentes após EnsureCreated: {tables.Count}");

                // Se não tiver tabelas suficientes, criar manualmente
                if (tables.Count < 15) // Número mínimo esperado de tabelas
                {
                    Debug.WriteLine("⚠️ Poucas tabelas criadas, criando manualmente...");
                    CreateTablesManually();
                }

                // Verificar novamente
                tables = GetExistingTables();
                Debug.WriteLine($"📊 Tabelas existentes após criação manual: {tables.Count}");

                // Criar índices
                CreateAllIndexes();

                // Inserir dados iniciais
                InsertInitialData();

                Debug.WriteLine("✅ Todas as tabelas criadas com sucesso!");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ ERRO ao criar tabelas: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");

                // Tentar abordagem de emergência
                CreateEmergencyTables();
            }
        }

        /// <summary>
        /// Cria todas as tabelas manualmente usando SQL puro
        /// </summary>
        private void CreateTablesManually()
        {
            try
            {
                Debug.WriteLine("🔨 Criando tabelas manualmente...");

                // 1. Users - DEVE SER A PRIMEIRA (é a que estava dando erro)
                ExecuteSqlIfTableNotExists("Users", @"
                    CREATE TABLE IF NOT EXISTS Users (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Username TEXT NOT NULL UNIQUE,
                        PasswordHash TEXT NOT NULL,
                        FullName TEXT,
                        Role INTEGER NOT NULL DEFAULT 0,
                        IsActive INTEGER DEFAULT 1,
                        CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP
                    )");

                // 2. RestaurantConfigs
                ExecuteSqlIfTableNotExists("RestaurantConfigs", @"
                    CREATE TABLE IF NOT EXISTS RestaurantConfigs (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        RestaurantName TEXT DEFAULT 'Meu Restaurante',
                        CurrencySymbol TEXT DEFAULT 'MTn',
                        LogoBytes BLOB,
                        PrinterName TEXT,
                        KitchenPrinterName TEXT,
                        CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP
                    )");

                // 3. ProductCategories
                ExecuteSqlIfTableNotExists("ProductCategories", @"
                    CREATE TABLE IF NOT EXISTS ProductCategories (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Nome TEXT NOT NULL,
                        Descricao TEXT,
                        Ativo INTEGER DEFAULT 1,
                        CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP
                    )");

                // 4. Ingredients
                ExecuteSqlIfTableNotExists("Ingredients", @"
                    CREATE TABLE IF NOT EXISTS Ingredients (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        Stock DECIMAL DEFAULT 0,
                        Cost DECIMAL DEFAULT 0,
                        Unit TEXT DEFAULT 'unidade',
                        Active INTEGER DEFAULT 1,
                        IsCritical INTEGER DEFAULT 0,
                        CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                        UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
                    )");

                // 5. Products
                ExecuteSqlIfTableNotExists("Products", @"
                    CREATE TABLE IF NOT EXISTS Products (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        Code TEXT UNIQUE,
                        Description TEXT,
                        Price DECIMAL NOT NULL,
                        Stock DECIMAL DEFAULT 0,
                        Active INTEGER DEFAULT 1,
                        IsComposite INTEGER DEFAULT 0,
                        PrintToKitchen INTEGER DEFAULT 1,
                        CategoryId INTEGER,
                        Barcode TEXT,
                        Image BLOB,
                        CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                        UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (CategoryId) REFERENCES ProductCategories(Id)
                    )");

                // 6. ProductIngredients
                ExecuteSqlIfTableNotExists("ProductIngredients", @"
                    CREATE TABLE IF NOT EXISTS ProductIngredients (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ProductId INTEGER NOT NULL,
                        IngredientId INTEGER NOT NULL,
                        QuantityUsed INTEGER DEFAULT 1,
                        FOREIGN KEY (ProductId) REFERENCES Products(Id) ON DELETE CASCADE,
                        FOREIGN KEY (IngredientId) REFERENCES Ingredients(Id) ON DELETE RESTRICT,
                        UNIQUE(ProductId, IngredientId)
                    )");

                // 7. Suppliers
                ExecuteSqlIfTableNotExists("Suppliers", @"
                    CREATE TABLE IF NOT EXISTS Suppliers (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        NIF TEXT UNIQUE,
                        Phone TEXT,
                        Email TEXT,
                        Address TEXT,
                        City TEXT,
                        Province TEXT,
                        ContactPerson TEXT,
                        PaymentTerms TEXT,
                        Notes TEXT,
                        Active INTEGER DEFAULT 1,
                        PurchaseCount INTEGER DEFAULT 0,
                        CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP
                    )");

                // 8. PurchaseOrders
                ExecuteSqlIfTableNotExists("PurchaseOrders", @"
                    CREATE TABLE IF NOT EXISTS PurchaseOrders (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        PurchaseNumber TEXT UNIQUE NOT NULL,
                        SupplierId INTEGER NOT NULL,
                        PurchaseType TEXT DEFAULT 'Compra',
                        Status TEXT DEFAULT 'Pendente',
                        TotalAmount DECIMAL DEFAULT 0,
                        Notes TEXT,
                        CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                        ReceivedAt TEXT,
                        FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id)
                    )");

                // 9. PurchaseOrderItems
                ExecuteSqlIfTableNotExists("PurchaseOrderItems", @"
                    CREATE TABLE IF NOT EXISTS PurchaseOrderItems (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        PurchaseOrderId INTEGER NOT NULL,
                        ProductId INTEGER NOT NULL,
                        Quantity INTEGER NOT NULL,
                        UnitPrice DECIMAL NOT NULL,
                        Subtotal DECIMAL NOT NULL,
                        FOREIGN KEY (PurchaseOrderId) REFERENCES PurchaseOrders(Id) ON DELETE CASCADE,
                        FOREIGN KEY (ProductId) REFERENCES Products(Id)
                    )");

                // 10. InventoryMovements
                ExecuteSqlIfTableNotExists("InventoryMovements", @"
                    CREATE TABLE IF NOT EXISTS InventoryMovements (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ProductId INTEGER NOT NULL,
                        MovementType TEXT NOT NULL,
                        Quantity INTEGER NOT NULL,
                        PreviousStock INTEGER NOT NULL,
                        NewStock INTEGER NOT NULL,
                        Reason TEXT,
                        Notes TEXT,
                        UserId INTEGER,
                        CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (ProductId) REFERENCES Products(Id),
                        FOREIGN KEY (UserId) REFERENCES Users(Id)
                    )");

                // 11. Orders
                ExecuteSqlIfTableNotExists("Orders", @"
                    CREATE TABLE IF NOT EXISTS Orders (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        OrderNumber TEXT UNIQUE,
                        TableId INTEGER,
                        CustomerName TEXT,
                        Status TEXT DEFAULT 'Aberto',
                        TotalAmount DECIMAL DEFAULT 0,
                        Notes TEXT,
                        CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                        UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
                    )");

                // 12. OrderItems
                ExecuteSqlIfTableNotExists("OrderItems", @"
                    CREATE TABLE IF NOT EXISTS OrderItems (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        OrderId INTEGER NOT NULL,
                        ProductId INTEGER NOT NULL,
                        Quantity INTEGER NOT NULL,
                        UnitPrice DECIMAL NOT NULL,
                        Subtotal DECIMAL NOT NULL,
                        Notes TEXT,
                        FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE CASCADE,
                        FOREIGN KEY (ProductId) REFERENCES Products(Id)
                    )");

                // 13. Payments
                ExecuteSqlIfTableNotExists("Payments", @"
                    CREATE TABLE IF NOT EXISTS Payments (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        OrderId INTEGER NOT NULL,
                        PaymentMethod TEXT NOT NULL,
                        Amount DECIMAL NOT NULL,
                        Status TEXT DEFAULT 'Pago',
                        TransactionDate TEXT DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (OrderId) REFERENCES Orders(Id)
                    )");

                // 14. RestaurantTables
                ExecuteSqlIfTableNotExists("RestaurantTables", @"
                    CREATE TABLE IF NOT EXISTS RestaurantTables (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TableNumber INTEGER NOT NULL UNIQUE,
                        Capacity INTEGER DEFAULT 4,
                        Status TEXT DEFAULT 'Livre',
                        Location TEXT
                    )");

                // 15. Outras tabelas
                ExecuteSqlIfTableNotExists("Promotions", "CREATE TABLE IF NOT EXISTS Promotions (Id INTEGER PRIMARY KEY AUTOINCREMENT)");
                ExecuteSqlIfTableNotExists("StockMovements", "CREATE TABLE IF NOT EXISTS StockMovements (Id INTEGER PRIMARY KEY AUTOINCREMENT)");
                ExecuteSqlIfTableNotExists("FuncionarioTurnos", "CREATE TABLE IF NOT EXISTS FuncionarioTurnos (Id INTEGER PRIMARY KEY AUTOINCREMENT)");
                ExecuteSqlIfTableNotExists("VFDConfigs", "CREATE TABLE IF NOT EXISTS VFDConfigs (Id INTEGER PRIMARY KEY AUTOINCREMENT)");
                ExecuteSqlIfTableNotExists("TaxasIVA", "CREATE TABLE IF NOT EXISTS TaxasIVA (Id INTEGER PRIMARY KEY AUTOINCREMENT)");
                ExecuteSqlIfTableNotExists("DailyClosings", "CREATE TABLE IF NOT EXISTS DailyClosings (Id INTEGER PRIMARY KEY AUTOINCREMENT)");
                ExecuteSqlIfTableNotExists("DailyStockSnapshots", "CREATE TABLE IF NOT EXISTS DailyStockSnapshots (Id INTEGER PRIMARY KEY AUTOINCREMENT)");
                ExecuteSqlIfTableNotExists("WhatsAppApiConfigs", "CREATE TABLE IF NOT EXISTS WhatsAppApiConfigs (Id INTEGER PRIMARY KEY AUTOINCREMENT)");
                ExecuteSqlIfTableNotExists("Reservations", "CREATE TABLE IF NOT EXISTS Reservations (Id INTEGER PRIMARY KEY AUTOINCREMENT)");
                ExecuteSqlIfTableNotExists("Clientes", "CREATE TABLE IF NOT EXISTS Clientes (Id INTEGER PRIMARY KEY AUTOINCREMENT)");
                ExecuteSqlIfTableNotExists("Entregadores", "CREATE TABLE IF NOT EXISTS Entregadores (Id INTEGER PRIMARY KEY AUTOINCREMENT)");
                ExecuteSqlIfTableNotExists("Turnos", "CREATE TABLE IF NOT EXISTS Turnos (Id INTEGER PRIMARY KEY AUTOINCREMENT)");
                ExecuteSqlIfTableNotExists("HorarioUsuarios", "CREATE TABLE IF NOT EXISTS HorarioUsuarios (Id INTEGER PRIMARY KEY AUTOINCREMENT)");

                Debug.WriteLine("✅ Tabelas criadas manualmente");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro na criação manual de tabelas: {ex.Message}");
            }
        }

        /// <summary>
        /// Cria tabelas em modo de emergência (apaga e recria tudo)
        /// </summary>
        private void CreateEmergencyTables()
        {
            try
            {
                Debug.WriteLine("🚨 CRIANDO TABELAS EM MODO DE EMERGÊNCIA...");

                // Fechar conexão
                try { Database.CloseConnection(); } catch { }

                // Deletar banco existente
                string dbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AyGestRest",
                    "AyGestRest.db");

                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                    Debug.WriteLine("🗑️ Banco antigo deletado");
                }

                // Recriar diretório
                Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

                // Abrir nova conexão
                Database.OpenConnection();

                // Criar tabelas essenciais
                CreateTablesManually();

                // Inserir dados críticos
                InsertInitialData();

                Debug.WriteLine("✅ Banco criado em modo de emergência");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ ERRO CRÍTICO no modo de emergência: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Executa SQL apenas se a tabela não existir
        /// </summary>
        private void ExecuteSqlIfTableNotExists(string tableName, string sql)
        {
            try
            {
                var exists = Database.ExecuteSqlRaw(
                    $"SELECT name FROM sqlite_master WHERE type='table' AND name='{tableName}'");

                if (exists == 0)
                {
                    Database.ExecuteSqlRaw(sql);
                    Debug.WriteLine($"✅ Tabela {tableName} criada");
                }
                else
                {
                    Debug.WriteLine($"ℹ️ Tabela {tableName} já existe");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ Erro ao criar tabela {tableName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Cria todos os índices necessários
        /// </summary>
        private void CreateAllIndexes()
        {
            try
            {
                Debug.WriteLine("📊 Criando índices...");

                // Índices para Users
                Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Users_Username ON Users(Username)");

                // Índices para Products
                Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Products_Code ON Products(Code) WHERE Code IS NOT NULL");
                Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Products_CategoryId ON Products(CategoryId)");

                // Índices para ProductIngredients
                Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ProductIngredients_ProductId ON ProductIngredients(ProductId)");
                Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ProductIngredients_IngredientId ON ProductIngredients(IngredientId)");

                // Índices para InventoryMovements
                Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_InventoryMovements_CreatedAt ON InventoryMovements(CreatedAt)");
                Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_InventoryMovements_ProductId ON InventoryMovements(ProductId)");

                // Índices para Suppliers
                Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Suppliers_NIF ON Suppliers(NIF) WHERE NIF IS NOT NULL");

                // Índices para PurchaseOrders
                Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_PurchaseOrders_PurchaseNumber ON PurchaseOrders(PurchaseNumber)");
                Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_PurchaseOrders_SupplierId ON PurchaseOrders(SupplierId)");
                Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_PurchaseOrders_CreatedAt ON PurchaseOrders(CreatedAt)");

                // Índices para Orders
                Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Orders_OrderNumber ON Orders(OrderNumber) WHERE OrderNumber IS NOT NULL");
                Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Orders_CreatedAt ON Orders(CreatedAt)");

                Debug.WriteLine("✅ Índices criados");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ Erro ao criar índices: {ex.Message}");
            }
        }

        /// <summary>
        /// Insere dados iniciais essenciais
        /// </summary>
        private void InsertInitialData()
        {
            try
            {
                Debug.WriteLine("📝 Inserindo dados iniciais...");

                // Verificar se já existe usuário admin
                var adminExists = Database.ExecuteSqlRaw("SELECT COUNT(*) FROM Users WHERE Username = 'Admin'") > 0;

                if (!adminExists)
                {
                    Database.ExecuteSqlRaw(@"
                        INSERT INTO Users (Username, PasswordHash, FullName, Role, IsActive)
                        VALUES ('Admin', '1304', 'Administrador', 0, 1)");
                    Debug.WriteLine("✅ Usuário admin criado");
                }

                // Verificar se já existe configuração
                var configExists = Database.ExecuteSqlRaw("SELECT COUNT(*) FROM RestaurantConfigs") > 0;

                if (!configExists)
                {
                    Database.ExecuteSqlRaw(@"
                        INSERT INTO RestaurantConfigs (RestaurantName, CurrencySymbol)
                        VALUES ('Meu Restaurante', 'MTn')");
                    Debug.WriteLine("✅ Configuração padrão criada");
                }

                // Criar categoria padrão se não existir
                var categoryExists = Database.ExecuteSqlRaw("SELECT COUNT(*) FROM ProductCategories") > 0;

                if (!categoryExists)
                {
                    Database.ExecuteSqlRaw(@"
                        INSERT INTO ProductCategories (Nome, Descricao, Ativo)
                        VALUES ('Geral', 'Categoria padrão', 1)");
                    Debug.WriteLine("✅ Categoria padrão criada");
                }

                Debug.WriteLine("✅ Dados iniciais inseridos");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ Erro ao inserir dados iniciais: {ex.Message}");
            }
        }

        /// <summary>
        /// Obtém lista de tabelas existentes
        /// </summary>
        private List<string> GetExistingTables()
        {
            var tables = new List<string>();
            try
            {
                using (var command = Database.GetDbConnection().CreateCommand())
                {
                    command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
                    Database.OpenConnection();
                    using (var result = command.ExecuteReader())
                    {
                        while (result.Read())
                        {
                            tables.Add(result.GetString(0));
                        }
                    }
                    Database.CloseConnection();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao obter tabelas existentes: {ex.Message}");
            }
            return tables;
        }

        /// <summary>
        /// Método simplificado para criar/atualizar o banco
        /// </summary>
        public void InitializeDatabase()
        {
            try
            {
                Debug.WriteLine("🚀 INICIALIZANDO BANCO DE DADOS...");

                // Garantir que o diretório existe
                string dbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AyGestRest",
                    "AyGestRest.db");

                Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

                // Criar todas as tabelas
                CreateAllTables();

                // Verificar resultado
                var tables = GetExistingTables();
                Debug.WriteLine($"🎯 Total de tabelas criadas: {tables.Count}");

                foreach (var table in tables.OrderBy(t => t))
                {
                    Debug.WriteLine($"  - {table}");
                }

                if (tables.Count >= 15)
                {
                    Debug.WriteLine("✅ Banco de dados inicializado com sucesso!");
                }
                else
                {
                    Debug.WriteLine($"⚠️ Atenção: Apenas {tables.Count} tabelas foram criadas");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ ERRO CRÍTICO na inicialização do banco: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Garante que o usuário admin existe
        /// </summary>
        public void EnsureAdminUser()
        {
            try
            {
                Debug.WriteLine("👤 Verificando usuário admin...");

                var adminExists = Users.Any(u => u.Username.ToLower().Trim() == "admin");

                if (!adminExists)
                {
                    var admin = new User
                    {
                        Username = "Admin",
                        PasswordHash = "1304",
                        FullName = "Administrador",
                        Role = UserRole.Administrador,
                        IsActive = true
                    };

                    Users.Add(admin);
                    SaveChanges();
                    Debug.WriteLine("✅ Usuário admin criado com sucesso!");
                }
                else
                {
                    Debug.WriteLine("ℹ️ Usuário admin já existe.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro ao verificar/criar usuário admin: {ex.Message}");
            }
        }
        public void MigrateProductIngredientsTable()
        {
            try
            {
                Debug.WriteLine("🔧 Verificando estrutura da tabela ProductIngredients...");

                // Verificar se a tabela existe e qual é sua estrutura atual
                var tableExists = Database.ExecuteSqlRaw(@"
            SELECT name FROM sqlite_master 
            WHERE type='table' AND name='ProductIngredients'") > 0;

                if (!tableExists)
                {
                    Debug.WriteLine("ℹ️ Tabela ProductIngredients não existe, será criada normalmente");
                    return;
                }

                // Verificar se a tabela já tem a estrutura correta (chave composta)
                var tableInfo = Database.ExecuteSqlRaw(@"
            SELECT sql FROM sqlite_master 
            WHERE type='table' AND name='ProductIngredients'");

                // Backup dos dados existentes
                Debug.WriteLine("💾 Fazendo backup dos dados existentes...");
                Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ProductIngredients_Backup AS 
            SELECT * FROM ProductIngredients");

                var backupCount = Database.ExecuteSqlRaw(@"SELECT COUNT(*) FROM ProductIngredients_Backup");
                Debug.WriteLine($"✅ Backup criado com {backupCount} registros");

                // Recriar a tabela com a estrutura correta (chave composta ProductId + IngredientId)
                Debug.WriteLine("🔄 Recriando tabela com estrutura correta...");

                Database.ExecuteSqlRaw(@"
            DROP TABLE IF EXISTS ProductIngredients_New;
            
            CREATE TABLE ProductIngredients_New (
                ProductId INTEGER NOT NULL,
                IngredientId INTEGER NOT NULL,
                QuantityUsed INTEGER DEFAULT 1,
                PRIMARY KEY (ProductId, IngredientId),
                FOREIGN KEY (ProductId) REFERENCES Products(Id) ON DELETE CASCADE,
                FOREIGN KEY (IngredientId) REFERENCES Ingredients(Id) ON DELETE RESTRICT
            );

            CREATE INDEX IF NOT EXISTS IX_ProductIngredients_IngredientId 
            ON ProductIngredients_New(IngredientId);");

                // Migrar dados (removendo duplicatas se houver)
                Debug.WriteLine("📦 Migrando dados...");
                Database.ExecuteSqlRaw(@"
            INSERT OR IGNORE INTO ProductIngredients_New (ProductId, IngredientId, QuantityUsed)
            SELECT ProductId, IngredientId, 
                   COALESCE(QuantityUsed, 1) as QuantityUsed 
            FROM ProductIngredients
            GROUP BY ProductId, IngredientId;");

                var migratedCount = Database.ExecuteSqlRaw(@"SELECT COUNT(*) FROM ProductIngredients_New");
                Debug.WriteLine($"✅ Migrados {migratedCount} registros");

                // Substituir tabela antiga pela nova
                Debug.WriteLine("🔄 Substituindo tabela antiga...");
                Database.ExecuteSqlRaw(@"
            DROP TABLE IF EXISTS ProductIngredients_Old;
            ALTER TABLE ProductIngredients RENAME TO ProductIngredients_Old;
            ALTER TABLE ProductIngredients_New RENAME TO ProductIngredients;
            DROP TABLE IF EXISTS ProductIngredients_Old;");

                Debug.WriteLine("✅ Tabela ProductIngredients migrada com sucesso!");
                Debug.WriteLine($"📊 Registros migrados: {migratedCount} (de {backupCount} backups)");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro ao migrar tabela: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");

                // Tentar restaurar do backup em caso de erro
                try
                {
                    Debug.WriteLine("🔄 Tentando restaurar do backup...");
                    Database.ExecuteSqlRaw(@"
                DROP TABLE IF EXISTS ProductIngredients;
                ALTER TABLE ProductIngredients_Backup RENAME TO ProductIngredients;");
                    Debug.WriteLine("✅ Backup restaurado");
                }
                catch { }
            }
        }


        /// <summary>
        /// Método de compatibilidade (mantido para não quebrar código existente)
        /// </summary>
        public void CreateOrUpdateDatabase()
        {
            InitializeDatabase();
        }
    }
}