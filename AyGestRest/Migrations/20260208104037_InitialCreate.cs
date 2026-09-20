using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AyGestRest.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /* migrationBuilder.CreateTable(
                 name: "Clientes",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     CodigoUnico = table.Column<string>(type: "TEXT", nullable: false),
                     Nome = table.Column<string>(type: "TEXT", nullable: false),
                     Telefone = table.Column<string>(type: "TEXT", nullable: true),
                     Email = table.Column<string>(type: "TEXT", nullable: true),
                     Pontos = table.Column<int>(type: "INTEGER", nullable: false),
                     UltimaVisita = table.Column<DateTime>(type: "TEXT", nullable: false)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_Clientes", x => x.Id);
                 });

             migrationBuilder.CreateTable(
                 name: "DailyClosings",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     Data = table.Column<DateTime>(type: "TEXT", nullable: false),
                     TotalVendido = table.Column<decimal>(type: "TEXT", nullable: false),
                     TotalDinheiro = table.Column<decimal>(type: "TEXT", nullable: false),
                     TotalOutrosPagamentos = table.Column<decimal>(type: "TEXT", nullable: false),
                     LucroBrutoEstimado = table.Column<decimal>(type: "TEXT", nullable: false),
                     ValorContado = table.Column<decimal>(type: "TEXT", nullable: false),
                     Diferenca = table.Column<decimal>(type: "TEXT", nullable: false),
                     IsCurrent = table.Column<bool>(type: "INTEGER", nullable: false)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_DailyClosings", x => x.Id);
                 });

             migrationBuilder.CreateTable(
                 name: "Entregadores",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     Nome = table.Column<string>(type: "TEXT", nullable: false),
                     Telefone = table.Column<string>(type: "TEXT", nullable: false),
                     Ativo = table.Column<bool>(type: "INTEGER", nullable: false)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_Entregadores", x => x.Id);
                 });

             migrationBuilder.CreateTable(
                 name: "Ingredients",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     Name = table.Column<string>(type: "TEXT", nullable: false),
                     Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                     Cost = table.Column<decimal>(type: "TEXT", nullable: false),
                     Stock = table.Column<decimal>(type: "TEXT", nullable: false),
                     Unit = table.Column<string>(type: "TEXT", nullable: false),
                     Active = table.Column<bool>(type: "INTEGER", nullable: false),
                     Critical = table.Column<bool>(type: "INTEGER", nullable: false)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_Ingredients", x => x.Id);
                 });

             migrationBuilder.CreateTable(
                 name: "ProductCategories",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     Nome = table.Column<string>(type: "TEXT", nullable: false),
                     CorHex = table.Column<string>(type: "TEXT", nullable: false),
                     Ativo = table.Column<bool>(type: "INTEGER", nullable: false),
                     Icone = table.Column<string>(type: "TEXT", nullable: false)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_ProductCategories", x => x.Id);
                 });

             migrationBuilder.CreateTable(
                 name: "Promotions",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     Name = table.Column<string>(type: "TEXT", nullable: false),
                     Type = table.Column<string>(type: "TEXT", nullable: false),
                     DiscountValue = table.Column<string>(type: "TEXT", nullable: false),
                     StartDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                     EndDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                     IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_Promotions", x => x.Id);
                 });

             migrationBuilder.CreateTable(
                 name: "RestaurantConfigs",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     RestaurantName = table.Column<string>(type: "TEXT", nullable: false),
                     NIF = table.Column<string>(type: "TEXT", nullable: true),
                     Address = table.Column<string>(type: "TEXT", nullable: true),
                     Phone = table.Column<string>(type: "TEXT", nullable: true),
                     Email = table.Column<string>(type: "TEXT", nullable: true),
                     EmailAddress = table.Column<string>(type: "TEXT", nullable: true),
                     EmailPassword = table.Column<string>(type: "TEXT", nullable: false),
                     SMTPServer = table.Column<string>(type: "TEXT", nullable: true),
                     SMTPPort = table.Column<int>(type: "INTEGER", nullable: false),
                     UseSSL = table.Column<bool>(type: "INTEGER", nullable: false),
                     RecipientEmail = table.Column<string>(type: "TEXT", nullable: true),
                     SendDailyReport = table.Column<bool>(type: "INTEGER", nullable: false),
                     SendReports = table.Column<bool>(type: "INTEGER", nullable: false),
                     CurrencySymbol = table.Column<string>(type: "TEXT", nullable: false),
                     LogoPath = table.Column<string>(type: "TEXT", nullable: true),
                     LogoBytes = table.Column<byte[]>(type: "BLOB", nullable: true),
                     LogoMimeType = table.Column<string>(type: "TEXT", nullable: true),
                     RequiresLogin = table.Column<bool>(type: "INTEGER", nullable: false),
                     InactivityTimeout = table.Column<int>(type: "INTEGER", nullable: false),
                     AutoSave = table.Column<bool>(type: "INTEGER", nullable: false),
                     FuncionarioPodeFechoDiario = table.Column<bool>(type: "INTEGER", nullable: false),
                     FuncionarioPodeReservas = table.Column<bool>(type: "INTEGER", nullable: false),
                     FuncionarioPodeHistoricoVendas = table.Column<bool>(type: "INTEGER", nullable: false),
                     SoundEffects = table.Column<bool>(type: "INTEGER", nullable: false),
                     DarkMode = table.Column<bool>(type: "INTEGER", nullable: false),
                     ShowNotifications = table.Column<bool>(type: "INTEGER", nullable: false),
                     BackupDiario = table.Column<bool>(type: "INTEGER", nullable: false),
                     AutoBackup = table.Column<bool>(type: "INTEGER", nullable: false),
                     BackupTime = table.Column<string>(type: "TEXT", nullable: false),
                     BackupPath = table.Column<string>(type: "TEXT", nullable: true),
                     PrinterName = table.Column<string>(type: "TEXT", nullable: true),
                     KitchenPrinterName = table.Column<string>(type: "TEXT", nullable: true),
                     PrintOrderToKitchen = table.Column<bool>(type: "INTEGER", nullable: false),
                     SecondScreenEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                     WhatsAppToken = table.Column<string>(type: "TEXT", nullable: true),
                     WhatsAppPhoneId = table.Column<string>(type: "TEXT", nullable: true),
                     CopiesCount = table.Column<int>(type: "INTEGER", nullable: false),
                     PrintMode = table.Column<string>(type: "TEXT", nullable: false),
                     WhatsAppFullNumber = table.Column<string>(type: "TEXT", nullable: true),
                     ImprimirAntesDeFechar = table.Column<bool>(type: "INTEGER", nullable: false)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_RestaurantConfigs", x => x.Id);
                 });

             migrationBuilder.CreateTable(
                 name: "RestaurantTable",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     Number = table.Column<string>(type: "TEXT", nullable: false),
                     Name = table.Column<string>(type: "TEXT", nullable: false),
                     Capacity = table.Column<int>(type: "INTEGER", nullable: false),
                     Status = table.Column<int>(type: "INTEGER", nullable: false),
                     PositionX = table.Column<double>(type: "REAL", nullable: false),
                     PositionY = table.Column<double>(type: "REAL", nullable: false),
                     Selecionado = table.Column<bool>(type: "INTEGER", nullable: false),
                     Notes = table.Column<string>(type: "TEXT", nullable: false)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_RestaurantTable", x => x.Id);
                 });

             migrationBuilder.CreateTable(
                 name: "Suppliers",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     Name = table.Column<string>(type: "TEXT", nullable: false),
                     NIF = table.Column<string>(type: "TEXT", nullable: false),
                     Phone = table.Column<string>(type: "TEXT", nullable: false),
                     Email = table.Column<string>(type: "TEXT", nullable: false),
                     Address = table.Column<string>(type: "TEXT", nullable: false),
                     City = table.Column<string>(type: "TEXT", nullable: false),
                     Province = table.Column<string>(type: "TEXT", nullable: false),
                     ContactPerson = table.Column<string>(type: "TEXT", nullable: false),
                     PaymentTerms = table.Column<string>(type: "TEXT", nullable: false),
                     Notes = table.Column<string>(type: "TEXT", nullable: false),
                     Active = table.Column<bool>(type: "INTEGER", nullable: false)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_Suppliers", x => x.Id);
                 });

             migrationBuilder.CreateTable(
                 name: "TaxasIVA",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     Descricao = table.Column<string>(type: "TEXT", nullable: false),
                     Valor = table.Column<double>(type: "REAL", nullable: false),
                     Tipo = table.Column<string>(type: "TEXT", nullable: false),
                     Ativa = table.Column<bool>(type: "INTEGER", nullable: false)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_TaxasIVA", x => x.Id);
                 });

             migrationBuilder.CreateTable(
                 name: "Turnos",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     FuncionarioId = table.Column<int>(type: "INTEGER", nullable: false),
                     Data = table.Column<DateTime>(type: "TEXT", nullable: false),
                     HoraInicio = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                     HoraFim = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                     Observacoes = table.Column<string>(type: "TEXT", nullable: true),
                     CheckIn = table.Column<DateTime>(type: "TEXT", nullable: true),
                     CheckOut = table.Column<DateTime>(type: "TEXT", nullable: true),
                     Estado = table.Column<string>(type: "TEXT", nullable: false),
                     CriadoPor = table.Column<string>(type: "TEXT", nullable: true),
                     CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: true),
                     AtualizadoEm = table.Column<DateTime>(type: "TEXT", nullable: true),
                     TipoTurno = table.Column<string>(type: "TEXT", nullable: true),
                     RepetirSemanalmente = table.Column<bool>(type: "INTEGER", nullable: true),
                     NotificarFuncionario = table.Column<bool>(type: "INTEGER", nullable: true)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_Turnos", x => x.Id);
                 });

             migrationBuilder.CreateTable(
                 name: "Users",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     Username = table.Column<string>(type: "TEXT", nullable: false),
                     PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                     FullName = table.Column<string>(type: "TEXT", nullable: false),
                     Role = table.Column<int>(type: "INTEGER", nullable: false),
                     IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                     NomeOrdenacao = table.Column<string>(type: "TEXT", nullable: false, computedColumnSql: "CASE WHEN FullName IS NOT NULL AND FullName != '' THEN FullName ELSE Username END"),
                     UltimoLogin = table.Column<DateTime>(type: "TEXT", nullable: true),
                     LastLogin = table.Column<DateTime>(type: "TEXT", nullable: true),
                     Name = table.Column<string>(type: "TEXT", nullable: true),
                     UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                     CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_Users", x => x.Id);
                 });

             migrationBuilder.CreateTable(
                 name: "VFDConfigs",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                     COMPort = table.Column<string>(type: "TEXT", nullable: false),
                     BaudRate = table.Column<int>(type: "INTEGER", nullable: false),
                     DisplayLines = table.Column<int>(type: "INTEGER", nullable: false),
                     DisplayColumns = table.Column<int>(type: "INTEGER", nullable: false),
                     ShowRealTimeTotal = table.Column<bool>(type: "INTEGER", nullable: false),
                     LastTestDate = table.Column<string>(type: "TEXT", nullable: false)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_VFDConfigs", x => x.Id);
                 });

             migrationBuilder.CreateTable(
                 name: "WhatsAppApiConfigs",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     AccessToken = table.Column<string>(type: "TEXT", nullable: false),
                     PhoneNumberId = table.Column<string>(type: "TEXT", nullable: false),
                     BusinessAccountId = table.Column<string>(type: "TEXT", nullable: false),
                     CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                     UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_WhatsAppApiConfigs", x => x.Id);
                 });

             migrationBuilder.CreateTable(
                 name: "Products",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     Name = table.Column<string>(type: "TEXT", nullable: false),
                     Code = table.Column<string>(type: "TEXT", nullable: true),
                     Description = table.Column<string>(type: "TEXT", nullable: true),
                     Price = table.Column<decimal>(type: "TEXT", nullable: false),
                     Active = table.Column<bool>(type: "INTEGER", nullable: false),
                     Stock = table.Column<decimal>(type: "TEXT", nullable: false),
                     Unit = table.Column<string>(type: "TEXT", nullable: false),
                     CategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                     IsComposite = table.Column<bool>(type: "INTEGER", nullable: false),
                     PrintToKitchen = table.Column<bool>(type: "INTEGER", nullable: false),
                     Barcode = table.Column<string>(type: "TEXT", nullable: true),
                     Featured = table.Column<bool>(type: "INTEGER", nullable: false),
                     Promo = table.Column<bool>(type: "INTEGER", nullable: false),
                     Image = table.Column<byte[]>(type: "BLOB", nullable: true),
                     TrackInventory = table.Column<bool>(type: "INTEGER", nullable: false),
                     IngredientId = table.Column<int>(type: "INTEGER", nullable: true),
                     ProductCategoryId = table.Column<int>(type: "INTEGER", nullable: true),
                     PromotionId = table.Column<int>(type: "INTEGER", nullable: true)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_Products", x => x.Id);
                     table.ForeignKey(
                         name: "FK_Products_Ingredients_IngredientId",
                         column: x => x.IngredientId,
                         principalTable: "Ingredients",
                         principalColumn: "Id");
                     table.ForeignKey(
                         name: "FK_Products_ProductCategories_CategoryId",
                         column: x => x.CategoryId,
                         principalTable: "ProductCategories",
                         principalColumn: "Id",
                         onDelete: ReferentialAction.Restrict);
                     table.ForeignKey(
                         name: "FK_Products_ProductCategories_ProductCategoryId",
                         column: x => x.ProductCategoryId,
                         principalTable: "ProductCategories",
                         principalColumn: "Id");
                     table.ForeignKey(
                         name: "FK_Products_Promotions_PromotionId",
                         column: x => x.PromotionId,
                         principalTable: "Promotions",
                         principalColumn: "Id");
                 });

             migrationBuilder.CreateTable(
                 name: "Reservations",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     NomeCliente = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                     ClienteId = table.Column<int>(type: "INTEGER", nullable: false),
                     RestaurantTableId = table.Column<int>(type: "INTEGER", nullable: false),
                     Inicio = table.Column<DateTime>(type: "TEXT", nullable: false),
                     Fim = table.Column<DateTime>(type: "TEXT", nullable: false),
                     NumPessoas = table.Column<int>(type: "INTEGER", nullable: false),
                     Estado = table.Column<int>(type: "INTEGER", nullable: false),
                     DataCriacao = table.Column<DateTime>(type: "TEXT", nullable: false),
                     DataAtualizacao = table.Column<DateTime>(type: "TEXT", nullable: true),
                     CodigoVerificacao = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                     MesaAtiva = table.Column<bool>(type: "INTEGER", nullable: false),
                     DataAtivacao = table.Column<DateTime>(type: "TEXT", nullable: true),
                     DataDesativacao = table.Column<DateTime>(type: "TEXT", nullable: true),
                     Observacoes = table.Column<string>(type: "TEXT", nullable: true)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_Reservations", x => x.Id);
                     table.ForeignKey(
                         name: "FK_Reservations_Clientes_ClienteId",
                         column: x => x.ClienteId,
                         principalTable: "Clientes",
                         principalColumn: "Id",
                         onDelete: ReferentialAction.Cascade);
                     table.ForeignKey(
                         name: "FK_Reservations_RestaurantTable_RestaurantTableId",
                         column: x => x.RestaurantTableId,
                         principalTable: "RestaurantTable",
                         principalColumn: "Id",
                         onDelete: ReferentialAction.Cascade);
                 });

             migrationBuilder.CreateTable(
                 name: "PurchaseOrders",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     PurchaseNumber = table.Column<string>(type: "TEXT", nullable: false),
                     SupplierId = table.Column<int>(type: "INTEGER", nullable: false),
                     PurchaseType = table.Column<string>(type: "TEXT", nullable: false),
                     Status = table.Column<string>(type: "TEXT", nullable: false),
                     TotalAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                     Notes = table.Column<string>(type: "TEXT", nullable: false),
                     CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                     ReceivedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_PurchaseOrders", x => x.Id);
                     table.ForeignKey(
                         name: "FK_PurchaseOrders_Suppliers_SupplierId",
                         column: x => x.SupplierId,
                         principalTable: "Suppliers",
                         principalColumn: "Id",
                         onDelete: ReferentialAction.Restrict);
                 });

             migrationBuilder.CreateTable(
                 name: "FuncionarioTurnos",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     UserId = table.Column<int>(type: "INTEGER", nullable: false),
                     Data = table.Column<DateTime>(type: "TEXT", nullable: false),
                     HoraEntrada = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                     HoraSaida = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                     HorasExtras = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                     HorarioPrevistoEntrada = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                     HorarioPrevistoSaida = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                     Turno = table.Column<string>(type: "TEXT", nullable: false),
                     Observacoes = table.Column<string>(type: "TEXT", nullable: false),
                     Ausente = table.Column<bool>(type: "INTEGER", nullable: false),
                     TipoAusencia = table.Column<string>(type: "TEXT", nullable: false),
                     JustificativaAusencia = table.Column<string>(type: "TEXT", nullable: false),
                     JustificativaHorasExtras = table.Column<string>(type: "TEXT", nullable: false),
                     CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                     UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_FuncionarioTurnos", x => x.Id);
                     table.ForeignKey(
                         name: "FK_FuncionarioTurnos_Users_UserId",
                         column: x => x.UserId,
                         principalTable: "Users",
                         principalColumn: "Id",
                         onDelete: ReferentialAction.Restrict);
                 });

             migrationBuilder.CreateTable(
                 name: "HorarioUsuarios",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     UserId = table.Column<int>(type: "INTEGER", nullable: false),
                     Segunda = table.Column<bool>(type: "INTEGER", nullable: false),
                     Terca = table.Column<bool>(type: "INTEGER", nullable: false),
                     Quarta = table.Column<bool>(type: "INTEGER", nullable: false),
                     Quinta = table.Column<bool>(type: "INTEGER", nullable: false),
                     Sexta = table.Column<bool>(type: "INTEGER", nullable: false),
                     Sabado = table.Column<bool>(type: "INTEGER", nullable: false),
                     Domingo = table.Column<bool>(type: "INTEGER", nullable: false),
                     DiasTrabalhoSemana = table.Column<int>(type: "INTEGER", nullable: false),
                     HorasPorDia = table.Column<int>(type: "INTEGER", nullable: false),
                     VigenciaInicio = table.Column<DateTime>(type: "TEXT", nullable: false),
                     VigenciaFim = table.Column<DateTime>(type: "TEXT", nullable: true),
                     CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                     UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_HorarioUsuarios", x => x.Id);
                     table.ForeignKey(
                         name: "FK_HorarioUsuarios_Users_UserId",
                         column: x => x.UserId,
                         principalTable: "Users",
                         principalColumn: "Id",
                         onDelete: ReferentialAction.Cascade);
                 });

             migrationBuilder.CreateTable(
                 name: "DailyStockSnapshots",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                     ProductName = table.Column<string>(type: "TEXT", nullable: false),
                     ProductCode = table.Column<string>(type: "TEXT", nullable: true),
                     CategoryName = table.Column<string>(type: "TEXT", nullable: true),
                     Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                     Unit = table.Column<string>(type: "TEXT", nullable: false),
                     SnapshotDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                     CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                     MinimumStockLevel = table.Column<decimal>(type: "TEXT", nullable: false),
                     IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                     TrackInventory = table.Column<bool>(type: "INTEGER", nullable: false),
                     StockValue = table.Column<decimal>(type: "TEXT", nullable: false)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_DailyStockSnapshots", x => x.Id);
                     table.ForeignKey(
                         name: "FK_DailyStockSnapshots_Products_ProductId",
                         column: x => x.ProductId,
                         principalTable: "Products",
                         principalColumn: "Id",
                         onDelete: ReferentialAction.Cascade);
                 });

             migrationBuilder.CreateTable(
                 name: "InventoryMovements",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                     MovementType = table.Column<string>(type: "TEXT", nullable: false),
                     Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                     PreviousStock = table.Column<decimal>(type: "TEXT", nullable: false),
                     NewStock = table.Column<decimal>(type: "TEXT", nullable: false),
                     Reason = table.Column<string>(type: "TEXT", nullable: false),
                     Notes = table.Column<string>(type: "TEXT", nullable: false),
                     UserId = table.Column<int>(type: "INTEGER", nullable: false),
                     CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_InventoryMovements", x => x.Id);
                     table.ForeignKey(
                         name: "FK_InventoryMovements_Products_ProductId",
                         column: x => x.ProductId,
                         principalTable: "Products",
                         principalColumn: "Id",
                         onDelete: ReferentialAction.Restrict);
                     table.ForeignKey(
                         name: "FK_InventoryMovements_Users_UserId",
                         column: x => x.UserId,
                         principalTable: "Users",
                         principalColumn: "Id",
                         onDelete: ReferentialAction.SetNull);
                 });

             migrationBuilder.CreateTable(
                 name: "ProductIngredients",
                 columns: table => new
                 {
                     ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                     IngredientId = table.Column<int>(type: "INTEGER", nullable: false),
                     QuantityUsed = table.Column<int>(type: "INTEGER", nullable: false),
                     IngredientId1 = table.Column<int>(type: "INTEGER", nullable: true)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_ProductIngredients", x => new { x.ProductId, x.IngredientId });
                     table.ForeignKey(
                         name: "FK_ProductIngredients_Ingredients_IngredientId",
                         column: x => x.IngredientId,
                         principalTable: "Ingredients",
                         principalColumn: "Id",
                         onDelete: ReferentialAction.Cascade);
                     table.ForeignKey(
                         name: "FK_ProductIngredients_Ingredients_IngredientId1",
                         column: x => x.IngredientId1,
                         principalTable: "Ingredients",
                         principalColumn: "Id");
                     table.ForeignKey(
                         name: "FK_ProductIngredients_Products_ProductId",
                         column: x => x.ProductId,
                         principalTable: "Products",
                         principalColumn: "Id",
                         onDelete: ReferentialAction.Cascade);
                 });

             migrationBuilder.CreateTable(
                 name: "StockMovements",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                     Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                     Type = table.Column<string>(type: "TEXT", nullable: false),
                     Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                     BalanceAfter = table.Column<decimal>(type: "TEXT", nullable: false)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_StockMovements", x => x.Id);
                     table.ForeignKey(
                         name: "FK_StockMovements_Products_ProductId",
                         column: x => x.ProductId,
                         principalTable: "Products",
                         principalColumn: "Id",
                         onDelete: ReferentialAction.Cascade);
                 });

             migrationBuilder.CreateTable(
                 name: "Orders",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     TableId = table.Column<int>(type: "INTEGER", nullable: true),
                     OpenDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                     CloseDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                     ReservationId = table.Column<int>(type: "INTEGER", nullable: true),
                     Status = table.Column<int>(type: "INTEGER", nullable: false),
                     Total = table.Column<decimal>(type: "TEXT", nullable: false),
                     Observations = table.Column<string>(type: "TEXT", nullable: false),
                     Data = table.Column<DateTime>(type: "TEXT", nullable: false),
                     UserId = table.Column<int>(type: "INTEGER", nullable: false),
                     EntregadorId = table.Column<int>(type: "INTEGER", nullable: true),
                     Estado = table.Column<string>(type: "TEXT", nullable: false),
                     ClienteNome = table.Column<string>(type: "TEXT", nullable: false),
                     Morada = table.Column<string>(type: "TEXT", nullable: false),
                     Telefone = table.Column<string>(type: "TEXT", nullable: false),
                     Motoboy = table.Column<string>(type: "TEXT", nullable: false),
                     DataPedido = table.Column<DateTime>(type: "TEXT", nullable: false),
                     TempoEstimado = table.Column<int>(type: "INTEGER", nullable: false),
                     TaxaEntrega = table.Column<decimal>(type: "TEXT", nullable: false),
                     Fecha = table.Column<bool>(type: "INTEGER", nullable: false),
                     TotalAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                     IsPaid = table.Column<bool>(type: "INTEGER", nullable: false),
                     PaidAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                     CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                     TalaoNumber = table.Column<int>(type: "INTEGER", nullable: false),
                     ClosedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                     IsClosed = table.Column<bool>(type: "INTEGER", nullable: false)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_Orders", x => x.Id);
                     table.ForeignKey(
                         name: "FK_Orders_Entregadores_EntregadorId",
                         column: x => x.EntregadorId,
                         principalTable: "Entregadores",
                         principalColumn: "Id");
                     table.ForeignKey(
                         name: "FK_Orders_Reservations_ReservationId",
                         column: x => x.ReservationId,
                         principalTable: "Reservations",
                         principalColumn: "Id");
                     table.ForeignKey(
                         name: "FK_Orders_RestaurantTable_TableId",
                         column: x => x.TableId,
                         principalTable: "RestaurantTable",
                         principalColumn: "Id");
                     table.ForeignKey(
                         name: "FK_Orders_Users_UserId",
                         column: x => x.UserId,
                         principalTable: "Users",
                         principalColumn: "Id",
                         onDelete: ReferentialAction.Cascade);
                 });

             migrationBuilder.CreateTable(
                 name: "PurchaseOrderItems",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     PurchaseOrderId = table.Column<int>(type: "INTEGER", nullable: false),
                     ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                     Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                     UnitPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                     Subtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_PurchaseOrderItems", x => x.Id);
                     table.ForeignKey(
                         name: "FK_PurchaseOrderItems_Products_ProductId",
                         column: x => x.ProductId,
                         principalTable: "Products",
                         principalColumn: "Id",
                         onDelete: ReferentialAction.Restrict);
                     table.ForeignKey(
                         name: "FK_PurchaseOrderItems_PurchaseOrders_PurchaseOrderId",
                         column: x => x.PurchaseOrderId,
                         principalTable: "PurchaseOrders",
                         principalColumn: "Id",
                         onDelete: ReferentialAction.Cascade);
                 });

             migrationBuilder.CreateTable(
                 name: "OrderItems",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     OrderId = table.Column<int>(type: "INTEGER", nullable: false),
                     ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                     Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                     PriceAtMoment = table.Column<decimal>(type: "TEXT", nullable: false),
                     Notes = table.Column<string>(type: "TEXT", nullable: false),
                     KitchenStatus = table.Column<int>(type: "INTEGER", nullable: false),
                     Estado = table.Column<string>(type: "TEXT", nullable: false),
                     Name = table.Column<string>(type: "TEXT", nullable: false),
                     Categoria = table.Column<string>(type: "TEXT", nullable: false),
                     CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                     UnitPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                     Total = table.Column<decimal>(type: "TEXT", nullable: false),
                     PromotionsApplied = table.Column<string>(type: "TEXT", nullable: false)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_OrderItems", x => x.Id);
                     table.ForeignKey(
                         name: "FK_OrderItems_Orders_OrderId",
                         column: x => x.OrderId,
                         principalTable: "Orders",
                         principalColumn: "Id",
                         onDelete: ReferentialAction.Cascade);
                     table.ForeignKey(
                         name: "FK_OrderItems_Products_ProductId",
                         column: x => x.ProductId,
                         principalTable: "Products",
                         principalColumn: "Id",
                         onDelete: ReferentialAction.Restrict);
                 });

             migrationBuilder.CreateTable(
                 name: "Payments",
                 columns: table => new
                 {
                     Id = table.Column<int>(type: "INTEGER", nullable: false)
                         .Annotation("Sqlite:Autoincrement", true),
                     OrderId = table.Column<int>(type: "INTEGER", nullable: false),
                     PaymentDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                     Data = table.Column<DateTime>(type: "TEXT", nullable: false),
                     Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                     Valor = table.Column<decimal>(type: "TEXT", nullable: true),
                     Type = table.Column<int>(type: "INTEGER", nullable: false),
                     TipoPagamento = table.Column<string>(type: "TEXT", nullable: false),
                     Reference = table.Column<string>(type: "TEXT", nullable: false),
                     Notes = table.Column<string>(type: "TEXT", nullable: false),
                     Cash = table.Column<decimal>(type: "TEXT", nullable: false),
                     Card = table.Column<decimal>(type: "TEXT", nullable: false),
                     MBWay = table.Column<decimal>(type: "TEXT", nullable: false),
                     TotalPaid = table.Column<decimal>(type: "TEXT", nullable: false),
                     CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                 },
                 constraints: table =>
                 {
                     table.PrimaryKey("PK_Payments", x => x.Id);
                     table.ForeignKey(
                         name: "FK_Payments_Orders_OrderId",
                         column: x => x.OrderId,
                         principalTable: "Orders",
                         principalColumn: "Id",
                         onDelete: ReferentialAction.Cascade);
                 });

             migrationBuilder.CreateIndex(
                 name: "IX_DailyStockSnapshots_ProductId",
                 table: "DailyStockSnapshots",
                 column: "ProductId");

             migrationBuilder.CreateIndex(
                 name: "IX_FuncionarioTurnos_UserId",
                 table: "FuncionarioTurnos",
                 column: "UserId");

             migrationBuilder.CreateIndex(
                 name: "IX_HorarioUsuarios_UserId",
                 table: "HorarioUsuarios",
                 column: "UserId");

             migrationBuilder.CreateIndex(
                 name: "IX_InventoryMovements_CreatedAt",
                 table: "InventoryMovements",
                 column: "CreatedAt");

             migrationBuilder.CreateIndex(
                 name: "IX_InventoryMovements_ProductId",
                 table: "InventoryMovements",
                 column: "ProductId");

             migrationBuilder.CreateIndex(
                 name: "IX_InventoryMovements_UserId",
                 table: "InventoryMovements",
                 column: "UserId");

             migrationBuilder.CreateIndex(
                 name: "IX_OrderItems_OrderId",
                 table: "OrderItems",
                 column: "OrderId");

             migrationBuilder.CreateIndex(
                 name: "IX_OrderItems_ProductId",
                 table: "OrderItems",
                 column: "ProductId");

             migrationBuilder.CreateIndex(
                 name: "IX_Orders_EntregadorId",
                 table: "Orders",
                 column: "EntregadorId");

             migrationBuilder.CreateIndex(
                 name: "IX_Orders_ReservationId",
                 table: "Orders",
                 column: "ReservationId");

             migrationBuilder.CreateIndex(
                 name: "IX_Orders_TableId",
                 table: "Orders",
                 column: "TableId");

             migrationBuilder.CreateIndex(
                 name: "IX_Orders_UserId",
                 table: "Orders",
                 column: "UserId");

             migrationBuilder.CreateIndex(
                 name: "IX_Payments_OrderId",
                 table: "Payments",
                 column: "OrderId");

             migrationBuilder.CreateIndex(
                 name: "IX_ProductIngredients_IngredientId",
                 table: "ProductIngredients",
                 column: "IngredientId");

             migrationBuilder.CreateIndex(
                 name: "IX_ProductIngredients_IngredientId1",
                 table: "ProductIngredients",
                 column: "IngredientId1");

             migrationBuilder.CreateIndex(
                 name: "IX_Products_CategoryId",
                 table: "Products",
                 column: "CategoryId");

             migrationBuilder.CreateIndex(
                 name: "IX_Products_Code",
                 table: "Products",
                 column: "Code",
                 unique: true,
                 filter: "[Code] IS NOT NULL");

             migrationBuilder.CreateIndex(
                 name: "IX_Products_IngredientId",
                 table: "Products",
                 column: "IngredientId");

             migrationBuilder.CreateIndex(
                 name: "IX_Products_ProductCategoryId",
                 table: "Products",
                 column: "ProductCategoryId");

             migrationBuilder.CreateIndex(
                 name: "IX_Products_PromotionId",
                 table: "Products",
                 column: "PromotionId");

             migrationBuilder.CreateIndex(
                 name: "IX_PurchaseOrderItems_ProductId",
                 table: "PurchaseOrderItems",
                 column: "ProductId");

             migrationBuilder.CreateIndex(
                 name: "IX_PurchaseOrderItems_PurchaseOrderId",
                 table: "PurchaseOrderItems",
                 column: "PurchaseOrderId");

             migrationBuilder.CreateIndex(
                 name: "IX_PurchaseOrders_PurchaseNumber",
                 table: "PurchaseOrders",
                 column: "PurchaseNumber",
                 unique: true);

             migrationBuilder.CreateIndex(
                 name: "IX_PurchaseOrders_SupplierId",
                 table: "PurchaseOrders",
                 column: "SupplierId");

             migrationBuilder.CreateIndex(
                 name: "IX_Reservations_ClienteId",
                 table: "Reservations",
                 column: "ClienteId");

             migrationBuilder.CreateIndex(
                 name: "IX_Reservations_RestaurantTableId",
                 table: "Reservations",
                 column: "RestaurantTableId");

             migrationBuilder.CreateIndex(
                 name: "IX_StockMovements_ProductId",
                 table: "StockMovements",
                 column: "ProductId");

             migrationBuilder.CreateIndex(
                 name: "IX_Suppliers_NIF",
                 table: "Suppliers",
                 column: "NIF",
                 unique: true,
                 filter: "[NIF] IS NOT NULL");

             migrationBuilder.CreateIndex(
                 name: "IX_Users_Username",
                 table: "Users",
                 column: "Username",
                 unique: true);
            */
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyClosings");

            migrationBuilder.DropTable(
                name: "DailyStockSnapshots");

            migrationBuilder.DropTable(
                name: "FuncionarioTurnos");

            migrationBuilder.DropTable(
                name: "HorarioUsuarios");

            migrationBuilder.DropTable(
                name: "InventoryMovements");

            migrationBuilder.DropTable(
                name: "OrderItems");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "ProductIngredients");

            migrationBuilder.DropTable(
                name: "PurchaseOrderItems");

            migrationBuilder.DropTable(
                name: "RestaurantConfigs");

            migrationBuilder.DropTable(
                name: "StockMovements");

            migrationBuilder.DropTable(
                name: "TaxasIVA");

            migrationBuilder.DropTable(
                name: "Turnos");

            migrationBuilder.DropTable(
                name: "VFDConfigs");

            migrationBuilder.DropTable(
                name: "WhatsAppApiConfigs");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "PurchaseOrders");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "Entregadores");

            migrationBuilder.DropTable(
                name: "Reservations");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Suppliers");

            migrationBuilder.DropTable(
                name: "Ingredients");

            migrationBuilder.DropTable(
                name: "ProductCategories");

            migrationBuilder.DropTable(
                name: "Promotions");

            migrationBuilder.DropTable(
                name: "Clientes");

            migrationBuilder.DropTable(
                name: "RestaurantTable");
        }
    }
}
