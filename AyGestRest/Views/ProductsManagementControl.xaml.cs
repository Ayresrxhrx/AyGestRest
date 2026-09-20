using AyGestRest.Data;
using AyGestRest.Models;
using MaterialDesignThemes.Wpf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Zen.Barcode;

namespace AyGestRest.Views
{
    public partial class ProductsManagementControl : UserControl
    {
        // Removido o contexto global - vamos usar factory pattern
        private Product? _currentProduct;
        private bool _isNewProduct;
        private ObservableCollection<ProductIngredient> _currentRecipe = new();
        private Ingredient? _currentIngredient;
        private Supplier? _currentSupplier;
        private static readonly SemaphoreSlim _printLock = new SemaphoreSlim(1, 1);

        private PurchaseOrder? _currentPurchase;
        private ObservableCollection<PurchaseOrderItem> _currentPurchaseItems = new();
        private ObservableCollection<PurchaseDisplayItem> _currentDisplayItems = new(); // NOVA: para exibição

        private readonly SnackbarMessageQueue _snackbarQueue = new SnackbarMessageQueue(TimeSpan.FromSeconds(6));

        public ProductsManagementControl()
        {
            try
            {
                Debug.WriteLine("🚀 ProductsManagementControl: Construtor iniciado");
                InitializeComponent();
                Loaded += OnLoaded;
                Debug.WriteLine("✅ ProductsManagementControl: Construtor concluído");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ ERRO NO CONSTRUTOR: {ex.Message}");
                MessageBox.Show($"Erro no construtor: {ex.Message}", "Erro Crítico",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ========== DbContext Factory Methods ==========
        private static AyGestRestContext CreateContext()
        {
            var context = new AyGestRestContext();
            // Desabilitar tracking para queries de leitura (melhor performance)
            context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            return context;
        }

        private static async Task<T> ExecuteDbAsync<T>(Func<AyGestRestContext, Task<T>> action)
        {
            using var context = new AyGestRestContext();
            return await action(context);
        }

        private static async Task ExecuteDbAsync(Func<AyGestRestContext, Task> action)
        {
            using var context = new AyGestRestContext();
            await action(context);
        }

        private static T ExecuteDb<T>(Func<AyGestRestContext, T> action)
        {
            using var context = new AyGestRestContext();
            return action(context);
        }

        private static void ExecuteDb(Action<AyGestRestContext> action)
        {
            using var context = new AyGestRestContext();
            action(context);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Debug.WriteLine("📌 OnLoaded: Iniciando carregamento...");

                // Verificar conexão com o banco
                TestDatabaseConnection();

                LoadCategories();
                LoadAllIngredients();
                LoadSuppliers();
                InitializeHistoryTab();
                LoadInventoryMovements();
                LoadPurchases();
                RefreshProducts();
                RefreshProductListControl();
                UpdateSummaryStatistics();
                UpdateStockValue();

                LoadBarcodeProducts();

                ProductStockHistoryCache.ClearCache();

                // Eventos para a nova tab
                TxtBarcodeCopies.TextChanged += (s, ev) => UpdateBarcodePreview();
                ChkIncludeDividers.Checked += (s, ev) => UpdateBarcodePreview();
                ChkIncludeDividers.Unchecked += (s, ev) => UpdateBarcodePreview();

                // Wire up form change events
                WireUpFormEvents();

                LoadBarcodeProducts();

                // Eventos para a tab de código de barras
                TxtBarcodeCopies.TextChanged += (s, ev) => UpdateBarcodePreview();
                TxtBarcodeWidth.TextChanged += (s, ev) => UpdateBarcodePreview();
                TxtBarcodeHeight.TextChanged += (s, ev) => UpdateBarcodePreview();
                ChkIncludeDividers.Checked += (s, ev) => UpdateBarcodePreview();
                ChkIncludeDividers.Unchecked += (s, ev) => UpdateBarcodePreview();
                ChkPrintToKitchen.Checked += ChkPrintToKitchen_Changed;
                ChkPrintToKitchen.Unchecked += ChkPrintToKitchen_Changed;

                // Carregar dados iniciais para preview
                if (Application.Current.Dispatcher != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdateBarcodePreview();
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
                if (ProductPopup.IsVisible)
                {
                    LoadKitchenPrinterInfo();
                }
                // Configurar data atual nos filtros
                DpInventoryFrom.SelectedDate = DateTime.Now.AddMonths(-1);
                DpInventoryTo.SelectedDate = DateTime.Now;
                DpPurchaseFrom.SelectedDate = DateTime.Now.AddMonths(-1);
                DpPurchaseTo.SelectedDate = DateTime.Now;

                // Inicializar contadores
                UpdateCounters();

                // Conectar os eventos dos filtros
                CmbFilterCategory.SelectionChanged += (s, ev) => RefreshProducts();
                CmbFilterStatus.SelectionChanged += (s, ev) => RefreshProducts();
                CmbFilterStock.SelectionChanged += (s, ev) => RefreshProducts();

                // Filtros de inventário
                CmbInventoryFilterType.SelectionChanged += (s, ev) => LoadInventoryMovements();
                CmbInventoryFilterProduct.SelectionChanged += (s, ev) => LoadInventoryMovements();
                DpInventoryFrom.SelectedDateChanged += (s, ev) => LoadInventoryMovements();
                DpInventoryTo.SelectedDateChanged += (s, ev) => LoadInventoryMovements();

                // Filtros de compras
                CmbPurchaseFilterStatus.SelectionChanged += (s, ev) => LoadPurchases();
                CmbPurchaseFilterSupplier.SelectionChanged += (s, ev) => LoadPurchases();
                DpPurchaseFrom.SelectedDateChanged += (s, ev) => LoadPurchases();
                DpPurchaseTo.SelectedDateChanged += (s, ev) => LoadPurchases();

                Debug.WriteLine("✅ OnLoaded: Concluído com sucesso");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ ERRO NO ONLOADED: {ex.Message}");
                Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                MessageBox.Show($"Erro ao inicializar: {ex.Message}\n\n{ex.StackTrace}",
                    "Erro Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TestDatabaseConnection()
        {
            try
            {
                Debug.WriteLine("🔍 Testando conexão com o banco de dados...");

                ExecuteDb(context =>
                {
                    // Testar se consegue conectar
                    var canConnect = context.Database.CanConnect();
                    Debug.WriteLine($"✅ Pode conectar: {canConnect}");

                    if (!canConnect)
                    {
                        MessageBox.Show("Não foi possível conectar ao banco de dados!",
                            "Erro de Conexão", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // Testar se consegue ler produtos
                    var productCount = context.Products.Count();
                    Debug.WriteLine($"📊 Total de produtos no banco: {productCount}");

                    // Testar se consegue ler categorias
                    var categoryCount = context.ProductCategories.Count();
                    Debug.WriteLine($"📊 Total de categorias no banco: {categoryCount}");

                    // Testar se consegue ler ingredientes
                    var ingredientCount = context.Ingredients.Count();
                    Debug.WriteLine($"📊 Total de ingredientes no banco: {ingredientCount}");
                });

                Debug.WriteLine("✅ Testes de banco de dados concluídos");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ ERRO NO TESTE DE CONEXÃO: {ex.Message}");
                throw;
            }
        }

        private void WireUpFormEvents()
        {
            try
            {
                Debug.WriteLine("🔌 Conectando eventos do formulário...");

                // Remover handlers existentes para evitar duplicatas
                TxtName.TextChanged -= OnFormChanged;
                TxtSalePrice.TextChanged -= OnFormChanged;
                CmbCategory.SelectionChanged -= OnFormChanged;
                ChkIsComposite.Checked -= OnFormChanged;
                ChkIsComposite.Unchecked -= OnFormChanged;
                TxtCode.TextChanged -= OnFormChanged;
                TxtStock.TextChanged -= OnFormChanged;
                ChkEnableBarcode.Checked -= OnFormChanged;
                ChkEnableBarcode.Unchecked -= OnFormChanged;
                TxtBarcode.TextChanged -= OnFormChanged;
                TxtDescription.TextChanged -= OnFormChanged;
                ChkActive.Checked -= OnFormChanged;
                ChkActive.Unchecked -= OnFormChanged;
                ChkPrintToKitchen.Checked -= OnFormChanged;
                ChkPrintToKitchen.Unchecked -= OnFormChanged;

                // Adicionar handlers
                TxtName.TextChanged += OnFormChanged;
                TxtSalePrice.TextChanged += OnFormChanged;
                CmbCategory.SelectionChanged += OnFormChanged;
                ChkIsComposite.Checked += OnFormChanged;
                ChkIsComposite.Unchecked += OnFormChanged;
                TxtCode.TextChanged += OnFormChanged;
                TxtStock.TextChanged += OnFormChanged;
                ChkEnableBarcode.Checked += OnFormChanged;
                ChkEnableBarcode.Unchecked += OnFormChanged;
                TxtBarcode.TextChanged += OnFormChanged;
                TxtDescription.TextChanged += OnFormChanged;
                ChkActive.Checked += OnFormChanged;
                ChkActive.Unchecked += OnFormChanged;
                ChkPrintToKitchen.Checked += OnFormChanged;
                ChkPrintToKitchen.Unchecked += OnFormChanged;

                Debug.WriteLine("✅ Eventos do formulário conectados");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro ao conectar eventos: {ex.Message}");
            }
        }

        private void OnFormChanged(object sender, EventArgs e)
        {
            try
            {
                bool hasRequiredFields = !string.IsNullOrWhiteSpace(TxtName.Text) &&
                                         !string.IsNullOrWhiteSpace(TxtSalePrice.Text) &&
                                         CmbCategory.SelectedItem != null;

                if (_isNewProduct)
                {
                    BtnSave.IsEnabled = hasRequiredFields;
                }
                else
                {
                    BtnSave.IsEnabled = hasRequiredFields && HasFormChanged();
                }

                Debug.WriteLine($"OnFormChanged: _isNewProduct={_isNewProduct}, hasRequiredFields={hasRequiredFields}, BtnSave.IsEnabled={BtnSave.IsEnabled}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no OnFormChanged: {ex.Message}");
            }
        }

        private bool HasFormChanged()
        {
            try
            {
                if (_currentProduct == null) return true;

                bool changed = TxtName.Text != _currentProduct.Name ||
                               TxtCode.Text != (_currentProduct.Code ?? "") ||
                               TxtDescription.Text != (_currentProduct.Description ?? "") ||
                               TxtSalePrice.Text != _currentProduct.Price.ToString("N2", CultureInfo.GetCultureInfo("pt-MZ")) ||
                               TxtStock.Text != _currentProduct.Stock.ToString("N2") ||
                               (int?)CmbCategory.SelectedValue != _currentProduct.CategoryId ||
                               (ChkActive.IsChecked ?? true) != _currentProduct.Active ||
                               (ChkIsComposite.IsChecked ?? false) != _currentProduct.IsComposite ||
                               (ChkPrintToKitchen.IsChecked ?? true) != _currentProduct.PrintToKitchen ||
                               (ChkEnableBarcode.IsChecked == true ? TxtBarcode.Text : null) != _currentProduct.Barcode;

                Debug.WriteLine($"HasFormChanged: {changed}");
                return changed;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no HasFormChanged: {ex.Message}");
                return true;
            }
        }

        private async void ChkPrintToKitchen_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                var config = await ExecuteDbAsync(async context =>
                    await context.RestaurantConfigs.FirstOrDefaultAsync());

                bool isChecked = ChkPrintToKitchen.IsChecked ?? false;

                if (isChecked && (config == null || string.IsNullOrEmpty(config.KitchenPrinterName)))
                {
                    var warningBorder = TxtKitchenPrinterInfo.Parent as Border;
                    if (warningBorder != null)
                    {
                        warningBorder.Background = System.Windows.Media.Brushes.LightGoldenrodYellow;
                        warningBorder.BorderBrush = System.Windows.Media.Brushes.Orange;
                        warningBorder.BorderThickness = new Thickness(1);

                        TxtKitchenPrinterInfo.Text = "⚠️ ATENÇÃO: Nenhuma impressora configurada!\nO produto será salvo mas não imprimirá.";
                        TxtKitchenPrinterInfo.Foreground = System.Windows.Media.Brushes.DarkOrange;
                    }

                    ChkPrintToKitchen.ToolTip = "Configure uma impressora na cozinha para que este produto seja impresso quando pedido";
                }
                else if (!isChecked && config != null && !string.IsNullOrEmpty(config.KitchenPrinterName))
                {
                    var warningBorder = TxtKitchenPrinterInfo.Parent as Border;
                    if (warningBorder != null)
                    {
                        warningBorder.Background = System.Windows.Media.Brushes.LightCyan;
                        warningBorder.BorderBrush = System.Windows.Media.Brushes.LightBlue;
                        warningBorder.BorderThickness = new Thickness(1);
                    }

                    TxtKitchenPrinterInfo.Text = $"❌ Não será impresso em: {config.KitchenPrinterName}";
                    TxtKitchenPrinterInfo.Foreground = System.Windows.Media.Brushes.Gray;
                    ChkPrintToKitchen.ToolTip = "Este produto NÃO será impresso na cozinha quando pedido";
                }
                else if (isChecked && config != null && !string.IsNullOrEmpty(config.KitchenPrinterName))
                {
                    var warningBorder = TxtKitchenPrinterInfo.Parent as Border;
                    if (warningBorder != null)
                    {
                        warningBorder.Background = System.Windows.Media.Brushes.LightGreen;
                        warningBorder.BorderBrush = System.Windows.Media.Brushes.Green;
                        warningBorder.BorderThickness = new Thickness(1);
                    }

                    TxtKitchenPrinterInfo.Text = $"✅ Será impresso em: {config.KitchenPrinterName}";
                    TxtKitchenPrinterInfo.Foreground = System.Windows.Media.Brushes.Green;
                    ChkPrintToKitchen.ToolTip = $"Será impresso em: {config.KitchenPrinterName} quando pedido";
                }

                OnFormChanged(sender, e);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no ChkPrintToKitchen_Changed: {ex.Message}");
            }
        }

        private async void LoadKitchenPrinterInfo()
        {
            try
            {
                var config = await ExecuteDbAsync(async context =>
                    await context.RestaurantConfigs.FirstOrDefaultAsync());

                if (config != null)
                {
                    string printerInfo;

                    if (string.IsNullOrEmpty(config.KitchenPrinterName))
                    {
                        printerInfo = "⚠️ Nenhuma impressora da cozinha configurada";
                        ChkPrintToKitchen.IsEnabled = true;
                        TxtKitchenPrinterInfo.Foreground = System.Windows.Media.Brushes.OrangeRed;
                        ChkPrintToKitchen.ToolTip = "Configure uma impressora na cozinha para usar esta funcionalidade";
                    }
                    else
                    {
                        printerInfo = $"✅ Impressora da cozinha: {config.KitchenPrinterName}";
                        ChkPrintToKitchen.IsEnabled = true;
                        TxtKitchenPrinterInfo.Foreground = System.Windows.Media.Brushes.Green;
                        ChkPrintToKitchen.ToolTip = $"Será impresso em: {config.KitchenPrinterName} quando selecionado";
                    }

                    TxtKitchenPrinterInfo.Text = printerInfo;
                }
                else
                {
                    TxtKitchenPrinterInfo.Text = "⚠️ Configuração não encontrada";
                    ChkPrintToKitchen.IsEnabled = true;
                    TxtKitchenPrinterInfo.Foreground = System.Windows.Media.Brushes.OrangeRed;
                    ChkPrintToKitchen.ToolTip = "Configure o restaurante primeiro";
                }
            }
            catch (Exception ex)
            {
                TxtKitchenPrinterInfo.Text = "Erro ao carregar configuração";
                TxtKitchenPrinterInfo.Foreground = System.Windows.Media.Brushes.Red;
                ChkPrintToKitchen.IsEnabled = true;
                ChkPrintToKitchen.ToolTip = "Erro ao carregar configuração da impressora";
            }
        }

        private void BtnGenerateBarcode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(TxtCode.Text))
                {
                    ShowErrorMessage("Digite um código para o produto primeiro.");
                    TxtCode.Focus();
                    return;
                }

                if (!decimal.TryParse(TxtSalePrice.Text.Replace(",", "."),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price) || price <= 0)
                {
                    ShowErrorMessage("Defina um preço válido primeiro.");
                    TxtSalePrice.Focus();
                    return;
                }

                string generatedBarcode = GenerateBarcodeValue(TxtCode.Text.Trim(), price);
                TxtBarcode.Text = generatedBarcode;
                ChkEnableBarcode.IsChecked = true;

                ShowSuccessMessage($"Código gerado: {generatedBarcode}");
                OnFormChanged(sender, e);
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Erro ao gerar código: {ex.Message}");
            }
        }

        // ========== PRODUTOS ==========
        private void LoadCategories()
        {
            ExecuteDb(context =>
            {
                try
                {
                    var categories = context.ProductCategories
                        .Where(c => c.Ativo)
                        .OrderBy(c => c.Nome)
                        .ToList();

                    Dispatcher.Invoke(() =>
                    {
                        CmbCategory.ItemsSource = categories;
                        CmbFilterCategory.ItemsSource = categories;

                        var products = context.Products.Where(p => p.Active).OrderBy(p => p.Name).ToList();
                        CmbMovementProduct.ItemsSource = products;

                        // CORREÇÃO: Carregar PRODUTOS E INGREDIENTES para o combobox de compra
                        var purchaseItems = new List<PurchaseItem>();

                        // Adicionar produtos
                        foreach (var p in products)
                        {
                            purchaseItems.Add(new PurchaseItem
                            {
                                Id = p.Id,
                                Name = p.Name,
                                Type = "Produto",
                                Icon = "📦",
                                IsProduct = true
                            });
                        }

                        // Adicionar ingredientes
                        var ingredients = context.Ingredients.OrderBy(i => i.Name).ToList();
                        foreach (var i in ingredients)
                        {
                            purchaseItems.Add(new PurchaseItem
                            {
                                Id = i.Id,
                                Name = i.Name,
                                Type = "Ingrediente",
                                Icon = "🧪",
                                IsProduct = false
                            });
                        }

                        // Ordenar por nome
                        purchaseItems = purchaseItems.OrderBy(x => x.Name).ToList();

                        CmbPurchaseProduct.ItemsSource = purchaseItems;
                        CmbPurchaseProduct.DisplayMemberPath = "DisplayName";
                        CmbPurchaseProduct.SelectedValuePath = "Id";

                        CmbInventoryFilterProduct.ItemsSource = context.Products.OrderBy(p => p.Name).ToList();
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => ShowErrorMessage($"Erro ao carregar categorias: {ex.Message}"));
                }
            });
        }

        private async void LoadAllIngredients()
        {
            await ExecuteDbAsync(async context =>
            {
                try
                {
                    await Dispatcher.InvokeAsync(() => Mouse.OverrideCursor = Cursors.Wait);

                    List<Ingredient> ingredients;

                    try
                    {
                        ingredients = await context.Ingredients
                            .Include(i => i.ProductIngredients)
                            .ThenInclude(pi => pi.Product)
                            .OrderBy(i => i.Name)
                            .ToListAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Erro ao carregar com includes: {ex.Message}");
                        ingredients = await context.Ingredients
                            .OrderBy(i => i.Name)
                            .ToListAsync();
                    }

                    Debug.WriteLine($"✅ Carregados {ingredients.Count} ingredientes");

                    await Dispatcher.InvokeAsync(() =>
                    {
                        DgIngredients.ItemsSource = ingredients;
                        UpdateCounters();
                        CmbAddIngredient.ItemsSource = ingredients;
                        CmbAddIngredient.DisplayMemberPath = "Name";
                        CmbAddIngredient.SelectedValuePath = "Id";

                        TxtIngredientCount.Text = $" ({ingredients.Count})";

                        ShowSuccessMessage($"Carregados {ingredients.Count} ingredientes");
                        Mouse.OverrideCursor = null;
                    });
                }
                catch (Exception ex)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        ShowErrorMessage($"Erro ao carregar ingredientes: {ex.Message}");
                        Mouse.OverrideCursor = null;
                    });
                    Debug.WriteLine($"❌ Erro detalhado: {ex.ToString()}");
                }
            });
        }

        private void RefreshProducts()
        {
            ExecuteDb(context =>
            {
                try
                {
                    var query = context.Products
                        .Include(p => p.Category)
                        .AsQueryable();

                    // Aplicar filtros
                    Dispatcher.Invoke(() =>
                    {
                        if (CmbFilterCategory.SelectedItem is ProductCategory selectedCategory)
                        {
                            query = query.Where(p => p.CategoryId == selectedCategory.Id);
                        }

                        if (CmbFilterStatus.SelectedItem is ComboBoxItem statusItem)
                        {
                            var statusText = statusItem.Content?.ToString();
                            if (statusText == "Ativos")
                                query = query.Where(p => p.Active);
                            else if (statusText == "Inativos")
                                query = query.Where(p => !p.Active);
                        }

                        if (CmbFilterStock.SelectedItem is ComboBoxItem stockItem)
                        {
                            var stockText = stockItem.Content?.ToString();
                            if (stockText == "Sem stock")
                                query = query.Where(p => p.Stock == 0);
                            else if (stockText == "Stock baixo (<10)")
                                query = query.Where(p => p.Stock > 0 && p.Stock < 10);
                            else if (stockText == "Stock normal")
                                query = query.Where(p => p.Stock >= 10);
                        }

                        if (!string.IsNullOrWhiteSpace(TxtSearch.Text))
                        {
                            var searchTerm = TxtSearch.Text.ToLower();
                            query = query.Where(p =>
                                p.Name.ToLower().Contains(searchTerm) ||
                                (p.Code ?? "").ToLower().Contains(searchTerm) ||
                                (p.Barcode ?? "").ToLower().Contains(searchTerm) ||
                                (p.Description ?? "").ToLower().Contains(searchTerm));
                        }
                    });

                    List<Product> products;

                    try
                    {
                        products = query
                            .Include(p => p.ProductIngredients)
                            .ThenInclude(pi => pi.Ingredient)
                            .OrderBy(p => p.Name)
                            .ToList();
                    }
                    catch
                    {
                        products = query
                            .OrderBy(p => p.Name)
                            .ToList();
                    }

                    Dispatcher.Invoke(() =>
                    {
                        ProductsListControl.ItemsSource = products;
                        ProductsListControl.Items.Refresh();

                        UpdateProductCounters(products);
                        UpdateSummaryStatistics(products);
                        TxtProductCount.Text = $" ({products.Count})";
                    });

                    Debug.WriteLine($"✅ Produtos carregados: {products.Count}");
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => ShowErrorMessage($"Erro ao carregar produtos: {ex.Message}"));
                    Debug.WriteLine($"❌ Erro detalhado: {ex.ToString()}");
                }
            });
        }

        private void RefreshProductListControl()
        {
            ExecuteDb(context =>
            {
                try
                {
                    var products = context.Products
                        .Include(p => p.Category)
                        .Include(p => p.ProductIngredients)
                        .ThenInclude(pi => pi.Ingredient)
                        .OrderBy(p => p.Name)
                        .ToList();

                    Dispatcher.Invoke(() =>
                    {
                        ProductsListControl.ItemsSource = products;
                        UpdateProductCounters(products);
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => ShowErrorMessage($"Erro ao atualizar lista de produtos: {ex.Message}"));
                }
            });
        }

        private void UpdateProductCounters(List<Product> products = null)
        {
            ExecuteDb(context =>
            {
                try
                {
                    products ??= context.Products.ToList();
                    var ingredientsCount = context.Ingredients.Count();

                    Dispatcher.Invoke(() =>
                    {
                        TxtProductCount.Text = $" ({products.Count})";
                        TxtTotalProducts.Text = products.Count.ToString();
                        TxtTotalIngredients.Text = ingredientsCount.ToString();

                        var barcodeCount = products.Count(p => !string.IsNullOrWhiteSpace(p.Barcode));
                        TxtBarcodeProducts.Text = $"{barcodeCount} com código de barras";
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => ShowErrorMessage($"Erro ao atualizar contadores: {ex.Message}"));
                }
            });
        }

        private void UpdateSummaryStatistics(List<Product> products = null)
        {
            ExecuteDb(context =>
            {
                try
                {
                    products ??= context.Products.ToList();

                    var activeCount = products.Count(p => p.Active);
                    var lowStockCount = products.Count(p => p.Stock > 0 && p.Stock < 10);
                    var outOfStockCount = products.Count(p => p.Stock == 0);

                    Dispatcher.Invoke(() =>
                    {
                        TxtActiveProducts.Text = $"{activeCount} ativos";
                        TxtLowStockProducts.Text = $"{lowStockCount} com stock baixo";
                        TxtOutOfStockProducts.Text = $"{outOfStockCount} sem stock";
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => ShowErrorMessage($"Erro ao atualizar estatísticas: {ex.Message}"));
                }
            });
        }

        private void UpdateStockValue()
        {
            ExecuteDb(context =>
            {
                try
                {
                    var totalValue = context.Products.Sum(p => p.Stock * p.Price);
                    Dispatcher.Invoke(() => TxtStockValue.Text = $"{totalValue:N2} MTn");
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => ShowErrorMessage($"Erro ao calcular valor do stock: {ex.Message}"));
                }
            });
        }

        private void UpdateCounters()
        {
            ExecuteDb(context =>
            {
                try
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtTotalProducts.Text = context.Products.Count().ToString();
                        TxtTotalIngredients.Text = context.Ingredients.Count().ToString();
                        TxtSupplierCount.Text = $" ({context.Suppliers.Count()})";
                        TxtMovementCount.Text = $" ({context.InventoryMovements.Count()})";
                        TxtPurchaseCount.Text = $" ({context.PurchaseOrders.Count()})";
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => ShowErrorMessage($"Erro ao atualizar contadores: {ex.Message}"));
                }
            });
        }

        private void BtnClearFilters_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                CmbFilterCategory.SelectedIndex = -1;
                CmbFilterStatus.SelectedIndex = 0;
                CmbFilterStock.SelectedIndex = 0;
                TxtSearch.Text = "";

                CmbInventoryFilterType.SelectedIndex = 0;
                CmbInventoryFilterProduct.SelectedIndex = -1;
                DpInventoryFrom.SelectedDate = DateTime.Now.AddMonths(-1);
                DpInventoryTo.SelectedDate = DateTime.Now;

                CmbPurchaseFilterStatus.SelectedIndex = 0;
                CmbPurchaseFilterSupplier.SelectedIndex = -1;
                DpPurchaseFrom.SelectedDate = DateTime.Now.AddMonths(-1);
                DpPurchaseTo.SelectedDate = DateTime.Now;

                RefreshProducts();
                LoadInventoryMovements();
                LoadPurchases();

                ShowSuccessMessage("Filtros limpos com sucesso!");
            });
        }

        private void ClearProductForm()
        {
            try
            {
                Debug.WriteLine("🧹 ClearProductForm: Limpando formulário");
                _currentProduct = null;
                _isNewProduct = false;
                _currentRecipe.Clear();

                TxtName.Clear();
                TxtCode.Clear();
                TxtDescription.Clear();
                TxtSalePrice.Text = "0,00";
                TxtStock.Clear();
                CmbCategory.SelectedIndex = -1;
                ChkActive.IsChecked = true;
                ChkIsComposite.IsChecked = false;
                ChkPrintToKitchen.IsChecked = true;
                ChkEnableBarcode.IsChecked = false;
                TxtBarcode.Clear();
                BarcodeGrid.Visibility = Visibility.Collapsed;
                ImgProduct.Source = null;

                CardRecipe.Visibility = Visibility.Collapsed;
                DgRecipe.ItemsSource = _currentRecipe;
                UpdateRecipeTotalCost();

                TxtFormTitle.Text = "Novo Produto";
                TxtKitchenPrinterInfo.Text = "Carregando...";
                TxtKitchenPrinterInfo.Foreground = System.Windows.Media.Brushes.Gray;

                BtnSave.IsEnabled = false;
                Debug.WriteLine("✅ ClearProductForm: Concluído");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro no ClearProductForm: {ex.Message}");
            }
        }

        private void FillProductForm(Product product)
        {
            try
            {
                Debug.WriteLine($"📋 FillProductForm: Carregando produto '{product.Name}' (ID: {product.Id})");
                _currentProduct = product;
                _isNewProduct = false;

                TxtName.Text = product.Name;
                TxtCode.Text = product.Code ?? "";
                TxtDescription.Text = product.Description ?? "";
                TxtSalePrice.Text = product.Price.ToString("N2", CultureInfo.GetCultureInfo("pt-MZ"));
                TxtStock.Text = product.Stock.ToString("N2");
                CmbCategory.SelectedValue = product.CategoryId;
                ChkActive.IsChecked = product.Active;
                ChkIsComposite.IsChecked = product.IsComposite;
                ChkPrintToKitchen.IsChecked = product.PrintToKitchen;

                if (!string.IsNullOrWhiteSpace(product.Barcode))
                {
                    ChkEnableBarcode.IsChecked = true;
                    TxtBarcode.Text = product.Barcode;
                    BarcodeGrid.Visibility = Visibility.Visible;
                }
                else
                {
                    ChkEnableBarcode.IsChecked = false;
                    TxtBarcode.Clear();
                    BarcodeGrid.Visibility = Visibility.Collapsed;
                }

                TxtFormTitle.Text = $"Editando: {product.Name}";

                if (product.IsComposite)
                {
                    _currentRecipe.Clear();
                    foreach (var pi in product.ProductIngredients)
                    {
                        _currentRecipe.Add(pi);
                        Debug.WriteLine($"  - Ingrediente carregado: {pi.Ingredient?.Name} (Qtd: {pi.QuantityUsed})");
                    }
                }
                else
                {
                    _currentRecipe.Clear();
                }

                DgRecipe.ItemsSource = _currentRecipe;
                CardRecipe.Visibility = product.IsComposite ? Visibility.Visible : Visibility.Collapsed;
                UpdateRecipeTotalCost();

                if (product.Image != null && product.Image.Length > 0)
                {
                    try
                    {
                        using var ms = new MemoryStream(product.Image);
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.StreamSource = ms;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        ImgProduct.Source = bitmap;
                        Debug.WriteLine("✅ Imagem carregada");
                    }
                    catch (Exception imgEx)
                    {
                        Debug.WriteLine($"⚠️ Erro ao carregar imagem: {imgEx.Message}");
                        ImgProduct.Source = null;
                    }
                }
                else
                {
                    ImgProduct.Source = null;
                }

                LoadKitchenPrinterInfo();
                BtnSave.IsEnabled = false;
                Debug.WriteLine("✅ FillProductForm: Concluído");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro no FillProductForm: {ex.Message}");
                ShowErrorMessage($"Erro ao carregar produto: {ex.Message}");
            }
        }

        private void BtnNew_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Debug.WriteLine("➕ BtnNew_Click: Iniciando criação de novo produto");
                ClearProductForm();
                _isNewProduct = true;

                OnFormChanged(sender, e);

                OverlayGrid.Visibility = Visibility.Visible;
                ProductPopup.Visibility = Visibility.Visible;

                TxtName.Focus();
                ShowSuccessMessage("Preencha os dados do novo produto.");
                Debug.WriteLine("✅ BtnNew_Click: Concluído");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro no BtnNew_Click: {ex.Message}");
                MessageBox.Show($"Erro ao abrir formulário: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Debug.WriteLine("💾 BtnSave_Click: INICIANDO SALVAMENTO");

                // Validações
                if (string.IsNullOrWhiteSpace(TxtName.Text))
                {
                    ShowErrorMessage("Nome do produto é obrigatório.");
                    TxtName.Focus();
                    return;
                }

                string priceText = TxtSalePrice.Text.Replace(",", ".").Trim();
                if (!decimal.TryParse(priceText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price) || price <= 0)
                {
                    ShowErrorMessage("Preço de venda inválido.");
                    TxtSalePrice.Focus();
                    return;
                }

                if (CmbCategory.SelectedValue == null)
                {
                    ShowErrorMessage("Selecione uma categoria.");
                    CmbCategory.Focus();
                    return;
                }

                decimal stock = 0;
                string stockText = TxtStock.Text.Replace(",", ".");
                if (!string.IsNullOrWhiteSpace(stockText))
                    decimal.TryParse(stockText, NumberStyles.Any, CultureInfo.InvariantCulture, out stock);

                string barcode = null;
                if (ChkEnableBarcode.IsChecked == true)
                {
                    barcode = TxtBarcode.Text?.Trim();
                    if (string.IsNullOrWhiteSpace(barcode))
                    {
                        ShowErrorMessage("Código de barras é obrigatório quando ativado.");
                        TxtBarcode.Focus();
                        return;
                    }
                }

                bool isNew = _currentProduct == null;

                using var context = new AyGestRestContext();

                if (isNew)
                {
                    // 🟢 SQL COMPLETO com TODOS os campos obrigatórios
                    string sql = @"
                INSERT INTO Products (
                    Name, Code, Description, Price, Stock, 
                    Active, IsComposite, CategoryId, Barcode, PrintToKitchen,
                    Unit, Featured, Promo, TrackInventory
                ) VALUES (
                    {0}, {1}, {2}, {3}, {4}, 
                    1, {5}, {6}, {7}, {8},
                    'un', 0, 0, 1
                )";

                    int result = await context.Database.ExecuteSqlRawAsync(sql,
                        TxtName.Text.Trim(),
                        TxtCode.Text.Trim() ?? "",
                        TxtDescription.Text.Trim() ?? "",
                        price,
                        stock,
                        ChkIsComposite.IsChecked ?? false,
                        (int)CmbCategory.SelectedValue,
                        barcode,
                        ChkPrintToKitchen.IsChecked ?? true
                    );

                    Debug.WriteLine($"✅ Produto inserido via SQL direto. Resultado: {result}");
                }
                else
                {
                    // 🟢 UPDATE com todos os campos
                    string sql = @"
                UPDATE Products SET
                    Name = {0},
                    Code = {1},
                    Description = {2},
                    Price = {3},
                    Stock = {4},
                    Active = 1,
                    IsComposite = {5},
                    CategoryId = {6},
                    Barcode = {7},
                    PrintToKitchen = {8},
                    Unit = 'un',
                    Featured = 0,
                    Promo = 0,
                    TrackInventory = 1
                WHERE Id = {9}";

                    int result = await context.Database.ExecuteSqlRawAsync(sql,
                        TxtName.Text.Trim(),
                        TxtCode.Text.Trim() ?? "",
                        TxtDescription.Text.Trim() ?? "",
                        price,
                        stock,
                        ChkIsComposite.IsChecked ?? false,
                        (int)CmbCategory.SelectedValue,
                        barcode,
                        ChkPrintToKitchen.IsChecked ?? true,
                        _currentProduct.Id
                    );

                    Debug.WriteLine($"✅ Produto atualizado via SQL direto. Resultado: {result}");
                }

                // ========== GERENCIAR RECEITA ==========
                if (ChkIsComposite.IsChecked == true && _currentRecipe.Any())
                {
                    // Obter o ID do produto
                    int productId;
                    if (isNew)
                    {
                        // Buscar o último ID inserido
                        productId = await context.Products
                            .OrderByDescending(p => p.Id)
                            .Select(p => p.Id)
                            .FirstOrDefaultAsync();
                    }
                    else
                    {
                        productId = _currentProduct.Id;
                    }

                    // Remover receitas antigas
                    await context.Database.ExecuteSqlRawAsync(
                        "DELETE FROM ProductIngredients WHERE ProductId = {0}", productId);

                    // Inserir novas receitas
                    foreach (var pi in _currentRecipe)
                    {
                        await context.Database.ExecuteSqlRawAsync(@"
                    INSERT INTO ProductIngredients (ProductId, IngredientId, QuantityUsed)
                    VALUES ({0}, {1}, {2})",
                            productId, pi.IngredientId, pi.QuantityUsed);
                    }

                    Debug.WriteLine($"✅ Receita salva com {_currentRecipe.Count} ingredientes");
                }

                // Recarregar o produto para atualizar o _currentProduct
                if (isNew)
                {
                    var newProduct = await context.Products
                        .Include(p => p.ProductIngredients)
                        .ThenInclude(pi => pi.Ingredient)
                        .OrderByDescending(p => p.Id)
                        .FirstOrDefaultAsync();
                    _currentProduct = newProduct;
                }

                // Atualizar UI
                if (_currentProduct != null)
                {
                    FillProductForm(_currentProduct);
                }

                OverlayGrid.Visibility = Visibility.Collapsed;
                ProductPopup.Visibility = Visibility.Collapsed;

                RefreshProducts();
                RefreshProductListControl();
                LoadCategories();
                UpdateStockValue();

                ShowSuccessMessage($"Produto salvo com sucesso!");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ ERRO: {ex.Message}");
                Debug.WriteLine($"Stack: {ex.StackTrace}");
                ShowErrorMessage($"Erro: {ex.Message}");
                MessageBox.Show($"Erro detalhado:\n{ex.Message}\n\n{ex.StackTrace}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void ShowDebugMessage(string message)
        {
            Debug.WriteLine($"🔍 DEBUG: {message}");
        }

        private void ChkEnableBarcode_Changed(object sender, RoutedEventArgs e)
        {
            BarcodeGrid.Visibility = ChkEnableBarcode.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            OnFormChanged(sender, e);
        }

        private void BtnScanBarcode_Click(object sender, RoutedEventArgs e)
        {
            Random random = new Random();
            string barcode = random.Next(100000000, 999999999).ToString();
            TxtBarcode.Text = barcode;
            ShowSuccessMessage($"Código de barras simulado: {barcode}");
            OnFormChanged(sender, e);
        }

        // ========== MÉTODOS DE EXPORTAÇÃO PDF ==========
        private void BtnExportProductsPDF_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new SaveFileDialog
                {
                    Filter = "Arquivo PDF|*.pdf",
                    FileName = $"Relatorio_Produtos_{DateTime.Now:yyyyMMdd_HHmm}.pdf",
                    DefaultExt = ".pdf"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    ExecuteDb(context =>
                    {
                        var products = context.Products
                            .Include(p => p.Category)
                            .Include(p => p.ProductIngredients)
                            .ThenInclude(pi => pi.Ingredient)
                            .OrderBy(p => p.Name)
                            .ToList();

                        var headers = new List<string>
                        {
                            "Nome",
                            "Código",
                            "Categoria",
                            "Preço",
                            "Stock",
                            "Status",
                            "Tipo"
                        };

                        var dataRows = new List<List<string>>();
                        decimal totalStockValue = 0;

                        foreach (var product in products)
                        {
                            var stockValue = product.Stock * product.Price;
                            totalStockValue += stockValue;

                            dataRows.Add(new List<string>
                            {
                                product.Name,
                                product.Code ?? "-",
                                product.Category?.Nome ?? "-",
                                product.Price.ToString("N2") + " MTn",
                                product.Stock.ToString("N0"),
                                product.Active ? "Ativo" : "Inativo",
                                product.IsComposite ? "Composto" : "Simples"
                            });
                        }

                        var restaurantConfig = context.RestaurantConfigs.FirstOrDefault();
                        string restaurantName = restaurantConfig?.RestaurantName ?? "AyGestRest";
                        string currencySymbol = restaurantConfig?.CurrencySymbol ?? "MTn";
                        byte[] logoBytes = restaurantConfig?.LogoBytes;

                        string reportTitle = "RELATÓRIO DE PRODUTOS";
                        string dateRange = $"Período: Completo • Filtro: {GetCurrentFilterInfo()}";

                        var summaryRows = new List<string>
                        {
                            $"Total de Produtos: {products.Count}",
                            $"Produtos Ativos: {products.Count(p => p.Active)}",
                            $"Produtos Inativos: {products.Count(p => !p.Active)}",
                            $"Produtos Compostos: {products.Count(p => p.IsComposite)}",
                            $"Produtos Simples: {products.Count(p => !p.IsComposite)}",
                            $"Com Código de Barras: {products.Count(p => !string.IsNullOrEmpty(p.Barcode))}",
                            $"Valor Total do Stock: {currencySymbol} {totalStockValue:N2}"
                        };

                        Dispatcher.Invoke(() => Mouse.OverrideCursor = Cursors.Wait);

                        ExportToPdf(
                            saveDialog.FileName,
                            restaurantName,
                            reportTitle,
                            dateRange,
                            headers,
                            dataRows,
                            summaryRows,
                            currencySymbol,
                            logoBytes,
                            "products"
                        );

                        Dispatcher.Invoke(() =>
                        {
                            Mouse.OverrideCursor = null;
                            ShowSuccessMessage($"PDF exportado: {saveDialog.FileName}");
                        });
                    });

                    Process.Start(new ProcessStartInfo(saveDialog.FileName) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => Mouse.OverrideCursor = null);
                ShowErrorMessage($"Erro ao exportar PDF: {ex.Message}");
            }
        }

        private string GetCurrentFilterInfo()
        {
            var filters = new List<string>();

            if (CmbFilterCategory.SelectedItem is ProductCategory category)
                filters.Add($"Categoria: {category.Nome}");

            if (CmbFilterStatus.SelectedItem is ComboBoxItem statusItem)
                filters.Add($"Status: {statusItem.Content}");

            if (CmbFilterStock.SelectedItem is ComboBoxItem stockItem)
                filters.Add($"Stock: {stockItem.Content}");

            if (!string.IsNullOrWhiteSpace(TxtSearch.Text))
                filters.Add($"Busca: '{TxtSearch.Text}'");

            return filters.Count > 0 ? string.Join(" • ", filters) : "Todos os produtos";
        }

        private async void BtnExportPurchasesPDF_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new SaveFileDialog
                {
                    Filter = "Arquivo PDF|*.pdf",
                    FileName = $"Relatorio_Compras_{DateTime.Now:yyyyMMdd_HHmm}.pdf",
                    DefaultExt = ".pdf"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    await ExecuteDbAsync(async context =>
                    {
                        var purchases = await context.PurchaseOrders
                            .Include(p => p.Supplier)
                            .Include(p => p.Items)
                            .ThenInclude(i => i.Product)
                            .OrderByDescending(p => p.CreatedAt)
                            .ToListAsync();

                        var headers = new List<string>
                        {
                            "Número",
                            "Fornecedor",
                            "Data",
                            "Tipo",
                            "Status",
                            "Itens",
                            "Total"
                        };

                        var dataRows = new List<List<string>>();
                        decimal totalAmount = 0;

                        foreach (var purchase in purchases)
                        {
                            totalAmount += purchase.TotalAmount;

                            dataRows.Add(new List<string>
                            {
                                purchase.PurchaseNumber,
                                purchase.Supplier?.Name ?? "-",
                                purchase.CreatedAt.ToString("dd/MM/yyyy"),
                                purchase.PurchaseType,
                                purchase.Status,
                                purchase.Items?.Count.ToString() ?? "0",
                                purchase.TotalAmount.ToString("N2") + " MTn"
                            });
                        }

                        var restaurantConfig = await context.RestaurantConfigs.FirstOrDefaultAsync();
                        string restaurantName = restaurantConfig?.RestaurantName ?? "AyGestRest";
                        string currencySymbol = restaurantConfig?.CurrencySymbol ?? "MTn";
                        byte[] logoBytes = restaurantConfig?.LogoBytes;

                        string reportTitle = "RELATÓRIO DE COMPRAS";
                        string dateRange = $"{DpPurchaseFrom.SelectedDate:dd/MM/yyyy} a {DpPurchaseTo.SelectedDate:dd/MM/yyyy}";

                        var summaryRows = new List<string>
                        {
                            $"Total de Compras: {purchases.Count}",
                            $"Compras Pendentes: {purchases.Count(p => p.Status == "Pendente")}",
                            $"Compras Recebidas: {purchases.Count(p => p.Status == "Recebida")}",
                            $"Compras Canceladas: {purchases.Count(p => p.Status == "Cancelada")}",
                            $"Valor Total: {currencySymbol} {totalAmount:N2}",
                            $"Média por Compra: {currencySymbol} {(purchases.Count > 0 ? totalAmount / purchases.Count : 0):N2}"
                        };

                        await Dispatcher.InvokeAsync(() => Mouse.OverrideCursor = Cursors.Wait);

                        await Task.Run(() => ExportToPdf(
                            saveDialog.FileName,
                            restaurantName,
                            reportTitle,
                            dateRange,
                            headers,
                            dataRows,
                            summaryRows,
                            currencySymbol,
                            logoBytes,
                            "purchases"
                        ));

                        await Dispatcher.InvokeAsync(() =>
                        {
                            Mouse.OverrideCursor = null;
                            ShowSuccessMessage($"PDF exportado: {saveDialog.FileName}");
                        });
                    });

                    Process.Start(new ProcessStartInfo(saveDialog.FileName) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => Mouse.OverrideCursor = null);
                ShowErrorMessage($"Erro ao exportar PDF: {ex.Message}");
            }
        }

        private void ExportToPdf(string filePath, string restaurantName, string reportTitle,
            string dateRange, List<string> headers, List<List<string>> dataRows,
            List<string> summaryRows, string currencySymbol, byte[] logoBytes, string reportType)
        {
            // Método existente - mantido igual
            try
            {
                var pdf = new PdfDocument();
                pdf.Info.Title = reportTitle;
                pdf.Info.Author = restaurantName;
                pdf.Info.Subject = $"Relatório de {reportType.ToUpper()}";
                pdf.Info.CreationDate = DateTime.Now;

                var primaryColor = XColor.FromArgb(79, 70, 229);
                var accentColor = XColor.FromArgb(59, 130, 246);
                var successColor = XColor.FromArgb(16, 185, 129);
                var warningColor = XColor.FromArgb(245, 158, 11);
                var dangerColor = XColor.FromArgb(239, 68, 68);
                var lightGray = XColor.FromArgb(248, 250, 252);
                var mediumGray = XColor.FromArgb(226, 232, 240);
                var borderGray = XColor.FromArgb(203, 213, 225);
                var textGray = XColor.FromArgb(100, 116, 139);
                var darkText = XColor.FromArgb(30, 41, 59);

                string fontName = "Arial";
                var titleFont = new XFont(fontName, 22, XFontStyle.Bold);
                var companyNameFont = new XFont(fontName, 18, XFontStyle.Bold);
                var reportTitleFont = new XFont(fontName, 16, XFontStyle.Bold);
                var dateFont = new XFont(fontName, 10, XFontStyle.Regular);
                var headerFont = new XFont(fontName, 10, XFontStyle.Bold);
                var normalFont = new XFont(fontName, 9, XFontStyle.Regular);
                var boldFont = new XFont(fontName, 9, XFontStyle.Bold);
                var footerFont = new XFont(fontName, 8, XFontStyle.Italic);
                var summaryFont = new XFont(fontName, 10, XFontStyle.Bold);

                XGraphics gfx = null;
                PdfPage page = null;
                double y = 0;
                double margin = 30;

                double pageWidth = 595;
                double pageHeight = 842;

                double availablePageHeight = pageHeight - (margin * 2);
                double footerHeight = 30;

                void StartNewPage(bool isFirstPage = false)
                {
                    page = pdf.AddPage();
                    page.Width = pageWidth;
                    page.Height = pageHeight;
                    gfx = XGraphics.FromPdfPage(page);
                    y = margin;

                    if (!isFirstPage)
                    {
                        var pageHeaderFont = new XFont(fontName, 9, XFontStyle.Regular);
                        gfx.DrawString(restaurantName, pageHeaderFont, new XSolidBrush(primaryColor),
                            new XPoint(margin, margin - 10));
                        gfx.DrawString($"Página {pdf.PageCount}", pageHeaderFont, new XSolidBrush(textGray),
                            new XPoint(pageWidth - margin - 50, margin - 10));
                        gfx.DrawLine(new XPen(borderGray, 0.5), margin, margin - 2,
                            pageWidth - margin, margin - 2);
                        y = margin + 8;
                    }
                }

                StartNewPage(true);

                if (logoBytes != null && logoBytes.Length > 0)
                {
                    try
                    {
                        using (var ms = new MemoryStream(logoBytes))
                        {
                            var image = XImage.FromStream(ms);
                            double logoWidth = Math.Min(80, image.PixelWidth * 0.5);
                            double logoHeight = logoWidth * image.PixelHeight / image.PixelWidth;

                            double x = (pageWidth - logoWidth) / 2;
                            gfx.DrawImage(image, x, y, logoWidth, logoHeight);
                            y += logoHeight + 10;
                        }
                    }
                    catch
                    {
                        gfx.DrawString("🍽️", new XFont("Arial", 24),
                            new XSolidBrush(primaryColor), new XPoint(pageWidth / 2 - 12, y));
                        y += 30;
                    }
                }
                else
                {
                    gfx.DrawString("🍽️", new XFont("Arial", 24),
                        new XSolidBrush(primaryColor), new XPoint(pageWidth / 2 - 12, y));
                    y += 30;
                }

                var companyName = restaurantName.ToUpper();
                var companyNameSize = gfx.MeasureString(companyName, companyNameFont);
                gfx.DrawString(companyName, companyNameFont, new XSolidBrush(darkText),
                    new XPoint((pageWidth - companyNameSize.Width) / 2, y));
                y += 20;

                gfx.DrawLine(new XPen(primaryColor, 1.5), margin + 80, y,
                    pageWidth - margin - 80, y);
                y += 20;

                var titleSize = gfx.MeasureString(reportTitle, reportTitleFont);
                gfx.DrawString(reportTitle, reportTitleFont, new XSolidBrush(primaryColor),
                    new XPoint((pageWidth - titleSize.Width) / 2, y));
                y += 20;

                string periodInfo = $"📅 {dateRange} • ⏰ {DateTime.Now:dd/MM/yyyy HH:mm} • 📊 {dataRows.Count} registros";
                var periodSize = gfx.MeasureString(periodInfo, dateFont);

                if (periodSize.Width > pageWidth - 2 * margin)
                {
                    string part1 = $"📅 {dateRange} • ⏰ {DateTime.Now:dd/MM/yyyy HH:mm}";
                    string part2 = $"📊 {dataRows.Count} registros";

                    var part1Size = gfx.MeasureString(part1, dateFont);
                    gfx.DrawString(part1, dateFont, new XSolidBrush(textGray),
                        new XPoint((pageWidth - part1Size.Width) / 2, y));
                    y += 15;

                    var part2Size = gfx.MeasureString(part2, dateFont);
                    gfx.DrawString(part2, dateFont, new XSolidBrush(textGray),
                        new XPoint((pageWidth - part2Size.Width) / 2, y));
                    y += 20;
                }
                else
                {
                    gfx.DrawString(periodInfo, dateFont, new XSolidBrush(textGray),
                        new XPoint((pageWidth - periodSize.Width) / 2, y));
                    y += 25;
                }

                if (summaryRows != null && summaryRows.Any())
                {
                    double summaryWidth = pageWidth - 2 * margin;
                    double summaryItemHeight = 16;
                    double summaryHeight = 20 + (summaryRows.Count * summaryItemHeight);

                    if (y + summaryHeight > pageHeight - margin - footerHeight - 50)
                    {
                        StartNewPage();
                    }

                    var summaryRect = new XRect(margin, y, summaryWidth, summaryHeight);
                    gfx.DrawRoundedRectangle(
                        new XPen(mediumGray, 0.5),
                        new XSolidBrush(lightGray),
                        summaryRect,
                        new XSize(5, 5)
                    );

                    gfx.DrawString("📊 RESUMO", summaryFont, new XSolidBrush(primaryColor),
                        new XPoint(margin + 10, y + 15));

                    double summaryY = y + 30;
                    foreach (var item in summaryRows)
                    {
                        gfx.DrawString($"• {item}", normalFont, new XSolidBrush(darkText),
                            new XPoint(margin + 15, summaryY));
                        summaryY += summaryItemHeight;
                    }

                    y += summaryHeight + 15;
                }

                int colCount = headers.Count;
                double availableWidth = pageWidth - 2 * margin;

                double minColWidth = 40;
                double calculatedColWidth = Math.Max(minColWidth, availableWidth / colCount);

                bool isLandscape = false;
                if (colCount * calculatedColWidth > availableWidth * 0.9)
                {
                    isLandscape = true;
                    double temp = pageWidth;
                    pageWidth = pageHeight;
                    pageHeight = temp;

                    page = pdf.AddPage();
                    page.Width = pageWidth;
                    page.Height = pageHeight;
                    page.Orientation = PageOrientation.Landscape;
                    gfx = XGraphics.FromPdfPage(page);
                    y = margin;

                    availableWidth = pageWidth - 2 * margin;
                    calculatedColWidth = Math.Max(minColWidth, availableWidth / colCount);
                }

                double colWidth = calculatedColWidth;

                void DrawTableHeader()
                {
                    double x = margin;
                    double headerHeight = 25;

                    var headerRect = new XRect(margin, y, availableWidth, headerHeight);
                    gfx.DrawRoundedRectangle(
                        new XPen(XColors.White, 0.5),
                        new XSolidBrush(primaryColor),
                        headerRect,
                        new XSize(3, 3)
                    );

                    foreach (var header in headers)
                    {
                        string text = header;
                        var textSize = gfx.MeasureString(text, headerFont);

                        if (textSize.Width > colWidth - 6)
                        {
                            for (int len = text.Length; len > 0; len--)
                            {
                                text = header.Substring(0, len) + "...";
                                if (gfx.MeasureString(text, headerFont).Width <= colWidth - 6)
                                    break;
                            }
                        }

                        double textX = x + (colWidth - gfx.MeasureString(text, headerFont).Width) / 2;
                        double textY = y + (headerHeight - gfx.MeasureString(text, headerFont).Height) / 2;

                        gfx.DrawString(text, headerFont, XBrushes.White,
                            new XPoint(textX, textY));

                        if (x > margin)
                        {
                            gfx.DrawLine(new XPen(XColors.White, 0.3),
                                x, y + 3, x, y + headerHeight - 3);
                        }

                        x += colWidth;
                    }
                    y += headerHeight;
                }

                void DrawTableRow(List<string> row, bool isAlternate, int rowNumber)
                {
                    double x = margin;
                    var fillBrush = isAlternate ? new XSolidBrush(lightGray) : XBrushes.White;

                    double rowHeight = 22;
                    var rowRect = new XRect(margin, y, availableWidth, rowHeight);
                    gfx.DrawRectangle(fillBrush, rowRect);

                    for (int i = 0; i < row.Count && i < headers.Count; i++)
                    {
                        string value = row[i] ?? "-";
                        XBrush textBrush = new XSolidBrush(darkText);
                        XFont cellFont = normalFont;

                        if (decimal.TryParse(value.Replace(",", ".").Replace(" MTn", "").Trim(),
                            NumberStyles.Any, CultureInfo.InvariantCulture, out decimal numericValue))
                        {
                            bool isCurrency = headers[i].ToLower().Contains("valor") ||
                                             headers[i].ToLower().Contains("total") ||
                                             headers[i].ToLower().Contains("preço") ||
                                             headers[i].ToLower().Contains("montante");

                            bool isQuantity = headers[i].ToLower().Contains("quantidade") ||
                                             headers[i].ToLower().Contains("stock") ||
                                             headers[i].ToLower().Contains("itens");

                            if (isCurrency)
                            {
                                value = $"{currencySymbol} {numericValue:N2}";
                                textBrush = numericValue < 0 ? new XSolidBrush(dangerColor) : new XSolidBrush(successColor);
                            }
                            else if (isQuantity)
                            {
                                value = numericValue.ToString("N0");
                                textBrush = numericValue < 0 ? new XSolidBrush(dangerColor) :
                                          numericValue == 0 ? new XSolidBrush(warningColor) :
                                          new XSolidBrush(successColor);
                            }
                            else
                            {
                                value = numericValue.ToString("N2");
                            }

                            cellFont = boldFont;
                        }
                        else if (DateTime.TryParse(value, out DateTime dateValue))
                        {
                            value = dateValue.ToString("dd/MM/yyyy");
                        }
                        else if (value.ToLower() == "entrada" || value.ToLower() == "recebida" || value.ToLower() == "ativo")
                        {
                            textBrush = new XSolidBrush(successColor);
                        }
                        else if (value.ToLower() == "saída" || value.ToLower() == "cancelada" || value.ToLower() == "inativo")
                        {
                            textBrush = new XSolidBrush(dangerColor);
                        }
                        else if (value.ToLower() == "pendente")
                        {
                            textBrush = new XSolidBrush(warningColor);
                        }

                        string displayText = value;
                        var textSize = gfx.MeasureString(value, cellFont);

                        if (textSize.Width > colWidth - 6)
                        {
                            for (int len = value.Length; len > 0; len--)
                            {
                                displayText = value.Substring(0, len) + "...";
                                if (gfx.MeasureString(displayText, cellFont).Width <= colWidth - 6)
                                    break;
                            }
                        }

                        double textX = x + 3;

                        if (decimal.TryParse(value.Replace(",", ".").Replace(" MTn", "").Replace(currencySymbol, "").Trim(),
                            NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                        {
                            textX = x + colWidth - gfx.MeasureString(displayText, cellFont).Width - 3;
                        }

                        double textY = y + (rowHeight - gfx.MeasureString(displayText, cellFont).Height) / 2;

                        gfx.DrawString(displayText, cellFont, textBrush,
                            new XPoint(textX, textY));

                        if (i < headers.Count - 1)
                        {
                            gfx.DrawLine(new XPen(borderGray, 0.3),
                                x + colWidth, y, x + colWidth, y + rowHeight);
                        }

                        x += colWidth;
                    }

                    gfx.DrawLine(new XPen(borderGray, 0.3),
                        margin, y + rowHeight, margin + availableWidth, y + rowHeight);

                    y += rowHeight;
                }

                DrawTableHeader();
                bool alternate = false;
                int currentRow = 1;
                int rowsOnThisPage = 0;
                int maxRowsPerPage = (int)((availablePageHeight - (y - margin) - footerHeight) / 22);

                foreach (var row in dataRows)
                {
                    if (y > pageHeight - margin - footerHeight - 30 || rowsOnThisPage >= maxRowsPerPage)
                    {
                        string pageInfo = $"Página {pdf.PageCount}";
                        var pageInfoSize = gfx.MeasureString(pageInfo, footerFont);
                        gfx.DrawString(pageInfo, footerFont, new XSolidBrush(textGray),
                            new XPoint((pageWidth - pageInfoSize.Width) / 2, pageHeight - 20));

                        StartNewPage();
                        DrawTableHeader();
                        rowsOnThisPage = 0;
                    }

                    DrawTableRow(row, alternate, currentRow);
                    alternate = !alternate;
                    currentRow++;
                    rowsOnThisPage++;
                }

                if (y > pageHeight - margin - footerHeight)
                {
                    StartNewPage();
                }

                y = pageHeight - margin - footerHeight + 10;
                gfx.DrawLine(new XPen(primaryColor, 0.5), margin, y,
                    pageWidth - margin, y);
                y += 5;

                string footerText = $"{restaurantName} • {DateTime.Now:dd/MM/yyyy HH:mm} • Página {pdf.PageCount}";
                var footerSize = gfx.MeasureString(footerText, footerFont);
                gfx.DrawString(footerText, footerFont, new XSolidBrush(textGray),
                    new XPoint((pageWidth - footerSize.Width) / 2, pageHeight - 15));

                pdf.Save(filePath);
                pdf.Dispose();
            }
            catch (Exception ex)
            {
                throw new Exception($"Erro ao gerar PDF: {ex.Message}", ex);
            }
        }

        // ========== INGREDIENTES ==========
        private async void BtnSaveIngredient_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(TxtIngredientName.Text))
                {
                    ShowErrorMessage("Nome do ingrediente obrigatório.");
                    TxtIngredientName.Focus();
                    return;
                }

                if (!decimal.TryParse(TxtIngredientCost.Text, out decimal cost) || cost < 0)
                {
                    ShowErrorMessage("Custo inválido.");
                    TxtIngredientCost.Focus();
                    return;
                }

                if (string.IsNullOrWhiteSpace(TxtIngredientUnit.Text))
                {
                    ShowErrorMessage("Unidade de medida obrigatória.");
                    TxtIngredientUnit.Focus();
                    return;
                }

                decimal stock = decimal.TryParse(TxtIngredientStock.Text, out var s) ? s : 0;

                await ExecuteDbAsync(async context =>
                {
                    var ing = _currentIngredient ?? new Ingredient();

                    ing.Name = TxtIngredientName.Text.Trim();
                    ing.Cost = cost;
                    ing.Unit = TxtIngredientUnit.Text.Trim();
                    ing.Stock = stock;

                    if (_currentIngredient == null)
                        context.Ingredients.Add(ing);
                    else
                        context.Ingredients.Update(ing);

                    await context.SaveChangesAsync();
                });

                LoadAllIngredients();
                ClearIngredientForm();

                OverlayGrid.Visibility = Visibility.Collapsed;
                IngredientPopup.Visibility = Visibility.Collapsed;

                ShowSuccessMessage("Ingrediente salvo com sucesso.");
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Erro ao salvar ingrediente: {ex.Message}");
            }
        }

        private void FillIngredientForm(Ingredient ingredient)
        {
            _currentIngredient = ingredient;
            TxtIngredientName.Text = ingredient.Name;
            TxtIngredientCost.Text = ingredient.Cost.ToString("N2");
            TxtIngredientUnit.Text = ingredient.Unit ?? "";
            TxtIngredientStock.Text = ingredient.Stock.ToString("N2");
        }

        private async void BtnCheckDataIntegrity_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                var result = await ExecuteDbAsync(async context =>
                {
                    var movementsWithoutNewStock = await context.InventoryMovements
                        .Where(m => m.NewStock == 0 && m.PreviousStock == 0)
                        .CountAsync();

                    var productsWithoutMovements = await context.Products
                        .Where(p => !context.InventoryMovements.Any(m => m.ProductId == p.Id))
                        .CountAsync();

                    var invalidDates = await context.InventoryMovements
                        .Where(m => m.CreatedAt > DateTime.Now)
                        .CountAsync();

                    return new
                    {
                        movementsWithoutNewStock,
                        productsWithoutMovements,
                        invalidDates
                    };
                });

                var message = new StringBuilder();
                message.AppendLine("🔍 VERIFICAÇÃO DE INTEGRIDADE DOS DADOS");
                message.AppendLine("========================================");

                if (result.movementsWithoutNewStock > 0)
                {
                    message.AppendLine($"⚠️ {result.movementsWithoutNewStock} movimentos sem NewStock");
                }
                else
                {
                    message.AppendLine("✅ Todos movimentos têm NewStock");
                }

                if (result.productsWithoutMovements > 0)
                {
                    message.AppendLine($"⚠️ {result.productsWithoutMovements} produtos sem movimentos");
                }
                else
                {
                    message.AppendLine("✅ Todos produtos têm movimentos");
                }

                if (result.invalidDates > 0)
                {
                    message.AppendLine($"🚨 {result.invalidDates} movimentos com data futura");
                }
                else
                {
                    message.AppendLine("✅ Todas datas são válidas");
                }

                message.AppendLine("");
                message.AppendLine("📊 STATUS DO CACHE:");
                message.AppendLine($"• Itens em cache: {ProductStockHistoryCache.GetCacheCount()}");
                message.AppendLine($"• Cache válido: {(ProductStockHistoryCache.IsCacheValid() ? "Sim" : "Não")}");

                Mouse.OverrideCursor = null;

                MessageBox.Show(message.ToString(), "Verificação de Dados",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                ShowErrorMessage($"Erro na verificação: {ex.Message}");
            }
        }

        private async Task<Dictionary<int, Dictionary<DateTime, decimal>>> CalculateBatchStockHistory(
            List<int> productIds,
            DateTime startDate,
            DateTime endDate)
        {
            var result = new Dictionary<int, Dictionary<DateTime, decimal>>();

            try
            {
                await ExecuteDbAsync(async context =>
                {
                    var allMovements = await context.InventoryMovements
                        .Where(m => productIds.Contains(m.ProductId) &&
                                   m.CreatedAt.Date >= startDate.Date &&
                                   m.CreatedAt.Date <= endDate.Date)
                        .OrderBy(m => m.CreatedAt)
                        .ToListAsync();

                    foreach (var productId in productIds)
                    {
                        var productMovements = allMovements
                            .Where(m => m.ProductId == productId)
                            .ToList();

                        var dailyStocks = new Dictionary<DateTime, decimal>();

                        for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
                        {
                            var lastMovement = productMovements
                                .Where(m => m.CreatedAt.Date <= date)
                                .OrderByDescending(m => m.CreatedAt)
                                .FirstOrDefault();

                            decimal stock = lastMovement?.NewStock ?? 0;
                            dailyStocks[date] = stock;

                            ProductStockHistoryCache.AddToCache(productId, date, stock);
                        }

                        result[productId] = dailyStocks;
                    }

                    ProductStockHistoryCache.UpdateCacheTime();
                });

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro no cálculo em lote: {ex.Message}");
                return result;
            }
        }

        private void ClearIngredientForm()
        {
            _currentIngredient = null;
            TxtIngredientName.Clear();
            TxtIngredientCost.Clear();
            TxtIngredientUnit.Clear();
            TxtIngredientStock.Clear();
        }

        private async void BtnDeleteIngredient_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not Ingredient ing) return;

            var result = MessageBox.Show($"Excluir o ingrediente '{ing.Name}'?\nEsta ação não poderá ser desfeita.", "Confirmação",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                bool usedInRecipes = await ExecuteDbAsync(async context =>
                    await context.ProductIngredients.AnyAsync(pi => pi.IngredientId == ing.Id));

                if (usedInRecipes)
                {
                    ShowErrorMessage("Não é possível excluir. Ingrediente está em uso em produtos compostos.");
                    return;
                }

                await ExecuteDbAsync(async context =>
                {
                    context.Ingredients.Remove(ing);
                    await context.SaveChangesAsync();
                });

                LoadAllIngredients();
                ClearIngredientForm();
                ShowSuccessMessage("Ingrediente excluído com sucesso.");
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Erro ao excluir: {ex.Message}");
            }
        }

        // ========== INVENTÁRIO ==========
        private void LoadInventoryMovements()
        {
            ExecuteDb(context =>
            {
                try
                {
                    var query = context.InventoryMovements
                        .Include(m => m.Product)
                        .Include(m => m.User)
                        .OrderByDescending(m => m.CreatedAt)
                        .AsQueryable();

                    Dispatcher.Invoke(() =>
                    {
                        if (CmbInventoryFilterType.SelectedIndex > 0)
                        {
                            var filterType = CmbInventoryFilterType.SelectedIndex == 1 ? "Entrada" : "Saída";
                            query = query.Where(m => m.MovementType == filterType);
                        }

                        if (CmbInventoryFilterProduct.SelectedValue is int productId && productId > 0)
                        {
                            query = query.Where(m => m.ProductId == productId);
                        }

                        if (DpInventoryFrom.SelectedDate.HasValue)
                        {
                            query = query.Where(m => m.CreatedAt >= DpInventoryFrom.SelectedDate.Value);
                        }

                        if (DpInventoryTo.SelectedDate.HasValue)
                        {
                            var endDate = DpInventoryTo.SelectedDate.Value.AddDays(1);
                            query = query.Where(m => m.CreatedAt < endDate);
                        }
                    });

                    var movements = query.ToList();

                    Dispatcher.Invoke(() =>
                    {
                        DgInventoryMovements.ItemsSource = movements;
                        TxtMovementCount.Text = $" ({movements.Count})";
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => ShowErrorMessage($"Erro ao carregar movimentos: {ex.Message}"));
                }
            });
        }

        private void BtnNewMovement_Click(object sender, RoutedEventArgs e)
        {
            ClearMovementForm();
            OverlayGrid.Visibility = Visibility.Visible;
            InventoryMovementPopup.Visibility = Visibility.Visible;
            CmbMovementProduct.Focus();
        }

        private void ClearMovementForm()
        {
            RdbEntrada.IsChecked = true;
            CmbMovementProduct.SelectedIndex = -1;
            TxtMovementQuantity.Clear();
            CmbMovementReason.SelectedIndex = 0;
            TxtMovementNotes.Clear();
            TxtMovementProductName.Text = "-";
            TxtMovementCurrentStock.Text = "Stock atual: 0";
            TxtMovementNewStock.Text = "Novo stock: 0";
        }

        private void CmbMovementProduct_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbMovementProduct.SelectedItem is Product product)
            {
                TxtMovementProductName.Text = product.Name;
                TxtMovementCurrentStock.Text = $"Stock atual: {product.Stock:N0}";
                CalculateNewStock();
            }
        }

        private void TxtMovementQuantity_TextChanged(object sender, TextChangedEventArgs e)
        {
            CalculateNewStock();
        }

        private void RdbEntrada_Checked(object sender, RoutedEventArgs e)
        {
            CalculateNewStock();
        }

        private void RdbSaida_Checked(object sender, RoutedEventArgs e)
        {
            CalculateNewStock();
        }

        private void CalculateNewStock()
        {
            if (CmbMovementProduct.SelectedItem is Product product &&
                int.TryParse(TxtMovementQuantity.Text, out int quantity) && quantity > 0)
            {
                var currentStock = product.Stock;
                var newStock = RdbEntrada.IsChecked == true
                    ? currentStock + quantity
                    : currentStock - quantity;

                TxtMovementNewStock.Text = $"Novo stock: {newStock:N0}";

                if (newStock < 0)
                {
                    TxtMovementNewStock.Foreground = System.Windows.Media.Brushes.Red;
                    BtnSaveMovement.IsEnabled = false;
                }
                else
                {
                    TxtMovementNewStock.Foreground = System.Windows.Media.Brushes.Green;
                    BtnSaveMovement.IsEnabled = true;
                }
            }
            else
            {
                TxtMovementNewStock.Text = "Novo stock: 0";
                BtnSaveMovement.IsEnabled = false;
            }
        }

        private async void BtnSaveMovement_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (CmbMovementProduct.SelectedItem is not Product product)
                {
                    ShowErrorMessage("Selecione um produto.");
                    return;
                }

                if (!int.TryParse(TxtMovementQuantity.Text, out int quantity) || quantity <= 0)
                {
                    ShowErrorMessage("Quantidade inválida.");
                    TxtMovementQuantity.Focus();
                    return;
                }

                var movementType = RdbEntrada.IsChecked == true ? "Entrada" : "Saída";
                var reason = (CmbMovementReason.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Ajuste";
                var notes = TxtMovementNotes.Text.Trim();

                await ExecuteDbAsync(async context =>
                {
                    // Recarregar produto do contexto para ter o stock atual
                    var dbProduct = await context.Products.FindAsync(product.Id);
                    if (dbProduct == null)
                    {
                        throw new Exception("Produto não encontrado no banco de dados.");
                    }

                    var currentStock = dbProduct.Stock;
                    var newStock = movementType == "Entrada"
                        ? currentStock + quantity
                        : currentStock - quantity;

                    if (newStock < 0)
                    {
                        throw new Exception("Stock insuficiente para esta saída.");
                    }

                    var movement = new InventoryMovement
                    {
                        ProductId = dbProduct.Id,
                        MovementType = movementType,
                        Quantity = quantity,
                        PreviousStock = currentStock,
                        NewStock = newStock,
                        Reason = reason,
                        Notes = notes,
                        UserId = AppSession.CurrentUser?.Id ?? 1,
                        CreatedAt = DateTime.Now
                    };

                    dbProduct.Stock = newStock;

                    context.InventoryMovements.Add(movement);
                    await context.SaveChangesAsync();
                });

                LoadInventoryMovements();
                RefreshProducts();
                UpdateStockValue();

                OverlayGrid.Visibility = Visibility.Collapsed;
                InventoryMovementPopup.Visibility = Visibility.Collapsed;

                ShowSuccessMessage($"Movimento de {movementType.ToLower()} registrado com sucesso!");
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Erro ao registrar movimento: {ex.Message}");
            }
        }

        private async void BtnExportInventory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "Arquivo PDF|*.pdf|Arquivo CSV|*.csv|Arquivo Excel|*.xlsx",
                    FileName = $"Relatorio_Inventario_{DateTime.Now:yyyyMMdd_HHmmss}",
                    DefaultExt = ".pdf"
                };

                if (dialog.ShowDialog() == true)
                {
                    var movements = DgInventoryMovements.ItemsSource as List<InventoryMovement>;
                    if (movements == null || movements.Count == 0)
                    {
                        ShowErrorMessage("Nenhum dado para exportar.");
                        return;
                    }

                    Mouse.OverrideCursor = Cursors.Wait;

                    await ExecuteDbAsync(async context =>
                    {
                        var restaurantConfig = await context.RestaurantConfigs.FirstOrDefaultAsync();
                        string restaurantName = restaurantConfig?.RestaurantName ?? "AyGestRest";
                        string currencySymbol = restaurantConfig?.CurrencySymbol ?? "MTn";
                        byte[] logoBytes = restaurantConfig?.LogoBytes;

                        string reportTitle = "RELATÓRIO DE INVENTÁRIO";
                        string dateRange = $"{DpInventoryFrom.SelectedDate:dd/MM/yyyy} a {DpInventoryTo.SelectedDate:dd/MM/yyyy}";

                        var headers = new List<string>
                        {
                            "Data/Hora",
                            "Produto",
                            "Código",
                            "Tipo",
                            "Quantidade",
                            "Stock Ant.",
                            "Stock Atual",
                            "Variação",
                            "Motivo",
                            "Usuário",
                            "Observações"
                        };

                        var dataRows = new List<List<string>>();

                        foreach (var m in movements.OrderBy(m => m.CreatedAt))
                        {
                            var variation = m.NewStock - m.PreviousStock;
                            var variationText = variation >= 0 ? $"+{variation:N0}" : $"{variation:N0}";

                            dataRows.Add(new List<string>
                            {
                                m.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
                                m.Product?.Name ?? "N/A",
                                m.Product?.Code ?? "N/A",
                                m.MovementType,
                                m.Quantity.ToString("N0"),
                                m.PreviousStock.ToString("N0"),
                                m.NewStock.ToString("N0"),
                                variationText,
                                m.Reason ?? "N/A",
                                m.User?.Username ?? "N/A",
                                m.Notes ?? ""
                            });
                        }

                        var summaryRows = new List<string>
                        {
                            $"Total de Movimentos: {movements.Count}",
                            $"Entradas: {movements.Count(m => m.MovementType == "Entrada")}",
                            $"Saídas: {movements.Count(m => m.MovementType == "Saída")}",
                            $"Produtos Movimentados: {movements.Select(m => m.ProductId).Distinct().Count()}",
                            $"Período: {dateRange}"
                        };

                        if (dialog.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        {
                            await Task.Run(() => ExportToPdf(
                                dialog.FileName,
                                restaurantName,
                                reportTitle,
                                dateRange,
                                headers,
                                dataRows,
                                summaryRows,
                                currencySymbol,
                                logoBytes,
                                "inventory"
                            ));
                        }
                        else if (dialog.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                        {
                            ExportToCsv(dialog.FileName, headers, dataRows, restaurantName, reportTitle, dateRange);
                        }
                        else
                        {
                            ExportToExcel(dialog.FileName, headers, dataRows, restaurantName, reportTitle, dateRange);
                        }
                    });

                    Mouse.OverrideCursor = null;
                    ShowSuccessMessage($"Relatório exportado com sucesso: {System.IO.Path.GetFileName(dialog.FileName)}");

                    var result = MessageBox.Show("Deseja abrir o relatório gerado?", "Exportação Concluída",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = dialog.FileName,
                            UseShellExecute = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                ShowErrorMessage($"Erro ao exportar relatório: {ex.Message}");
            }
        }

        private async void BtnExportPurchases_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "Arquivo PDF|*.pdf|Arquivo CSV|*.csv|Arquivo Excel|*.xlsx",
                    FileName = $"Relatorio_Compras_{DateTime.Now:yyyyMMdd_HHmmss}",
                    DefaultExt = ".pdf"
                };

                if (dialog.ShowDialog() == true)
                {
                    var purchases = DgPurchases.ItemsSource as List<PurchaseOrder>;
                    if (purchases == null || purchases.Count == 0)
                    {
                        ShowErrorMessage("Nenhum dado para exportar.");
                        return;
                    }

                    Mouse.OverrideCursor = Cursors.Wait;

                    await ExecuteDbAsync(async context =>
                    {
                        var restaurantConfig = await context.RestaurantConfigs.FirstOrDefaultAsync();
                        string restaurantName = restaurantConfig?.RestaurantName ?? "AyGestRest";
                        string currencySymbol = restaurantConfig?.CurrencySymbol ?? "MTn";
                        byte[] logoBytes = restaurantConfig?.LogoBytes;

                        string reportTitle = "RELATÓRIO DE COMPRAS";
                        string dateRange = $"{DpPurchaseFrom.SelectedDate:dd/MM/yyyy} a {DpPurchaseTo.SelectedDate:dd/MM/yyyy}";

                        var headers = new List<string>
                        {
                            "Número",
                            "Data",
                            "Fornecedor",
                            "NIF",
                            "Tipo",
                            "Status",
                            "Itens",
                            "Valor Total",
                            "Data Recebimento",
                            "Observações"
                        };

                        var dataRows = new List<List<string>>();

                        foreach (var p in purchases.OrderByDescending(p => p.CreatedAt))
                        {
                            dataRows.Add(new List<string>
                            {
                                p.PurchaseNumber,
                                p.CreatedAt.ToString("dd/MM/yyyy"),
                                p.Supplier?.Name ?? "N/A",
                                p.Supplier?.NIF ?? "N/A",
                                p.PurchaseType,
                                p.Status,
                                p.ItemCount.ToString(),
                                p.TotalAmount.ToString("N2"),
                                p.ReceivedAt?.ToString("dd/MM/yyyy") ?? "Pendente",
                                p.Notes ?? ""
                            });
                        }

                        decimal totalAmount = purchases.Sum(p => p.TotalAmount);
                        int pendingCount = purchases.Count(p => p.Status == "Pendente");
                        int receivedCount = purchases.Count(p => p.Status == "Recebida");
                        int cancelledCount = purchases.Count(p => p.Status == "Cancelada");

                        var summaryRows = new List<string>
                        {
                            $"Total de Compras: {purchases.Count}",
                            $"Valor Total: {currencySymbol} {totalAmount:N2}",
                            $"Pendentes: {pendingCount}",
                            $"Recebidas: {receivedCount}",
                            $"Canceladas: {cancelledCount}",
                            $"Média por Compra: {currencySymbol} {(purchases.Count > 0 ? totalAmount / purchases.Count : 0):N2}",
                            $"Período: {dateRange}"
                        };

                        if (dialog.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        {
                            await Task.Run(() => ExportToPdf(
                                dialog.FileName,
                                restaurantName,
                                reportTitle,
                                dateRange,
                                headers,
                                dataRows,
                                summaryRows,
                                currencySymbol,
                                logoBytes,
                                "purchases"
                            ));
                        }
                        else if (dialog.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                        {
                            ExportToCsv(dialog.FileName, headers, dataRows, restaurantName, reportTitle, dateRange);
                        }
                        else
                        {
                            ExportToExcel(dialog.FileName, headers, dataRows, restaurantName, reportTitle, dateRange);
                        }
                    });

                    Mouse.OverrideCursor = null;
                    ShowSuccessMessage($"Relatório exportado com sucesso: {System.IO.Path.GetFileName(dialog.FileName)}");

                    var result = MessageBox.Show("Deseja abrir o relatório gerado?", "Exportação Concluída",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = dialog.FileName,
                            UseShellExecute = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                ShowErrorMessage($"Erro ao exportar relatório: {ex.Message}");
            }
        }

        private void ExportToCsv(string filePath, List<string> headers, List<List<string>> dataRows,
            string restaurantName, string reportTitle, string dateRange)
        {
            try
            {
                var csv = new StringBuilder();

                csv.AppendLine($"Restaurante: {restaurantName}");
                csv.AppendLine($"Relatório: {reportTitle}");
                csv.AppendLine($"Período: {dateRange}");
                csv.AppendLine($"Gerado em: {DateTime.Now:dd/MM/yyyy HH:mm}");
                csv.AppendLine();

                csv.AppendLine(string.Join(";", headers));

                foreach (var row in dataRows)
                {
                    var escapedRow = row.Select(cell =>
                        cell.Contains(";") ? $"\"{cell}\"" : cell);
                    csv.AppendLine(string.Join(";", escapedRow));
                }

                File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                throw new Exception($"Erro ao gerar CSV: {ex.Message}", ex);
            }
        }

        private void ExportToExcel(string filePath, List<string> headers, List<List<string>> dataRows,
            string restaurantName, string reportTitle, string dateRange)
        {
            try
            {
                var csvContent = new StringBuilder();

                csvContent.AppendLine($"Restaurante: {restaurantName}");
                csvContent.AppendLine($"Relatório: {reportTitle}");
                csvContent.AppendLine($"Período: {dateRange}");
                csvContent.AppendLine($"Gerado em: {DateTime.Now:dd/MM/yyyy HH:mm}");
                csvContent.AppendLine();
                csvContent.AppendLine(string.Join(";", headers));

                foreach (var row in dataRows)
                {
                    csvContent.AppendLine(string.Join(";", row));
                }

                if (filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    filePath = System.IO.Path.ChangeExtension(filePath, ".csv");
                }

                File.WriteAllText(filePath, csvContent.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                throw new Exception($"Erro ao gerar arquivo: {ex.Message}", ex);
            }
        }

        // ========== MÉTODOS PARA A TAB DE CÓDIGO DE BARRAS ==========

        private void BarcodeProductCheckBox_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            UpdateBarcodeStatistics();
        }

        private void UpdateBarcodeStatistics()
        {
            try
            {
                if (DgBarcodeProducts.ItemsSource is List<BarcodeProduct> products)
                {
                    var selectedCount = products.Count(p => p.IsSelected);
                    var totalCount = products.Count;
                    var withBarcodeCount = products.Count(p => !string.IsNullOrEmpty(p.Barcode));
                    var withoutBarcodeCount = totalCount - withBarcodeCount;

                    TxtSelectedCount.Text = selectedCount.ToString();
                    TxtTotalCount.Text = totalCount.ToString();
                    TxtWithBarcodeCount.Text = withBarcodeCount.ToString();
                    TxtWithoutBarcodeCount.Text = withoutBarcodeCount.ToString();
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Erro ao atualizar estatísticas: {ex.Message}");
            }
        }

        private void BtnSelectAllBarcodes_Click(object sender, RoutedEventArgs e)
        {
            if (DgBarcodeProducts.ItemsSource is List<BarcodeProduct> products)
            {
                foreach (var product in products)
                {
                    product.IsSelected = true;
                }
                DgBarcodeProducts.Items.Refresh();
                UpdateBarcodeStatistics();
            }
        }

        private void BtnDeselectAllBarcodes_Click(object sender, RoutedEventArgs e)
        {
            if (DgBarcodeProducts.ItemsSource is List<BarcodeProduct> products)
            {
                foreach (var product in products)
                {
                    product.IsSelected = false;
                }
                DgBarcodeProducts.Items.Refresh();
                UpdateBarcodeStatistics();
            }
        }

        private void BtnRefreshBarcodeList_Click(object sender, RoutedEventArgs e)
        {
            LoadBarcodeProducts();
            ShowSuccessMessage("Lista de produtos atualizada.");
        }

        private void LoadBarcodeProducts()
        {
            ExecuteDb(context =>
            {
                try
                {
                    var products = context.Products
                        .Where(p => !string.IsNullOrEmpty(p.Name) &&
                                   !string.IsNullOrEmpty(p.Code) &&
                                   p.Price > 0)
                        .OrderBy(p => p.Name)
                        .ToList();

                    var barcodeProducts = products.Select(p => new BarcodeProduct
                    {
                        Id = p.Id,
                        Code = p.Code ?? "N/A",
                        Name = p.Name,
                        Price = p.Price,
                        Stock = p.Stock,
                        Barcode = p.Barcode,
                        IsSelected = false
                    }).ToList();

                    Dispatcher.Invoke(() =>
                    {
                        DgBarcodeProducts.ItemsSource = barcodeProducts;
                        TxtBarcodeProductsCount.Text = $" ({barcodeProducts.Count})";

                        GenerateBarcodePreviews(barcodeProducts);
                        UpdateBarcodeStatistics();
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => ShowErrorMessage($"Erro ao carregar produtos para código de barras: {ex.Message}"));
                }
            });

            UpdatePrinterInfo();
        }

        private async void UpdatePrinterInfo()
        {
            try
            {
                var config = await ExecuteDbAsync(async context =>
                    await context.RestaurantConfigs.FirstOrDefaultAsync());

                Dispatcher.Invoke(() =>
                {
                    TxtPrinterName.Text = config?.PrinterName ?? "Não configurada";
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => TxtPrinterName.Text = "Erro ao carregar");
            }
        }

        private void BtnGenerateCode_Click(object sender, RoutedEventArgs e)
        {
            ExecuteDb(context =>
            {
                try
                {
                    var lastProduct = context.Products
                        .Where(p => p.Code != null && p.Code.StartsWith("P"))
                        .OrderByDescending(p => p.Id)
                        .FirstOrDefault();

                    int nextNumber = 10001;

                    if (lastProduct != null && !string.IsNullOrEmpty(lastProduct.Code))
                    {
                        var codeStr = lastProduct.Code.Substring(1);
                        if (int.TryParse(codeStr, out int lastNumber))
                        {
                            nextNumber = lastNumber + 1;
                        }
                    }

                    Dispatcher.Invoke(() =>
                    {
                        TxtCode.Text = $"P{nextNumber}";
                        OnFormChanged(sender, e);
                        ShowSuccessMessage($"Código gerado: P{nextNumber}");
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => ShowErrorMessage($"Erro ao gerar código: {ex.Message}"));
                }
            });
        }

        // ========== FORNECEDORES ==========
        private void LoadSuppliers()
        {
            ExecuteDb(context =>
            {
                try
                {
                    var suppliers = context.Suppliers
                        .Include(s => s.PurchaseOrders)
                        .OrderBy(s => s.Name)
                        .ToList();

                    foreach (var supplier in suppliers)
                    {
                        supplier.PurchaseCount = supplier.PurchaseOrders?.Count ?? 0;
                    }

                    Dispatcher.Invoke(() =>
                    {
                        DgSuppliers.ItemsSource = suppliers;
                        CmbPurchaseSupplier.ItemsSource = suppliers;
                        CmbPurchaseFilterSupplier.ItemsSource = suppliers;
                        TxtSupplierCount.Text = $" ({suppliers.Count})";
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => ShowErrorMessage($"Erro ao carregar fornecedores: {ex.Message}"));
                }
            });
        }

        private void BtnNewSupplier_Click(object sender, RoutedEventArgs e)
        {
            ClearSupplierForm();
            _currentSupplier = null;
            OverlayGrid.Visibility = Visibility.Visible;
            SupplierPopup.Visibility = Visibility.Visible;
            TxtSupplierName.Focus();
            ShowSuccessMessage("Preencha os dados do novo fornecedor.");
        }

        private void ClearSupplierForm()
        {
            TxtSupplierName.Clear();
            TxtSupplierNIF.Clear();
            TxtSupplierPhone.Clear();
            TxtSupplierEmail.Clear();
            TxtSupplierAddress.Clear();
            TxtSupplierCity.Clear();
            TxtSupplierProvince.Clear();
            TxtSupplierContactPerson.Clear();
            TxtSupplierPaymentTerms.Clear();
            TxtSupplierNotes.Clear();
            ChkSupplierActive.IsChecked = true;
            TxtSupplierFormTitle.Text = "Novo Fornecedor";
        }

        private void FillSupplierForm(Supplier supplier)
        {
            _currentSupplier = supplier;
            TxtSupplierName.Text = supplier.Name;
            TxtSupplierNIF.Text = supplier.NIF;
            TxtSupplierPhone.Text = supplier.Phone;
            TxtSupplierEmail.Text = supplier.Email;
            TxtSupplierAddress.Text = supplier.Address;
            TxtSupplierCity.Text = supplier.City;
            TxtSupplierProvince.Text = supplier.Province;
            TxtSupplierContactPerson.Text = supplier.ContactPerson;
            TxtSupplierPaymentTerms.Text = supplier.PaymentTerms;
            TxtSupplierNotes.Text = supplier.Notes;
            ChkSupplierActive.IsChecked = supplier.Active;
            TxtSupplierFormTitle.Text = $"Editando: {supplier.Name}";
        }

        private async void BtnSaveSupplier_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(TxtSupplierName.Text))
                {
                    ShowErrorMessage("Nome do fornecedor é obrigatório.");
                    TxtSupplierName.Focus();
                    return;
                }

                if (string.IsNullOrWhiteSpace(TxtSupplierNIF.Text))
                {
                    ShowErrorMessage("NIF/Identificação é obrigatório.");
                    TxtSupplierNIF.Focus();
                    return;
                }

                if (string.IsNullOrWhiteSpace(TxtSupplierPhone.Text))
                {
                    ShowErrorMessage("Telefone é obrigatório.");
                    TxtSupplierPhone.Focus();
                    return;
                }

                var supplier = _currentSupplier ?? new Supplier();

                supplier.Name = TxtSupplierName.Text.Trim();
                supplier.NIF = TxtSupplierNIF.Text.Trim();
                supplier.Phone = TxtSupplierPhone.Text.Trim();
                supplier.Email = TxtSupplierEmail.Text.Trim();
                supplier.Address = TxtSupplierAddress.Text.Trim();
                supplier.City = TxtSupplierCity.Text.Trim();
                supplier.Province = TxtSupplierProvince.Text.Trim();
                supplier.ContactPerson = TxtSupplierContactPerson.Text.Trim();
                supplier.PaymentTerms = TxtSupplierPaymentTerms.Text.Trim();
                supplier.Notes = TxtSupplierNotes.Text.Trim();
                supplier.Active = ChkSupplierActive.IsChecked ?? true;

                await ExecuteDbAsync(async context =>
                {
                    if (_currentSupplier == null)
                        context.Suppliers.Add(supplier);
                    else
                        context.Suppliers.Update(supplier);

                    await context.SaveChangesAsync();
                });

                LoadSuppliers();

                OverlayGrid.Visibility = Visibility.Collapsed;
                SupplierPopup.Visibility = Visibility.Collapsed;

                ShowSuccessMessage($"Fornecedor '{supplier.Name}' salvo com sucesso!");
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Erro ao salvar fornecedor: {ex.Message}");
            }
        }

        private async void BtnDeleteSupplier_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not Supplier supplier) return;

            var hasPurchases = await ExecuteDbAsync(async context =>
                await context.PurchaseOrders.AnyAsync(p => p.SupplierId == supplier.Id));

            if (hasPurchases)
            {
                ShowErrorMessage("Não é possível excluir fornecedor com compras registradas.");
                return;
            }

            var result = MessageBox.Show($"Excluir o fornecedor '{supplier.Name}'?\nEsta ação não poderá ser desfeita.",
                "Confirmação", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                await ExecuteDbAsync(async context =>
                {
                    context.Suppliers.Remove(supplier);
                    await context.SaveChangesAsync();
                });

                LoadSuppliers();
                ShowSuccessMessage("Fornecedor excluído com sucesso.");
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Erro ao excluir fornecedor: {ex.Message}");
            }
        }

        // ========== COMPRAS ==========
        private void LoadPurchases()
        {
            ExecuteDb(context =>
            {
                try
                {
                    var query = context.PurchaseOrders
                        .Include(p => p.Supplier)
                        .Include(p => p.Items)
                        .ThenInclude(i => i.Product)
                        .OrderByDescending(p => p.CreatedAt)
                        .AsQueryable();

                    Dispatcher.Invoke(() =>
                    {
                        if (CmbPurchaseFilterStatus.SelectedIndex > 0)
                        {
                            var status = CmbPurchaseFilterStatus.SelectedIndex switch
                            {
                                1 => "Pendente",
                                2 => "Recebida",
                                3 => "Cancelada",
                                _ => "Pendente"
                            };
                            query = query.Where(p => p.Status == status);
                        }

                        if (CmbPurchaseFilterSupplier.SelectedValue is int supplierId && supplierId > 0)
                        {
                            query = query.Where(p => p.SupplierId == supplierId);
                        }

                        if (DpPurchaseFrom.SelectedDate.HasValue)
                        {
                            query = query.Where(p => p.CreatedAt >= DpPurchaseFrom.SelectedDate.Value);
                        }

                        if (DpPurchaseTo.SelectedDate.HasValue)
                        {
                            var endDate = DpPurchaseTo.SelectedDate.Value.AddDays(1);
                            query = query.Where(p => p.CreatedAt < endDate);
                        }
                    });

                    var purchases = query.ToList();

                    Dispatcher.Invoke(() =>
                    {
                        DgPurchases.ItemsSource = purchases;
                        TxtPurchaseCount.Text = $" ({purchases.Count})";
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => ShowErrorMessage($"Erro ao carregar compras: {ex.Message}"));
                }
            });
        }

        private void BtnNewPurchase_Click(object sender, RoutedEventArgs e)
        {
            ClearPurchaseForm();

            OverlayGrid.Visibility = Visibility.Visible;
            PurchasePopup.Visibility = Visibility.Visible;

            TxtPurchaseFormTitle.Text = "Nova Requisição/Compra";
            CmbPurchaseSupplier.Focus();

            ShowSuccessMessage("Preencha os dados da nova compra.");
        }

        private void ClearPurchaseForm()
        {
            _currentPurchase = null;
            _currentPurchaseItems = new ObservableCollection<PurchaseOrderItem>();
            _currentDisplayItems = new ObservableCollection<PurchaseDisplayItem>(); // NOVO

            CmbPurchaseSupplier.SelectedIndex = -1;
            CmbPurchaseType.SelectedIndex = 0;
            TxtPurchaseNotes.Clear();
            CmbPurchaseProduct.SelectedIndex = -1;
            TxtPurchaseQuantity.Text = "1";
            TxtPurchaseUnitPrice.Text = "0,00";

            // Usar a coleção de exibição no DataGrid
            DgPurchaseItems.ItemsSource = _currentDisplayItems;
            TxtPurchaseItemCount.Text = "0";
            TxtPurchaseTotal.Text = "0,00 MTn";
            BtnSavePurchase.IsEnabled = false;

            TxtPurchaseFormTitle.Text = "Nova Requisição/Compra";
        }
        private async void BtnAddPurchaseItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (CmbPurchaseProduct.SelectedItem is not PurchaseItem selectedItem)
                {
                    ShowErrorMessage("Selecione um produto ou ingrediente.");
                    CmbPurchaseProduct.Focus();
                    return;
                }

                if (!int.TryParse(TxtPurchaseQuantity.Text, out int quantity) || quantity <= 0)
                {
                    ShowErrorMessage("Quantidade inválida.");
                    TxtPurchaseQuantity.Focus();
                    return;
                }

                if (!decimal.TryParse(TxtPurchaseUnitPrice.Text.Replace(",", "."),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out decimal unitPrice) || unitPrice < 0)
                {
                    ShowErrorMessage("Preço unitário inválido.");
                    TxtPurchaseUnitPrice.Focus();
                    return;
                }

                // Verificar se o item já foi adicionado (usando a lista de exibição)
                if (_currentDisplayItems.Any(x => x.OriginalId == selectedItem.Id))
                {
                    ShowErrorMessage("Este item já foi adicionado à compra.");
                    return;
                }

                // Buscar o item do banco de dados
                await ExecuteDbAsync(async context =>
                {
                    if (selectedItem.IsProduct)
                    {
                        // É um produto
                        var product = await context.Products.FindAsync(selectedItem.Id);
                        if (product != null)
                        {
                            // Criar item para a lista de exibição
                            var displayItem = new PurchaseDisplayItem
                            {
                                Id = _currentDisplayItems.Count + 1,
                                Name = product.Name,
                                Type = "Produto",
                                Icon = "📦",
                                Quantity = quantity,
                                UnitPrice = unitPrice,
                                Subtotal = quantity * unitPrice,
                                OriginalId = product.Id,
                                IsProduct = true
                            };

                            _currentDisplayItems.Add(displayItem);

                            // Criar item real para salvar no banco (será usado depois)
                            var purchaseItem = new PurchaseOrderItem
                            {
                                ProductId = product.Id,
                                Product = product,
                                Quantity = quantity,
                                UnitPrice = unitPrice,
                                Subtotal = quantity * unitPrice
                            };

                            _currentPurchaseItems.Add(purchaseItem);
                        }
                    }
                    else
                    {
                        // É um ingrediente
                        var ingredient = await context.Ingredients.FindAsync(selectedItem.Id);
                        if (ingredient != null)
                        {
                            // Criar item para a lista de exibição
                            var displayItem = new PurchaseDisplayItem
                            {
                                Id = _currentDisplayItems.Count + 1,
                                Name = ingredient.Name,
                                Type = "Ingrediente",
                                Icon = "🧪",
                                Quantity = quantity,
                                UnitPrice = unitPrice,
                                Subtotal = quantity * unitPrice,
                                OriginalId = ingredient.Id,
                                IsProduct = false
                            };

                            _currentDisplayItems.Add(displayItem);

                            // Para ingredientes, NÃO criamos PurchaseOrderItem ainda
                            // Vamos tratar no salvamento
                        }
                    }
                });

                // Atualizar o DataGrid com a lista de exibição
                DgPurchaseItems.ItemsSource = null;
                DgPurchaseItems.ItemsSource = _currentDisplayItems;

                UpdatePurchaseSummary();

                CmbPurchaseProduct.SelectedIndex = -1;
                TxtPurchaseQuantity.Text = "1";
                TxtPurchaseUnitPrice.Text = "0,00";
                CmbPurchaseProduct.Focus();
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Erro ao adicionar item: {ex.Message}");
            }
        }
        private void BtnRemovePurchaseItem_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is PurchaseDisplayItem displayItem)
            {
                // Remover da lista de exibição
                _currentDisplayItems.Remove(displayItem);

                // Remover o item correspondente da lista de compras (se for produto)
                var itemToRemove = _currentPurchaseItems.FirstOrDefault(x => x.ProductId == displayItem.OriginalId);
                if (itemToRemove != null)
                {
                    _currentPurchaseItems.Remove(itemToRemove);
                }

                UpdatePurchaseSummary();
            }
        }
        private void UpdatePurchaseSummary()
        {
            var itemCount = _currentDisplayItems.Count;
            var total = _currentDisplayItems.Sum(i => i.Subtotal);

            TxtPurchaseItemCount.Text = $"{itemCount} itens";
            TxtPurchaseTotal.Text = $"{total:N2} MTn";

            BtnSavePurchase.IsEnabled = itemCount > 0;
        }

        private async void BtnSavePurchase_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (CmbPurchaseSupplier.SelectedItem is not Supplier supplier)
                {
                    ShowErrorMessage("Selecione um fornecedor válido.");
                    CmbPurchaseSupplier.Focus();
                    return;
                }

                if (_currentDisplayItems == null || _currentDisplayItems.Count == 0)
                {
                    ShowErrorMessage("Adicione pelo menos um item à compra.");
                    return;
                }

                // Validar itens
                foreach (var item in _currentDisplayItems)
                {
                    if (item.Quantity <= 0)
                    {
                        ShowErrorMessage("Quantidade inválida em um dos itens.");
                        return;
                    }

                    if (item.UnitPrice < 0)
                    {
                        ShowErrorMessage("Preço unitário inválido em um dos itens.");
                        return;
                    }
                }

                Mouse.OverrideCursor = Cursors.Wait;
                BtnSavePurchase.IsEnabled = false;

                string purchaseNumber = await GeneratePurchaseNumberAsync();

                using var context = new AyGestRestContext();

                // Iniciar transação
                using var transaction = await context.Database.BeginTransactionAsync();

                try
                {
                    // 1. Criar a compra
                    var purchase = new PurchaseOrder
                    {
                        PurchaseNumber = purchaseNumber,
                        SupplierId = supplier.Id,
                        PurchaseType = (CmbPurchaseType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Compra",
                        Status = "Pendente",
                        TotalAmount = _currentDisplayItems.Sum(i => i.Subtotal),
                        Notes = TxtPurchaseNotes.Text?.Trim() ?? "",
                        CreatedAt = DateTime.Now
                    };

                    context.PurchaseOrders.Add(purchase);
                    await context.SaveChangesAsync();

                    // 2. Processar cada item
                    foreach (var displayItem in _currentDisplayItems)
                    {
                        int productId;

                        if (displayItem.IsProduct)
                        {
                            // É um produto existente
                            productId = displayItem.OriginalId;
                        }
                        else
                        {
                            // É um ingrediente - verificar se já existe produto virtual
                            var existingProductId = await context.Database
                                .ExecuteSqlRawAsync(@"
                            SELECT Id FROM Products 
                            WHERE Code LIKE {0} 
                            LIMIT 1", $"ING_{displayItem.OriginalId}_%");

                            if (existingProductId > 0)
                            {
                                // Já existe, precisamos pegar o ID
                                var product = await context.Products
                                    .FirstOrDefaultAsync(p => EF.Functions.Like(p.Code, $"ING_{displayItem.OriginalId}_%"));
                                productId = product?.Id ?? 0;
                            }
                            else
                            {
                                // Buscar o ingrediente original
                                var ingredient = await context.Ingredients.FindAsync(displayItem.OriginalId);

                                // Buscar a primeira categoria
                                var firstCategory = await context.ProductCategories.FirstOrDefaultAsync();
                                if (firstCategory == null)
                                {
                                    throw new Exception("Nenhuma categoria encontrada. Crie uma categoria primeiro.");
                                }

                                // SQL para inserir o produto virtual com TODOS os campos
                                string sql = @"
                            INSERT INTO Products (
                                Name, Code, Description, Price, Stock, 
                                Active, IsComposite, CategoryId, Barcode, PrintToKitchen,
                                Unit, Featured, Promo, TrackInventory
                            ) VALUES (
                                {0}, {1}, {2}, {3}, {4}, 
                                1, 0, {5}, NULL, 0,
                                {6}, 0, 0, 1
                            );
                            SELECT last_insert_rowid();";

                                var result = await context.Database.ExecuteSqlRawAsync(sql,
                                    $"{displayItem.Name} (Ingrediente)",
                                    $"ING_{displayItem.OriginalId}_{DateTime.Now:yyyyMMddHHmmss}",
                                    $"Produto virtual para compra de ingrediente: {displayItem.Name}",
                                    displayItem.UnitPrice,
                                    0, // Stock inicial
                                    firstCategory.Id,
                                    ingredient?.Unit ?? "un"
                                );

                                // Pegar o ID do produto inserido
                                var newProduct = await context.Products
                                    .OrderByDescending(p => p.Id)
                                    .FirstOrDefaultAsync(p => p.Code.StartsWith($"ING_{displayItem.OriginalId}"));

                                productId = newProduct?.Id ?? 0;

                                Debug.WriteLine($"✅ Produto virtual criado via SQL: {displayItem.Name} (ID: {productId})");
                            }
                        }

                        // 3. Criar o item de compra
                        var purchaseItem = new PurchaseOrderItem
                        {
                            PurchaseOrderId = purchase.Id,
                            ProductId = productId,
                            Quantity = displayItem.Quantity,
                            UnitPrice = displayItem.UnitPrice,
                            Subtotal = displayItem.Subtotal
                        };

                        context.PurchaseOrderItems.Add(purchaseItem);
                    }

                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    _currentPurchase = purchase;

                    LoadPurchases();
                    RefreshProducts();
                    LoadAllIngredients();

                    ClearPurchaseForm();
                    OverlayGrid.Visibility = Visibility.Collapsed;
                    PurchasePopup.Visibility = Visibility.Collapsed;

                    ShowSuccessMessage($"✅ Compra {purchaseNumber} salva com sucesso! Itens: {_currentDisplayItems.Count}");

                    // Enviar email
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var items = await ExecuteDbAsync(async ctx =>
                                await ctx.PurchaseOrderItems
                                    .Include(i => i.Product)
                                    .Where(i => i.PurchaseOrderId == _currentPurchase.Id)
                                    .ToListAsync());

                            await SendPurchaseEmailAsync(_currentPurchase, items, supplier);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"❌ Erro ao enviar email: {ex.Message}");
                        }
                    });

                    try { UpdateDailyClosingExpenses(); } catch { }
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"❌ Erro ao salvar compra: {ex.Message}");
                Debug.WriteLine($"❌ Erro detalhado: {ex.ToString()}");
            }
            finally
            {
                Mouse.OverrideCursor = null;
                BtnSavePurchase.IsEnabled = true;
            }
        }
        private async Task<string> GeneratePurchaseNumberAsync()
        {
            return await ExecuteDbAsync(async context =>
            {
                var lastPurchase = await context.PurchaseOrders
                    .OrderByDescending(p => p.Id)
                    .FirstOrDefaultAsync();

                if (lastPurchase == null || string.IsNullOrEmpty(lastPurchase.PurchaseNumber))
                {
                    return "COMP-00001";
                }

                if (lastPurchase.PurchaseNumber.StartsWith("COMP-") &&
                    int.TryParse(lastPurchase.PurchaseNumber.Substring(5), out int lastNum))
                {
                    return $"COMP-{(lastNum + 1):D5}";
                }

                return $"COMP-{DateTime.Now:yyyyMMddHHmmss}";
            });
        }

        private async Task SendPurchaseEmailAsync(PurchaseOrder purchase, List<PurchaseOrderItem> items, Supplier supplier)
        {
            try
            {
                var config = await ExecuteDbAsync(async context =>
                    await context.RestaurantConfigs.FirstOrDefaultAsync());

                if (config == null || string.IsNullOrEmpty(config.EmailAddress) ||
                    string.IsNullOrEmpty(config.RecipientEmail) || !config.SendReports)
                {
                    Debug.WriteLine("⚠️ Email não configurado para envio de relatórios.");
                    return;
                }

                string restaurantName = config.RestaurantName ?? "AyGestRest";
                string currencySymbol = config.CurrencySymbol ?? "MTn";
                byte[] logoBytes = null;

                if (!string.IsNullOrEmpty(config.LogoPath))
                {
                    string fullLogoPath = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "AyGestRest",
                        "Logos",
                        config.LogoPath);

                    if (File.Exists(fullLogoPath))
                    {
                        logoBytes = File.ReadAllBytes(fullLogoPath);
                    }
                }

                string reportsFolder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "AyGestRest",
                    "Relatórios Compras");

                Directory.CreateDirectory(reportsFolder);

                string fileName = $"Compra_{purchase.PurchaseNumber}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                string pdfPath = System.IO.Path.Combine(reportsFolder, fileName);

                var headers = new List<string>
                {
                    "Produto",
                    "Código",
                    "Quantidade",
                    "Preço Unit.",
                    "Subtotal"
                };

                var dataRows = new List<List<string>>();

                foreach (var item in items)
                {
                    dataRows.Add(new List<string>
                    {
                        item.Product?.Name ?? "N/A",
                        item.Product?.Code ?? "N/A",
                        item.Quantity.ToString("N0"),
                        item.UnitPrice.ToString("N2"),
                        item.Subtotal.ToString("N2")
                    });
                }

                var summaryRows = new List<string>
                {
                    $"Número da Compra: {purchase.PurchaseNumber}",
                    $"Fornecedor: {supplier.Name}",
                    $"NIF: {supplier.NIF}",
                    $"Data: {purchase.CreatedAt:dd/MM/yyyy HH:mm}",
                    $"Tipo: {purchase.PurchaseType}",
                    $"Status: {purchase.Status}",
                    $"Total de Itens: {items.Count}",
                    $"Valor Total: {currencySymbol} {purchase.TotalAmount:N2}"
                };

                await Task.Run(() => ExportToPdf(
                    pdfPath,
                    restaurantName,
                    $"COMPRA - {purchase.PurchaseNumber}",
                    $"Data: {purchase.CreatedAt:dd/MM/yyyy HH:mm}",
                    headers,
                    dataRows,
                    summaryRows,
                    currencySymbol,
                    logoBytes,
                    "purchase"
                ));

                string subject = $"📦 Nova Compra Registrada - {purchase.PurchaseNumber} - {restaurantName}";

                string body = $@"
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; color: #333; }}
        .header {{ background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 20px; border-radius: 5px; margin-bottom: 20px; }}
        .success {{ color: #00C853; }}
        .warning {{ color: #FF9100; }}
        .info {{ background: #f8f9fa; padding: 15px; border-left: 4px solid #667eea; margin-bottom: 20px; }}
        table {{ border-collapse: collapse; width: 100%; margin-bottom: 20px; }}
        th {{ background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 12px; text-align: left; }}
        td {{ padding: 10px; border-bottom: 1px solid #ddd; }}
        tr:hover {{ background-color: #f5f5f5; }}
        .total {{ font-size: 18px; font-weight: bold; color: #667eea; text-align: right; margin-top: 20px; }}
        .footer {{ margin-top: 30px; padding-top: 20px; border-top: 1px solid #ddd; font-size: 12px; color: #666; }}
    </style>
</head>
<body>
    <div class='header'>
        <h2>📦 NOVA COMPRA REGISTRADA</h2>
    </div>

    <div class='info'>
        <h3>📋 Informações da Compra</h3>
        <table>
            <tr>
                <td><strong>Número:</strong> {purchase.PurchaseNumber}</td>
                <td><strong>Data:</strong> {purchase.CreatedAt:dd/MM/yyyy HH:mm}</td>
            </tr>
            <tr>
                <td><strong>Fornecedor:</strong> {supplier.Name}</td>
                <td><strong>NIF:</strong> {supplier.NIF}</td>
            </tr>
            <tr>
                <td><strong>Tipo:</strong> {purchase.PurchaseType}</td>
                <td><strong>Status:</strong> <span class='success'>✓ {purchase.Status}</span></td>
            </tr>
            <tr>
                <td><strong>Total de Itens:</strong> {items.Count}</td>
                <td><strong>Valor Total:</strong> <span style='font-size: 16px; font-weight: bold; color: #667eea;'>{currencySymbol} {purchase.TotalAmount:N2}</span></td>
            </tr>
        </table>
    </div>

    <div class='info'>
        <h3>🛒 Itens da Compra</h3>
        <table>
            <thead>
                <tr>
                    <th>Produto</th>
                    <th>Código</th>
                    <th>Qtd</th>
                    <th>Preço Unit.</th>
                    <th>Subtotal</th>
                </tr>
            </thead>
            <tbody>";

                foreach (var item in items)
                {
                    body += $@"
                <tr>
                    <td>{item.Product?.Name ?? "N/A"}</td>
                    <td>{item.Product?.Code ?? "N/A"}</td>
                    <td>{item.Quantity:N0}</td>
                    <td>{currencySymbol} {item.UnitPrice:N2}</td>
                    <td><strong>{currencySymbol} {item.Subtotal:N2}</strong></td>
                </tr>";
                }

                body += $@"
            </tbody>
        </table>
    </div>

    <div class='total'>
        TOTAL DA COMPRA: {currencySymbol} {purchase.TotalAmount:N2}
    </div>

    <p style='margin-top: 20px;'>
        📊 Um PDF detalhado com todas as informações está anexado a este email.
    </p>

    <div class='footer'>
        <p><strong>{restaurantName}</strong></p>
        <p>Sistema de Gestão AyGestRest</p>
        <p>{DateTime.Now:dd/MM/yyyy HH:mm}</p>
    </div>
</body>
</html>";

                bool emailSent = SettingsControl.SendEmail(subject, body, true, pdfPath);

                if (emailSent)
                {
                    ShowSuccessMessage($"📧 Email enviado para {config.RecipientEmail} com o PDF da compra!");
                }
                else
                {
                    ShowSuccessMessage($"✅ Compra salva! PDF salvo em: {pdfPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro ao enviar email de compra: {ex.Message}");
                ShowErrorMessage($"Compra salva, mas falha ao enviar email: {ex.Message}");
            }
        }

        private void UpdateDailyClosingExpenses()
        {
            try
            {
                var mainWindow = Window.GetWindow(this);
                if (mainWindow != null)
                {
                    var dailyClosingsControl = FindVisualChild<DailyClosingsControl>(mainWindow);
                    if (dailyClosingsControl != null)
                    {
                        dailyClosingsControl.ReloadData();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro ao atualizar fecho do dia: {ex.Message}");
            }
        }

        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child != null && child is T)
                    return (T)child;
                else
                {
                    var descendant = FindVisualChild<T>(child);
                    if (descendant != null)
                        return descendant;
                }
            }
            return null;
        }

        private async void BtnReceivePurchase_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not PurchaseOrder purchase)
                return;

            if (purchase.Status == "Recebida")
            {
                ShowErrorMessage("Esta compra já foi recebida anteriormente.");
                return;
            }

            if (purchase.Status == "Cancelada")
            {
                ShowErrorMessage("Não é possível receber uma compra cancelada.");
                return;
            }

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                await ExecuteDbAsync(async context =>
                {
                    var todayStart = DateTime.Today;
                    var tomorrow = todayStart.AddDays(1);

                    var paymentsToday = (decimal)await context.Payments
                        .Where(p => p.PaymentDate >= todayStart)
                        .SumAsync(p => (double)p.Amount);

                    var todayExpenses = (decimal)await context.PurchaseOrders
                        .Where(p =>
                            p.CreatedAt >= todayStart &&
                            p.CreatedAt < tomorrow &&
                            p.Status == "Recebida")
                        .SumAsync(p => (double)p.TotalAmount);

                    var netSales = paymentsToday - todayExpenses - purchase.TotalAmount;

                    string warningMessage = "";

                    if (netSales < 0)
                    {
                        warningMessage =
                            $"⚠️ ATENÇÃO: Após receber esta compra, o saldo do dia será NEGATIVO!\n\n" +
                            $"Total Vendido Hoje: {paymentsToday:N2} MTn\n" +
                            $"Total de Compras Hoje: {(todayExpenses + purchase.TotalAmount):N2} MTn\n" +
                            $"Saldo Projetado: {netSales:N2} MTn\n\n" +
                            $"Deseja continuar mesmo assim?";
                    }
                    else if (paymentsToday > 0 &&
                             purchase.TotalAmount > paymentsToday * 0.5m)
                    {
                        warningMessage =
                            $"⚠️ ATENÇÃO: Esta compra representa mais de 50% do total vendido hoje!\n\n" +
                            $"Total Vendido Hoje: {paymentsToday:N2} MTn\n" +
                            $"Valor da Compra: {purchase.TotalAmount:N2} MTn\n" +
                            $"Percentual: {(purchase.TotalAmount / paymentsToday * 100):N1}%\n\n" +
                            $"Deseja continuar mesmo assim?";
                    }

                    if (!string.IsNullOrEmpty(warningMessage))
                    {
                        var result = MessageBox.Show(
                            warningMessage,
                            "Validação de Recebimento",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        if (result != MessageBoxResult.Yes)
                            return;
                    }

                    var confirmResult = MessageBox.Show(
                        $"📦 CONFIRMAR RECEBIMENTO\n" +
                        $"=======================\n" +
                        $"Compra: {purchase.PurchaseNumber}\n" +
                        $"Fornecedor: {purchase.Supplier?.Name ?? "N/A"}\n" +
                        $"Valor Total: {purchase.TotalAmount:N2} MTn\n" +
                        $"Data: {purchase.CreatedAt:dd/MM/yyyy}\n" +
                        $"=======================\n\n" +
                        $"Os produtos serão adicionados ao inventário e o valor será descontado do fecho do dia.\n\n" +
                        $"Confirmar recebimento?",
                        "Confirmar Recebimento",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (confirmResult != MessageBoxResult.Yes)
                        return;

                    // Buscar a compra novamente do contexto
                    var dbPurchase = await context.PurchaseOrders
                        .Include(p => p.Supplier)
                        .Include(p => p.Items)
                        .ThenInclude(i => i.Product)
                        .FirstOrDefaultAsync(p => p.Id == purchase.Id);

                    if (dbPurchase == null)
                    {
                        throw new Exception("Compra não encontrada no banco de dados.");
                    }

                    // ATUALIZAR O STATUS PARA RECEBIDA
                    dbPurchase.Status = "Recebida";
                    dbPurchase.ReceivedAt = DateTime.Now;

                    foreach (var item in dbPurchase.Items)
                    {
                        var product = await context.Products.FindAsync(item.ProductId);
                        if (product != null)
                        {
                            var previousStock = product.Stock;

                            product.Stock += item.Quantity;

                            var movement = new InventoryMovement
                            {
                                ProductId = item.ProductId,
                                MovementType = "Entrada",
                                Quantity = item.Quantity,
                                PreviousStock = previousStock,
                                NewStock = product.Stock,
                                Reason = $"Compra {dbPurchase.PurchaseNumber}",
                                Notes = $"Recebimento de compra do fornecedor {dbPurchase.Supplier?.Name}",
                                UserId = AppSession.CurrentUser?.Id ?? 1,
                                CreatedAt = DateTime.Now
                            };

                            context.InventoryMovements.Add(movement);

                            // Se o produto for um produto virtual de ingrediente,
                            // também precisamos atualizar o ingrediente original
                            if (product.Code != null && product.Code.StartsWith("ING_"))
                            {
                                // Extrair o ID do ingrediente do código
                                var codeParts = product.Code.Split('_');
                                if (codeParts.Length >= 2 && int.TryParse(codeParts[1], out int ingredientId))
                                {
                                    var ingredient = await context.Ingredients.FindAsync(ingredientId);
                                    if (ingredient != null)
                                    {
                                        // Atualizar o stock do ingrediente
                                        ingredient.Stock += item.Quantity;

                                        Debug.WriteLine($"✅ Ingrediente {ingredient.Name} atualizado: +{item.Quantity} unidades");
                                    }
                                }
                            }
                        }
                    }

                    await context.SaveChangesAsync();

                    // Enviar e-mail de confirmação
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await SendPurchaseReceivedEmailAsync(dbPurchase);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"❌ Erro ao enviar email de recebimento: {ex.Message}");
                        }
                    });
                });

                UpdateDailyClosingExpenses();

                Mouse.OverrideCursor = null;

                // Recarregar tudo
                LoadPurchases();
                LoadInventoryMovements();
                RefreshProducts();
                LoadAllIngredients(); // Importante: recarregar ingredientes
                UpdateStockValue();

                ShowSuccessMessage($"✅ Compra {purchase.PurchaseNumber} recebida com sucesso! Ingredientes e produtos atualizados.");
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                ShowErrorMessage($"Erro ao receber compra: {ex.Message}");
            }
        }
        private async Task SendPurchaseReceivedEmailAsync(PurchaseOrder purchase)
        {
            try
            {
                var config = await ExecuteDbAsync(async context =>
                    await context.RestaurantConfigs.FirstOrDefaultAsync());

                if (config == null || string.IsNullOrEmpty(config.EmailAddress) ||
                    string.IsNullOrEmpty(config.RecipientEmail) || !config.SendReports)
                {
                    Debug.WriteLine("⚠️ Email não configurado para envio de relatórios.");
                    return;
                }

                string restaurantName = config.RestaurantName ?? "AyGestRest";
                string currencySymbol = config.CurrencySymbol ?? "MTn";

                string subject = $"✅ COMPRA RECEBIDA - {purchase.PurchaseNumber} - {restaurantName}";

                string body = $@"
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; color: #333; }}
        .header {{ background: linear-gradient(135deg, #00C853 0%, #009624 100%); color: white; padding: 20px; border-radius: 5px; margin-bottom: 20px; }}
        .info {{ background: #f8f9fa; padding: 15px; border-left: 4px solid #00C853; margin-bottom: 20px; }}
        table {{ border-collapse: collapse; width: 100%; margin-bottom: 20px; }}
        th {{ background: linear-gradient(135deg, #00C853 0%, #009624 100%); color: white; padding: 12px; text-align: left; }}
        td {{ padding: 10px; border-bottom: 1px solid #ddd; }}
        .total {{ font-size: 18px; font-weight: bold; color: #00C853; text-align: right; margin-top: 20px; }}
        .footer {{ margin-top: 30px; padding-top: 20px; border-top: 1px solid #ddd; font-size: 12px; color: #666; }}
        .success-badge {{ background-color: #00C853; color: white; padding: 3px 8px; border-radius: 3px; font-weight: bold; }}
    </style>
</head>
<body>
    <div class='header'>
        <h2>✅ COMPRA RECEBIDA COM SUCESSO</h2>
    </div>

    <div class='info'>
        <h3>📋 Informações do Recebimento</h3>
        <table>
            <tr>
                <td><strong>Número da Compra:</strong> {purchase.PurchaseNumber}</td>
                <td><strong>Data do Recebimento:</strong> {DateTime.Now:dd/MM/yyyy HH:mm}</td>
            </tr>
            <tr>
                <td><strong>Fornecedor:</strong> {purchase.Supplier?.Name ?? "N/A"}</td>
                <td><strong>NIF:</strong> {purchase.Supplier?.NIF ?? "N/A"}</td>
            </tr>
            <tr>
                <td><strong>Status:</strong> <span class='success-badge'>RECEBIDA</span></td>
                <td><strong>Total da Compra:</strong> {currencySymbol} {purchase.TotalAmount:N2}</td>
            </tr>
            <tr>
                <td colspan='2'><strong>Data da Compra:</strong> {purchase.CreatedAt:dd/MM/yyyy HH:mm}</td>
            </tr>
        </table>
    </div>

    <div class='info'>
        <h3>🛒 Itens Recebidos</h3>
        <table>
            <thead>
                <tr>
                    <th>Produto</th>
                    <th>Código</th>
                    <th>Quantidade</th>
                    <th>Preço Unit.</th>
                    <th>Subtotal</th>
                </tr>
            </thead>
            <tbody>";

                // Buscar os itens da compra com os produtos
                await ExecuteDbAsync(async context =>
                {
                    var items = await context.PurchaseOrderItems
                        .Include(i => i.Product)
                        .Where(i => i.PurchaseOrderId == purchase.Id)
                        .ToListAsync();

                    foreach (var item in items)
                    {
                        body += $@"
                <tr>
                    <td>{item.Product?.Name ?? "N/A"}</td>
                    <td>{item.Product?.Code ?? "N/A"}</td>
                    <td>{item.Quantity:N0}</td>
                    <td>{currencySymbol} {item.UnitPrice:N2}</td>
                    <td><strong>{currencySymbol} {item.Subtotal:N2}</strong></td>
                </tr>";
                    }
                });

                body += $@"
            </tbody>
        </table>
    </div>

    <div class='total'>
        TOTAL RECEBIDO: {currencySymbol} {purchase.TotalAmount:N2}
    </div>

    <p style='margin-top: 20px;'>
        📦 Os produtos foram adicionados ao inventário automaticamente.
    </p>

    <div class='footer'>
        <p><strong>{restaurantName}</strong></p>
        <p>Sistema de Gestão AyGestRest</p>
        <p>{DateTime.Now:dd/MM/yyyy HH:mm}</p>
    </div>
</body>
</html>";

                bool emailSent = SettingsControl.SendEmail(subject, body, true);

                if (emailSent)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        ShowSuccessMessage($"📧 Email de confirmação enviado para {config.RecipientEmail}");
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro ao enviar email de recebimento: {ex.Message}");
            }
        }

        private async void BtnCancelPurchase_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not PurchaseOrder purchase) return;

            if (purchase.Status == "Recebida")
            {
                ShowErrorMessage("Não é possível cancelar uma compra já recebida.");
                return;
            }

            var result = MessageBox.Show($"Cancelar a compra {purchase.PurchaseNumber}?\n" +
                                        "Esta ação não poderá ser desfeita.",
                "Confirmar Cancelamento", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                await ExecuteDbAsync(async context =>
                {
                    purchase.Status = "Cancelada";
                    await context.SaveChangesAsync();
                });

                LoadPurchases();
                ShowSuccessMessage($"Compra {purchase.PurchaseNumber} cancelada.");
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Erro ao cancelar compra: {ex.Message}");
            }
        }

        // ========== MÉTODOS AUXILIARES ==========
        private void ShowSuccessMessage(string message)
        {
            Debug.WriteLine($"✅ {message}");
            _snackbarQueue.Enqueue(message, "OK", () => { });
        }

        private void ShowErrorMessage(string message)
        {
            Debug.WriteLine($"❌ {message}");
            _snackbarQueue.Enqueue($"❌ {message}", "OK", () => { });
        }

        private void CloseProductPopup_Click(object sender, RoutedEventArgs e)
        {
            OverlayGrid.Visibility = Visibility.Collapsed;
            ProductPopup.Visibility = Visibility.Collapsed;
            ClearProductForm();
        }

        private void CloseIngredientPopup_Click(object sender, RoutedEventArgs e)
        {
            OverlayGrid.Visibility = Visibility.Collapsed;
            IngredientPopup.Visibility = Visibility.Collapsed;
            ClearIngredientForm();
        }

        private void CloseInventoryMovementPopup_Click(object sender, RoutedEventArgs e)
        {
            OverlayGrid.Visibility = Visibility.Collapsed;
            InventoryMovementPopup.Visibility = Visibility.Collapsed;
            ClearMovementForm();
        }

        private void CloseSupplierPopup_Click(object sender, RoutedEventArgs e)
        {
            OverlayGrid.Visibility = Visibility.Collapsed;
            SupplierPopup.Visibility = Visibility.Collapsed;
            ClearSupplierForm();
        }

        private void ClosePurchasePopup_Click(object sender, RoutedEventArgs e)
        {
            OverlayGrid.Visibility = Visibility.Collapsed;
            PurchasePopup.Visibility = Visibility.Collapsed;
            ClearPurchaseForm();
        }

        // ========== EVENTOS EXISTENTES ==========
        private void ChkIsComposite_CheckChanged(object sender, RoutedEventArgs e)
        {
            CardRecipe.Visibility = ChkIsComposite.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            OnFormChanged(sender, e);

            if (ChkIsComposite.IsChecked != true)
            {
                _currentRecipe.Clear();
                DgRecipe.ItemsSource = _currentRecipe;
                UpdateRecipeTotalCost();
            }
        }

        private void BtnAddIngredient_Click(object sender, RoutedEventArgs e)
        {
            if (CmbAddIngredient.SelectedItem is not Ingredient ing)
            {
                ShowErrorMessage("Selecione um ingrediente.");
                CmbAddIngredient.Focus();
                return;
            }

            if (!decimal.TryParse(TxtQuantityAdd.Text, out decimal qty) || qty <= 0)
            {
                ShowErrorMessage("Quantidade inválida.");
                TxtQuantityAdd.Focus();
                return;
            }

            if (_currentRecipe.Any(r => r.IngredientId == ing.Id))
            {
                ShowErrorMessage("Este ingrediente já foi adicionado.");
                return;
            }

            var pi = new ProductIngredient
            {
                IngredientId = ing.Id,
                Ingredient = ing,
                QuantityUsed = (int)qty
            };

            _currentRecipe.Add(pi);
            DgRecipe.ItemsSource = null;
            DgRecipe.ItemsSource = _currentRecipe;
            UpdateRecipeTotalCost();
            UpdateRecipeSummary();

            BtnSave.IsEnabled = true;

            TxtQuantityAdd.Text = "1";
            CmbAddIngredient.SelectedIndex = -1;
        }

        private void BtnRemoveIngredient_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is ProductIngredient pi)
            {
                _currentRecipe.Remove(pi);
                UpdateRecipeTotalCost();
                UpdateRecipeSummary();
                BtnSave.IsEnabled = true;
            }
        }

        private void UpdateRecipeTotalCost()
        {
            decimal total = _currentRecipe.Sum(pi => pi.QuantityUsed * (pi.Ingredient?.Cost ?? 0));
            TxtTotalRecipeCost.Text = total.ToString("N2") + " MTn";
        }

        private void UpdateRecipeSummary()
        {
            int count = _currentRecipe.Count;
            TxtRecipeSummary.Text = $"{count} ingrediente{(count != 1 ? "s" : "")} adicionado{(count != 1 ? "s" : "")}";
        }

        private void DgIngredients_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgIngredients.SelectedItem is Ingredient i)
                FillIngredientForm(i);
            else
                ClearIngredientForm();
        }

        private void EditIngredient_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is Ingredient ingredient)
            {
                FillIngredientForm(ingredient);
                OverlayGrid.Visibility = Visibility.Visible;
                IngredientPopup.Visibility = Visibility.Visible;
            }
        }

        private void BtnNewIngredient_Click(object sender, RoutedEventArgs e)
        {
            ClearIngredientForm();
            _currentIngredient = null;

            OverlayGrid.Visibility = Visibility.Visible;
            IngredientPopup.Visibility = Visibility.Visible;

            TxtIngredientName.Focus();
            ShowSuccessMessage("Preencha os dados do novo ingrediente.");
        }

        private void BtnClearIngredient_Click(object sender, RoutedEventArgs e)
        {
            ClearIngredientForm();
        }

        private void ProductItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is Product product)
            {
                FillProductForm(product);
                OverlayGrid.Visibility = Visibility.Visible;
                ProductPopup.Visibility = Visibility.Visible;
                e.Handled = true;
            }
        }

        private void ProductAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is Product product)
            {
                var menu = new ContextMenu();

                var editItem = new MenuItem
                {
                    Header = "Editar",
                    Icon = new PackIcon { Kind = PackIconKind.Pencil }
                };
                editItem.Click += (s, args) =>
                {
                    FillProductForm(product);
                    OverlayGrid.Visibility = Visibility.Visible;
                    ProductPopup.Visibility = Visibility.Visible;
                };

                var deleteItem = new MenuItem
                {
                    Header = "Excluir",
                    Icon = new PackIcon { Kind = PackIconKind.Delete }
                };
                deleteItem.Click += async (s, args) =>
                {
                    var hasSales = await ExecuteDbAsync(async context =>
                        await context.OrderItems.AnyAsync(si => si.ProductId == product.Id));

                    if (hasSales)
                    {
                        ShowErrorMessage("Não é possível excluir produto que já foi vendido.");
                        return;
                    }

                    var hasMovements = await ExecuteDbAsync(async context =>
                        await context.InventoryMovements.AnyAsync(m => m.ProductId == product.Id));

                    if (hasMovements)
                    {
                        ShowErrorMessage("Não é possível excluir produto com movimentos de inventário.");
                        return;
                    }

                    var result = MessageBox.Show($"Excluir o produto '{product.Name}'?", "Confirmação",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            await ExecuteDbAsync(async context =>
                            {
                                context.Products.Remove(product);
                                await context.SaveChangesAsync();
                            });

                            RefreshProducts();
                            RefreshProductListControl();
                            ClearProductForm();
                            UpdateCounters();
                            UpdateStockValue();
                            ShowSuccessMessage("Produto excluído com sucesso.");
                        }
                        catch (Exception ex)
                        {
                            ShowErrorMessage($"Erro ao excluir produto: {ex.Message}");
                        }
                    }
                };

                var duplicateItem = new MenuItem
                {
                    Header = "Duplicar",
                    Icon = new PackIcon { Kind = PackIconKind.ContentCopy }
                };
                duplicateItem.Click += (s, args) => DuplicateProduct(product);

                var movementItem = new MenuItem
                {
                    Header = "Movimento de Stock",
                    Icon = new PackIcon { Kind = PackIconKind.ArrowUpDown }
                };
                movementItem.Click += (s, args) =>
                {
                    FillProductForm(product);
                    BtnNewMovement_Click(sender, e);
                    CmbMovementProduct.SelectedItem = product;
                };

                menu.Items.Add(editItem);
                menu.Items.Add(deleteItem);
                menu.Items.Add(new Separator());
                menu.Items.Add(duplicateItem);
                menu.Items.Add(movementItem);

                menu.PlacementTarget = button;
                menu.IsOpen = true;
            }
        }

        private void DuplicateProduct(Product originalProduct)
        {
            try
            {
                var newProduct = new Product
                {
                    Name = $"{originalProduct.Name} (Cópia)",
                    Code = $"COPY_{originalProduct.Code}",
                    Description = originalProduct.Description,
                    Price = originalProduct.Price,
                    Stock = 0,
                    Active = originalProduct.Active,
                    IsComposite = originalProduct.IsComposite,
                    CategoryId = originalProduct.CategoryId,
                    Barcode = null,
                    Image = originalProduct.Image
                };

                ExecuteDb(context =>
                {
                    context.Products.Add(newProduct);
                    context.SaveChanges();
                });

                RefreshProducts();
                RefreshProductListControl();
                FillProductForm(newProduct);
                ShowSuccessMessage("Produto duplicado com sucesso.");
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Erro ao duplicar produto: {ex.Message}");
            }
        }

        private void BtnRemoveImage_Click(object sender, RoutedEventArgs e)
        {
            ImgProduct.Source = null;

            if (_currentProduct != null)
            {
                _currentProduct.Image = null;
            }

            ShowSuccessMessage("Imagem removida do produto.");
            BtnSave.IsEnabled = true;
        }

        private void BtnRemoveRecipeItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is int ingredientId)
                {
                    var item = _currentRecipe.FirstOrDefault(pi => pi.IngredientId == ingredientId);
                    if (item != null)
                    {
                        _currentRecipe.Remove(item);

                        DgRecipe.ItemsSource = null;
                        DgRecipe.ItemsSource = _currentRecipe;

                        UpdateRecipeTotalCost();
                        UpdateRecipeSummary();
                        BtnSave.IsEnabled = true;

                        ShowSuccessMessage("Ingrediente removido da receita.");
                    }
                }
                else if (sender is Button button2 && button2.DataContext is ProductIngredient pi)
                {
                    _currentRecipe.Remove(pi);
                    DgRecipe.ItemsSource = null;
                    DgRecipe.ItemsSource = _currentRecipe;
                    UpdateRecipeTotalCost();
                    UpdateRecipeSummary();
                    BtnSave.IsEnabled = true;
                    ShowSuccessMessage("Ingrediente removido da receita.");
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Erro ao remover ingrediente: {ex.Message}");
            }
        }

        private void BtnLoadImage_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp|Todos os arquivos|*.*",
                Title = "Selecionar imagem do produto"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                var imageBytes = File.ReadAllBytes(dlg.FileName);

                if (imageBytes.Length > 5 * 1024 * 1024)
                {
                    ShowErrorMessage("A imagem deve ter no máximo 5MB.");
                    return;
                }

                if (_currentProduct != null)
                {
                    _currentProduct.Image = imageBytes;
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(dlg.FileName);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                ImgProduct.Source = bitmap;

                ShowSuccessMessage("Imagem carregada com sucesso.");
                BtnSave.IsEnabled = true;
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Erro ao carregar imagem: {ex.Message}");
            }
        }

        // ========== FILTROS ==========
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshProducts();
        private void CmbFilterCategory_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshProducts();
        private void CmbFilterStatus_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshProducts();
        private void CmbFilterStock_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshProducts();

        private void CmbInventoryFilterType_SelectionChanged(object sender, SelectionChangedEventArgs e) => LoadInventoryMovements();
        private void CmbInventoryFilterProduct_SelectionChanged(object sender, SelectionChangedEventArgs e) => LoadInventoryMovements();
        private void DpInventoryFrom_SelectedDateChanged(object sender, SelectionChangedEventArgs e) => LoadInventoryMovements();
        private void DpInventoryTo_SelectedDateChanged(object sender, SelectionChangedEventArgs e) => LoadInventoryMovements();

        private void CmbPurchaseFilterStatus_SelectionChanged(object sender, SelectionChangedEventArgs e) => LoadPurchases();
        private void CmbPurchaseFilterSupplier_SelectionChanged(object sender, SelectionChangedEventArgs e) => LoadPurchases();
        private void DpPurchaseFrom_SelectedDateChanged(object sender, SelectionChangedEventArgs e) => LoadPurchases();
        private void DpPurchaseTo_SelectedDateChanged(object sender, SelectionChangedEventArgs e) => LoadPurchases();

        // ========== EVENTOS DE FORNECEDORES ==========
        private void DgSuppliers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgSuppliers.SelectedItem is Supplier supplier)
                FillSupplierForm(supplier);
            else
                ClearSupplierForm();
        }

        private void EditSupplier_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is Supplier supplier)
            {
                FillSupplierForm(supplier);
                OverlayGrid.Visibility = Visibility.Visible;
                SupplierPopup.Visibility = Visibility.Visible;
            }
        }

        // ========== CÓDIGO DE BARRAS ==========

        private void GenerateBarcodePreviews(List<BarcodeProduct> products)
        {
            foreach (var product in products)
            {
                if (string.IsNullOrEmpty(product.Barcode))
                {
                    product.Barcode = GenerateBarcodeValue(product.Code, product.Price);
                }

                product.BarcodeImage = GenerateBarcodeImage(product.Barcode, product.Name, product.Price, product.Code);
                product.HasBarcode = !string.IsNullOrEmpty(product.Barcode);
            }

            DgBarcodeProducts.ItemsSource = null;
            DgBarcodeProducts.ItemsSource = products;
        }

        private string GenerateBarcodeValue(string productCode, decimal price)
        {
            try
            {
                var pricePart = ((int)(price * 100)).ToString("D8");
                var timestampPart = DateTime.Now.ToString("ddHHmm");

                var baseBarcode = $"{productCode}{pricePart}{timestampPart}";

                if (baseBarcode.Length > 13)
                {
                    baseBarcode = baseBarcode.Substring(0, 13);
                }
                else if (baseBarcode.Length < 13)
                {
                    baseBarcode = baseBarcode.PadRight(13, '0');
                }

                return baseBarcode;
            }
            catch (Exception)
            {
                var random = new Random();
                return random.Next(100000000, 999999999).ToString().PadRight(13, '0').Substring(0, 13);
            }
        }

        private BitmapImage GenerateBarcodeImage(string barcodeValue, string productName, decimal price, string productCode)
        {
            try
            {
                var barcodeDraw = BarcodeDrawFactory.Code128WithChecksum;
                var barcodeImage = barcodeDraw.Draw(barcodeValue, 40);

                using (var memory = new MemoryStream())
                {
                    barcodeImage.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                    memory.Position = 0;

                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.StreamSource = memory;
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();

                    return bitmapImage;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void UpdateBarcodePreview()
        {
            try
            {
                var selectedProduct = (DgBarcodeProducts.ItemsSource as List<BarcodeProduct>)?
                    .FirstOrDefault(p => p.IsSelected);

                if (selectedProduct == null && DgBarcodeProducts.ItemsSource is List<BarcodeProduct> products && products.Count > 0)
                {
                    selectedProduct = products[0];
                }

                if (selectedProduct != null)
                {
                    TxtPreviewName.Text = selectedProduct.Name;
                    TxtPreviewPrice.Text = $"Preço: {selectedProduct.Price:N2} MTn";
                    TxtPreviewCode.Text = $"Código: {selectedProduct.Code}";
                    TxtPreviewBarcode.Text = selectedProduct.Barcode ?? "Sem código";

                    var barcodeImage = GenerateBarcodeImage(
                        selectedProduct.Barcode ?? GenerateBarcodeValue(selectedProduct.Code, selectedProduct.Price),
                        selectedProduct.Name,
                        selectedProduct.Price,
                        selectedProduct.Code
                    );

                    if (barcodeImage != null)
                    {
                        ImgBarcodePreview.Source = barcodeImage;
                    }
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Erro ao atualizar visualização: {ex.Message}");
            }
        }

        private async void BtnGenerateAllBarcodes_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var products = await ExecuteDbAsync(async context =>
                    await context.Products
                        .Where(p => string.IsNullOrEmpty(p.Barcode) &&
                                   !string.IsNullOrEmpty(p.Name) &&
                                   !string.IsNullOrEmpty(p.Code) &&
                                   p.Price > 0)
                        .ToListAsync());

                if (products.Count == 0)
                {
                    ShowSuccessMessage("Todos os produtos já possuem código de barras.");
                    return;
                }

                int generatedCount = 0;
                foreach (var product in products)
                {
                    try
                    {
                        product.Barcode = GenerateBarcodeValue(product.Code, product.Price);
                        generatedCount++;
                    }
                    catch
                    {
                        // Continuar com o próximo produto
                    }
                }

                await ExecuteDbAsync(async context =>
                {
                    context.Products.UpdateRange(products);
                    await context.SaveChangesAsync();
                });

                LoadBarcodeProducts();
                RefreshProducts();

                ShowSuccessMessage($"{generatedCount} códigos de barras gerados com sucesso!");
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Erro ao gerar códigos de barras: {ex.Message}");
            }
        }

        // ========== MÉTODOS PARA ANÁLISE HISTÓRICA ==========

        private void InitializeHistoryTab()
        {
            DpHistoryFrom.SelectedDate = DateTime.Today.AddDays(-30);
            DpHistoryTo.SelectedDate = DateTime.Today;

            LoadHistoryProducts();
            LoadHistoryCategories();

            DpHistoryFrom.SelectedDateChanged += (s, e) => UpdateHistoryPeriodText();
            DpHistoryTo.SelectedDateChanged += (s, e) => UpdateHistoryPeriodText();
            CmbHistoryProduct.SelectionChanged += (s, e) => UpdateHistoryPeriodText();

            UpdateHistoryPeriodText();
        }

        private void LoadHistoryProducts()
        {
            ExecuteDb(context =>
            {
                try
                {
                    var products = context.Products
                        .Where(p => p.Active)
                        .OrderBy(p => p.Name)
                        .ToList();

                    var allProducts = new List<Product>
                    {
                        new Product { Id = 0, Name = "Todos os produtos" }
                    };
                    allProducts.AddRange(products);

                    Dispatcher.Invoke(() =>
                    {
                        CmbHistoryProduct.ItemsSource = allProducts;
                        CmbHistoryProduct.SelectedIndex = 0;
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => ShowErrorMessage($"Erro ao carregar produtos: {ex.Message}"));
                }
            });
        }

        private void LoadHistoryCategories()
        {
            ExecuteDb(context =>
            {
                try
                {
                    var categories = context.ProductCategories
                        .Where(c => c.Ativo)
                        .OrderBy(c => c.Nome)
                        .ToList();

                    var allCategories = new List<ProductCategory>
                    {
                        new ProductCategory { Id = 0, Nome = "Todas categorias" }
                    };
                    allCategories.AddRange(categories);

                    Dispatcher.Invoke(() =>
                    {
                        CmbHistoryCategory.ItemsSource = allCategories;
                        CmbHistoryCategory.SelectedIndex = 0;
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => ShowErrorMessage($"Erro ao carregar categorias: {ex.Message}"));
                }
            });
        }

        private async void BtnAnalyzeHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!DpHistoryFrom.SelectedDate.HasValue || !DpHistoryTo.SelectedDate.HasValue)
                {
                    ShowErrorMessage("Selecione as datas inicial e final.");
                    return;
                }

                DateTime startDate = DpHistoryFrom.SelectedDate.Value;
                DateTime endDate = DpHistoryTo.SelectedDate.Value.AddDays(1).AddSeconds(-1);

                if (startDate > endDate)
                {
                    ShowErrorMessage("A data inicial deve ser anterior à data final.");
                    return;
                }

                if ((endDate - startDate).TotalDays > 365)
                {
                    ShowErrorMessage("O período máximo de análise é de 1 ano.");
                    return;
                }

                Mouse.OverrideCursor = Cursors.Wait;

                int? productId = null;
                if (CmbHistoryProduct.SelectedValue is int selectedProductId && selectedProductId > 0)
                {
                    productId = selectedProductId;
                }

                int? categoryId = null;
                if (CmbHistoryCategory.SelectedValue is int selectedCategoryId && selectedCategoryId > 0)
                {
                    categoryId = selectedCategoryId;
                }

                bool showOnlyChanges = ChkShowOnlyChanges.IsChecked ?? true;
                bool includeZeroStock = ChkIncludeZeroStock.IsChecked ?? false;

                var results = await AnalyzeStockHistory(startDate, endDate, productId, categoryId, includeZeroStock);

                if (showOnlyChanges)
                {
                    results = results.Where(r => r.HistoricalData.Variation != 0).ToList();
                }

                UpdateHistoryResults(results);
                UpdateHistoryStatistics(results);
                UpdateHistoryPeriodText();

                Mouse.OverrideCursor = null;

                ShowSuccessMessage($"Análise concluída: {results.Count} produtos analisados.");
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                ShowErrorMessage($"Erro ao analisar histórico: {ex.Message}");
            }
        }

        private async Task<List<StockComparisonResult>> AnalyzeStockHistory(
            DateTime startDate,
            DateTime endDate,
            int? productId = null,
            int? categoryId = null,
            bool includeZeroStock = false)
        {
            var results = new List<StockComparisonResult>();

            try
            {
                await ExecuteDbAsync(async context =>
                {
                    var productsQuery = context.Products
                        .Include(p => p.Category)
                        .Where(p => p.Active);

                    if (productId.HasValue && productId > 0)
                    {
                        productsQuery = productsQuery.Where(p => p.Id == productId.Value);
                    }

                    if (categoryId.HasValue && categoryId > 0)
                    {
                        productsQuery = productsQuery.Where(p => p.CategoryId == categoryId.Value);
                    }

                    if (!includeZeroStock)
                    {
                        productsQuery = productsQuery.Where(p => p.Stock > 0);
                    }

                    var products = await productsQuery.ToListAsync();

                    if (products.Count > 20)
                    {
                        var productIds = products.Select(p => p.Id).ToList();
                        var batchResults = await CalculateBatchStockHistory(productIds, startDate, endDate);

                        foreach (var product in products)
                        {
                            if (batchResults.TryGetValue(product.Id, out var dailyStocks))
                            {
                                if (dailyStocks.TryGetValue(startDate.Date, out decimal stockStart) &&
                                    dailyStocks.TryGetValue(endDate.Date, out decimal stockEnd))
                                {
                                    var historicalData = CreateHistoricalData(product, stockStart, stockEnd);
                                    var result = CreateComparisonResult(historicalData);
                                    results.Add(result);
                                }
                            }
                        }
                    }
                    else
                    {
                        foreach (var product in products)
                        {
                            var stockStart = await GetProductStockAtDate(product.Id, startDate);
                            var stockEnd = await GetProductStockAtDate(product.Id, endDate);

                            var historicalData = CreateHistoricalData(product, stockStart, stockEnd);
                            var result = CreateComparisonResult(historicalData);
                            results.Add(result);
                        }
                    }
                });

                return results.OrderByDescending(r => Math.Abs(r.HistoricalData.VariationPercentage)).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro na análise: {ex.Message}");
                return results;
            }
        }

        private ProductStockHistory CreateHistoricalData(Product product, decimal stockStart, decimal stockEnd)
        {
            return new ProductStockHistory
            {
                ProductId = product.Id,
                ProductName = product.Name,
                ProductCode = product.Code ?? "N/A",
                CategoryName = product.Category?.Nome ?? "N/A",
                StockAtStart = stockStart,
                StockAtEnd = stockEnd,
                Variation = stockEnd - stockStart,
                VariationPercentage = stockStart != 0 ? (stockEnd - stockStart) / stockStart : 0
            };
        }

        private StockComparisonResult CreateComparisonResult(ProductStockHistory historicalData)
        {
            return new StockComparisonResult
            {
                HistoricalData = historicalData,
                Trend = DetermineTrend(historicalData.VariationPercentage),
                TrendIcon = DetermineTrendIcon(historicalData.VariationPercentage),
                TrendColor = DetermineTrendColor(historicalData.VariationPercentage)
            };
        }

        private async Task<decimal> GetProductStockAtDate(int productId, DateTime date)
        {
            try
            {
                if (ProductStockHistoryCache.TryGetFromCache(productId, date, out decimal cachedStock))
                {
                    return cachedStock;
                }

                decimal calculatedStock = await CalculateStockForDate(productId, date);
                ProductStockHistoryCache.AddToCache(productId, date, calculatedStock);

                return calculatedStock;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao calcular stock: {ex.Message}");
                return 0;
            }
        }

        private async Task<decimal> CalculateStockForDate(int productId, DateTime date)
        {
            return await ExecuteDbAsync(async context =>
            {
                var lastMovement = await context.InventoryMovements
                    .Where(m => m.ProductId == productId &&
                               m.CreatedAt.Date <= date.Date)
                    .OrderByDescending(m => m.CreatedAt)
                    .FirstOrDefaultAsync();

                if (lastMovement != null)
                {
                    return lastMovement.NewStock;
                }

                var product = await context.Products.FindAsync(productId);
                return product?.Stock ?? 0;
            });
        }

        private async Task<List<DailyStockData>> GetDailyStockData(int productId, DateTime startDate, DateTime endDate)
        {
            var dailyData = new List<DailyStockData>();

            try
            {
                await ExecuteDbAsync(async context =>
                {
                    var movements = await context.InventoryMovements
                        .Where(m => m.ProductId == productId &&
                                   m.CreatedAt >= startDate &&
                                   m.CreatedAt <= endDate)
                        .OrderBy(m => m.CreatedAt)
                        .ToListAsync();

                    var dailyGroups = movements
                        .GroupBy(m => m.CreatedAt.Date)
                        .OrderBy(g => g.Key);

                    foreach (var dayGroup in dailyGroups)
                    {
                        var dayData = new DailyStockData
                        {
                            Date = dayGroup.Key,
                            Sales = dayGroup.Where(m => m.MovementType == "Saída" &&
                                                       (m.Reason.Contains("Venda") || m.Reason.Contains("saída")))
                                          .Sum(m => m.Quantity),
                            Purchases = dayGroup.Where(m => m.MovementType == "Entrada" &&
                                                           (m.Reason.Contains("Compra") || m.Reason.Contains("entrada")))
                                             .Sum(m => m.Quantity),
                            Adjustments = dayGroup.Where(m => m.MovementType == "Entrada" || m.MovementType == "Saída")
                                                .Sum(m => m.MovementType == "Entrada" ? m.Quantity : -m.Quantity)
                        };

                        var lastMovement = dayGroup.OrderByDescending(m => m.CreatedAt).FirstOrDefault();
                        dayData.Stock = lastMovement?.NewStock ?? await GetProductStockAtDate(productId, dayGroup.Key);

                        dailyData.Add(dayData);
                    }
                });
            }
            catch (Exception)
            {
                // Retornar lista vazia em caso de erro
            }

            return dailyData;
        }

        private string DetermineTrend(decimal variationPercentage)
        {
            if (variationPercentage > 0.05m)
                return "AUMENTO";
            else if (variationPercentage < -0.05m)
                return "QUEDA";
            else
                return "ESTÁVEL";
        }

        private string DetermineTrendIcon(decimal variationPercentage)
        {
            if (variationPercentage > 0.05m)
                return "↗️";
            else if (variationPercentage < -0.05m)
                return "↘️";
            else
                return "➡️";
        }

        private System.Windows.Media.Brush DetermineTrendColor(decimal variationPercentage)
        {
            if (variationPercentage > 0.05m)
                return System.Windows.Media.Brushes.Green;
            else if (variationPercentage < -0.05m)
                return System.Windows.Media.Brushes.Red;
            else
                return System.Windows.Media.Brushes.Gray;
        }

        private void UpdateHistoryResults(List<StockComparisonResult> results)
        {
            DgStockHistory.ItemsSource = results;
            TxtHistoryCount.Text = $" ({results.Count} produtos)";
            TxtHistoryPeriodResults.Text = $"{DpHistoryFrom.SelectedDate:dd/MM/yyyy} → {DpHistoryTo.SelectedDate:dd/MM/yyyy} • {results.Count} produtos analisados";

            BorderNoData.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateHistoryStatistics(List<StockComparisonResult> results)
        {
            if (results == null || results.Count == 0)
            {
                ResetHistoryStatistics();
                return;
            }

            int increased = results.Count(r => r.HistoricalData.VariationPercentage > 0.05m);
            int decreased = results.Count(r => r.HistoricalData.VariationPercentage < -0.05m);
            int stable = results.Count(r => Math.Abs(r.HistoricalData.VariationPercentage) <= 0.05m);

            TxtIncreasedCount.Text = increased.ToString();
            TxtDecreasedCount.Text = decreased.ToString();
            TxtStableCount.Text = stable.ToString();

            decimal totalVariation = results.Sum(r => r.HistoricalData.Variation);
            TxtTotalVariation.Text = $"{totalVariation:N0} unidades";

            var maxIncrease = results.Where(r => r.HistoricalData.VariationPercentage > 0)
                                    .DefaultIfEmpty()
                                    .Max(r => r?.HistoricalData.VariationPercentage ?? 0);
            var maxDecrease = results.Where(r => r.HistoricalData.VariationPercentage < 0)
                                    .DefaultIfEmpty()
                                    .Min(r => r?.HistoricalData.VariationPercentage ?? 0);

            TxtMaxIncrease.Text = $"{maxIncrease:P1}";
            TxtMaxDecrease.Text = $"{Math.Abs(maxDecrease):P1}";

            TxtProductsAnalyzed.Text = results.Count.ToString();
        }

        private void ResetHistoryStatistics()
        {
            TxtIncreasedCount.Text = "0";
            TxtDecreasedCount.Text = "0";
            TxtStableCount.Text = "0";
            TxtTotalVariation.Text = "0 unidades";
            TxtMaxIncrease.Text = "0%";
            TxtMaxDecrease.Text = "0%";
            TxtProductsAnalyzed.Text = "0";
        }

        private void UpdateHistoryPeriodText()
        {
            if (DpHistoryFrom.SelectedDate.HasValue && DpHistoryTo.SelectedDate.HasValue)
            {
                string productFilter = "";
                if (CmbHistoryProduct.SelectedItem is Product selectedProduct && selectedProduct.Id > 0)
                {
                    productFilter = $" • Produto: {selectedProduct.Name}";
                }

                string categoryFilter = "";
                if (CmbHistoryCategory.SelectedItem is ProductCategory selectedCategory && selectedCategory.Id > 0)
                {
                    categoryFilter = $" • Categoria: {selectedCategory.Nome}";
                }

                TxtHistoryPeriod.Text =
                    $"{DpHistoryFrom.SelectedDate:dd/MM/yyyy} → {DpHistoryTo.SelectedDate:dd/MM/yyyy}" +
                    productFilter + categoryFilter;
            }
            else
            {
                TxtHistoryPeriod.Text = "Selecione um período e clique em Analisar";
            }
        }

        private async void BtnExportHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var results = DgStockHistory.ItemsSource as List<StockComparisonResult>;
                if (results == null || results.Count == 0)
                {
                    ShowErrorMessage("Nenhum dado para exportar. Execute uma análise primeiro.");
                    return;
                }

                var saveDialog = new SaveFileDialog
                {
                    Filter = "Arquivo PDF|*.pdf|Arquivo Excel|*.xlsx|Arquivo CSV|*.csv",
                    FileName = $"Analise_Stock_{DateTime.Now:yyyyMMdd_HHmm}",
                    DefaultExt = ".pdf"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    Mouse.OverrideCursor = Cursors.Wait;

                    var headers = new List<string>
                    {
                        "Produto", "Código", "Categoria",
                        "Stock Inicial", "Stock Final", "Variação", "Variação %", "Tendência"
                    };

                    var dataRows = new List<List<string>>();

                    foreach (var result in results.OrderByDescending(r => r.HistoricalData.VariationPercentage))
                    {
                        dataRows.Add(new List<string>
                        {
                            result.HistoricalData.ProductName,
                            result.HistoricalData.ProductCode,
                            result.HistoricalData.CategoryName,
                            result.HistoricalData.StockAtStart.ToString("N0"),
                            result.HistoricalData.StockAtEnd.ToString("N0"),
                            result.HistoricalData.Variation.ToString("N0"),
                            result.HistoricalData.VariationPercentage.ToString("P1"),
                            result.Trend
                        });
                    }

                    await ExecuteDbAsync(async context =>
                    {
                        var restaurantConfig = await context.RestaurantConfigs.FirstOrDefaultAsync();
                        string restaurantName = restaurantConfig?.RestaurantName ?? "AyGestRest";
                        string currencySymbol = restaurantConfig?.CurrencySymbol ?? "MTn";
                        byte[] logoBytes = restaurantConfig?.LogoBytes;

                        string reportTitle = "ANÁLISE HISTÓRICA DE STOCK";
                        string dateRange = TxtHistoryPeriod.Text;

                        var summaryRows = new List<string>
                        {
                            $"Período: {dateRange}",
                            $"Total de Produtos Analisados: {results.Count}",
                            $"Produtos com Aumento: {TxtIncreasedCount.Text}",
                            $"Produtos com Queda: {TxtDecreasedCount.Text}",
                            $"Produtos Estáveis: {TxtStableCount.Text}",
                            $"Variação Total do Stock: {TxtTotalVariation.Text}",
                            $"Gerado em: {DateTime.Now:dd/MM/yyyy HH:mm}"
                        };

                        if (saveDialog.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        {
                            await Task.Run(() => ExportToPdf(
                                saveDialog.FileName,
                                restaurantName,
                                reportTitle,
                                dateRange,
                                headers,
                                dataRows,
                                summaryRows,
                                currencySymbol,
                                logoBytes,
                                "stock_history"
                            ));
                        }
                        else if (saveDialog.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                        {
                            ExportToCsv(saveDialog.FileName, headers, dataRows, restaurantName, reportTitle, dateRange);
                        }
                        else
                        {
                            ExportToExcel(saveDialog.FileName, headers, dataRows, restaurantName, reportTitle, dateRange);
                        }
                    });

                    Mouse.OverrideCursor = null;
                    ShowSuccessMessage($"Relatório exportado: {System.IO.Path.GetFileName(saveDialog.FileName)}");

                    var openResult = MessageBox.Show("Deseja abrir o relatório?", "Exportação Concluída",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (openResult == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo(saveDialog.FileName) { UseShellExecute = true });
                    }
                }
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                ShowErrorMessage($"Erro ao exportar relatório: {ex.Message}");
            }
        }

        private async void BtnViewStockDetails_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ProductStockHistory history)
            {
                await ShowStockDetailsPopup(history);
            }
        }

        private async Task ShowStockDetailsPopup(ProductStockHistory history)
        {
            try
            {
                var popup = new Window
                {
                    Title = $"📊 Detalhes: {history.ProductName}",
                    Width = 600,
                    Height = 500,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Window.GetWindow(this),
                    Background = System.Windows.Media.Brushes.White,
                    WindowStyle = WindowStyle.ToolWindow
                };

                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var headerPanel = new StackPanel
                {
                    Background = System.Windows.Media.Brushes.White,
                    Margin = new Thickness(0, 0, 0, 10)
                };

                var title = new TextBlock
                {
                    Text = $"📊 {history.ProductName}",
                    FontWeight = FontWeights.Bold,
                    FontSize = 16,
                    Foreground = System.Windows.Media.Brushes.DarkSlateGray,
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var period = new TextBlock
                {
                    Text = $"Período: {DpHistoryFrom.SelectedDate:dd/MM/yyyy} → {DpHistoryTo.SelectedDate:dd/MM/yyyy}",
                    FontSize = 12,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    Margin = new Thickness(0, 0, 0, 4)
                };

                var variation = new TextBlock
                {
                    Text = $"Variação: {history.Variation:N0} unidades ({history.VariationPercentage:P1})",
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = history.Variation >= 0 ?
                        System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red
                };

                headerPanel.Children.Add(title);
                headerPanel.Children.Add(period);
                headerPanel.Children.Add(variation);

                Grid.SetRow(headerPanel, 0);
                grid.Children.Add(headerPanel);

                var dataGrid = new DataGrid
                {
                    AutoGenerateColumns = false,
                    IsReadOnly = true,
                    Margin = new Thickness(16, 0, 16, 16),
                    BorderBrush = System.Windows.Media.Brushes.LightGray,
                    BorderThickness = new Thickness(1),
                    FontSize = 11
                };

                dataGrid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
                {
                    Header = "Data",
                    Binding = new Binding("Date") { StringFormat = "dd/MM/yyyy" },
                    Width = 100
                });

                dataGrid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
                {
                    Header = "Stock",
                    Binding = new Binding("Stock") { StringFormat = "N0" },
                    Width = 80
                });

                dataGrid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
                {
                    Header = "Vendas",
                    Binding = new Binding("Sales") { StringFormat = "N0" },
                    Width = 80
                });

                dataGrid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
                {
                    Header = "Compras",
                    Binding = new Binding("Purchases") { StringFormat = "N0" },
                    Width = 80
                });

                dataGrid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
                {
                    Header = "Ajustes",
                    Binding = new Binding("Adjustments") { StringFormat = "N0" },
                    Width = 80
                });

                var dailyData = history.DailyData ?? await GetDailyStockData(
                    history.ProductId,
                    DpHistoryFrom.SelectedDate ?? DateTime.Today,
                    DpHistoryTo.SelectedDate ?? DateTime.Today);

                dataGrid.ItemsSource = dailyData.OrderBy(d => d.Date);

                Grid.SetRow(dataGrid, 1);
                grid.Children.Add(dataGrid);

                popup.Content = grid;
                popup.ShowDialog();
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Erro ao mostrar detalhes: {ex.Message}");
            }
        }

        private async void BtnCompareDates_Click(object sender, RoutedEventArgs e)
        {
            var compareWindow = new Window
            {
                Title = "📅 Comparar Stock entre Datas",
                Width = 500,
                Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = System.Windows.Media.Brushes.White
            };

            var stackPanel = new StackPanel
            {
                Margin = new Thickness(20)
            };

            var date1Panel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            date1Panel.Children.Add(new TextBlock
            {
                Text = "Primeira Data:",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var dpDate1 = new DatePicker
            {
                SelectedDate = DateTime.Today.AddDays(-7),
                Width = 200
            };
            date1Panel.Children.Add(dpDate1);

            var date2Panel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            date2Panel.Children.Add(new TextBlock
            {
                Text = "Segunda Data:",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var dpDate2 = new DatePicker
            {
                SelectedDate = DateTime.Today,
                Width = 200
            };
            date2Panel.Children.Add(dpDate2);

            var productPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
            productPanel.Children.Add(new TextBlock
            {
                Text = "Produto (opcional):",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var cmbProduct = new ComboBox
            {
                Width = 250,
                DisplayMemberPath = "Name",
                SelectedValuePath = "Id"
            };

            try
            {
                var products = await ExecuteDbAsync(async context =>
                    await context.Products
                        .Where(p => p.Active)
                        .OrderBy(p => p.Name)
                        .ToListAsync());

                cmbProduct.ItemsSource = products;
            }
            catch (Exception)
            {
                // Ignorar erro
            }

            productPanel.Children.Add(cmbProduct);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnCompare = new Button
            {
                Content = "🔍 Comparar",
                Background = System.Windows.Media.Brushes.DodgerBlue,
                Foreground = System.Windows.Media.Brushes.White,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand
            };

            var btnCancel = new Button
            {
                Content = "Cancelar",
                Background = System.Windows.Media.Brushes.LightGray,
                Padding = new Thickness(12, 6, 12, 6),
                Cursor = Cursors.Hand
            };

            btnCompare.Click += async (s, args) =>
            {
                if (!dpDate1.SelectedDate.HasValue || !dpDate2.SelectedDate.HasValue)
                {
                    MessageBox.Show("Selecione ambas as datas.", "Aviso",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                int? productId = null;
                if (cmbProduct.SelectedItem is Product selectedProduct)
                {
                    productId = selectedProduct.Id;
                }

                await ShowComparisonResults(
                    dpDate1.SelectedDate.Value,
                    dpDate2.SelectedDate.Value,
                    productId);

                compareWindow.Close();
            };

            btnCancel.Click += (s, args) => compareWindow.Close();

            buttonPanel.Children.Add(btnCompare);
            buttonPanel.Children.Add(btnCancel);

            stackPanel.Children.Add(date1Panel);
            stackPanel.Children.Add(date2Panel);
            stackPanel.Children.Add(productPanel);
            stackPanel.Children.Add(buttonPanel);

            compareWindow.Content = stackPanel;
            compareWindow.ShowDialog();
        }

        private void BtnClearHistoryFilters_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DpHistoryFrom.SelectedDate = DateTime.Today.AddDays(-30);
                DpHistoryTo.SelectedDate = DateTime.Today;

                if (CmbHistoryProduct.Items.Count > 0)
                    CmbHistoryProduct.SelectedIndex = 0;

                if (CmbHistoryCategory.Items.Count > 0)
                    CmbHistoryCategory.SelectedIndex = 0;

                ChkShowOnlyChanges.IsChecked = true;
                ChkIncludeZeroStock.IsChecked = false;

                DgStockHistory.ItemsSource = null;
                ResetHistoryStatistics();
                UpdateHistoryPeriodText();
                TxtHistoryPeriodResults.Text = "Execute uma análise para ver os resultados";

                BorderNoData.Visibility = Visibility.Visible;

                ShowSuccessMessage("Filtros limpos com sucesso!");
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Erro ao limpar filtros: {ex.Message}");
            }
        }

        private void BtnShowProductChart_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ProductStockHistory history)
            {
                ShowProductChart(history);
            }
        }

        private async void BtnRefreshCache_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                ProductStockHistoryCache.ClearCache();
                ProductStockHistoryCache.UpdateCacheTime();

                if (DpHistoryFrom.SelectedDate.HasValue && DpHistoryTo.SelectedDate.HasValue)
                {
                    await AnalyzeHistoryAsync();
                }

                ShowSuccessMessage($"Cache atualizado! {ProductStockHistoryCache.GetCacheCount()} itens em cache.");
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Erro ao atualizar cache: {ex.Message}");
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private async void BtnDebugTest_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MessageBox.Show("=== INICIANDO DIAGNÓSTICO ===", "Debug", MessageBoxButton.OK, MessageBoxImage.Information);

                var produtos = await ExecuteDbAsync(async context =>
                    await context.Products.Take(5).ToListAsync());

                MessageBox.Show($"Produtos encontrados: {produtos.Count}\n" +
                               $"Exemplos: {string.Join(", ", produtos.Select(p => p.Name))}",
                               "Debug - Produtos", MessageBoxButton.OK, MessageBoxImage.Information);

                var movimentos = await ExecuteDbAsync(async context =>
                    await context.InventoryMovements.Take(5).ToListAsync());

                MessageBox.Show($"Movimentos encontrados: {movimentos.Count}\n" +
                               $"Tem NewStock?: {(movimentos.Any(m => m.NewStock > 0) ? "SIM" : "NÃO")}",
                               "Debug - Movimentos", MessageBoxButton.OK, MessageBoxImage.Information);

                if (produtos.Any())
                {
                    var primeiroProduto = produtos.First();
                    var hoje = DateTime.Today;
                    var ontem = hoje.AddDays(-1);

                    var stockHoje = await GetProductStockAtDate(primeiroProduto.Id, hoje);
                    var stockOntem = await GetProductStockAtDate(primeiroProduto.Id, ontem);

                    MessageBox.Show($"Produto: {primeiroProduto.Name}\n" +
                                   $"Stock Hoje: {stockHoje}\n" +
                                   $"Stock Ontem: {stockOntem}\n" +
                                   $"Variação: {stockHoje - stockOntem}",
                                   "Debug - Cálculo", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                MessageBox.Show($"Data Inicial: {DpHistoryFrom.SelectedDate}\n" +
                               $"Data Final: {DpHistoryTo.SelectedDate}\n" +
                               $"Produto selecionado: {CmbHistoryProduct.SelectedValue}",
                               "Debug - Filtros", MessageBoxButton.OK, MessageBoxImage.Information);

                MessageBox.Show("=== DIAGNÓSTICO COMPLETO ===", "Debug", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ERRO NO DIAGNÓSTICO:\n{ex.Message}\n\n{ex.StackTrace}",
                               "ERRO", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnForceRecalculate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                ProductStockHistoryCache.ClearCache();

                await Task.Run(async () =>
                {
                    // Aqui você pode adicionar cálculo em lote se tiver muitos produtos
                });

                ShowSuccessMessage("Recálculo completo concluído!");
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Erro no recálculo: {ex.Message}");
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void ShowProductChart(ProductStockHistory history)
        {
            var chartWindow = new Window
            {
                Title = $"📈 Gráfico: {history.ProductName}",
                Width = 700,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = System.Windows.Media.Brushes.White
            };

            var grid = new Grid();

            var headerPanel = new StackPanel
            {
                Background = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var title = new TextBlock
            {
                Text = $"📈 {history.ProductName}",
                FontWeight = FontWeights.Bold,
                FontSize = 18,
                Foreground = System.Windows.Media.Brushes.DarkSlateGray,
                Margin = new Thickness(0, 0, 0, 8)
            };

            var period = new TextBlock
            {
                Text = $"Período: {DpHistoryFrom.SelectedDate:dd/MM/yyyy} → {DpHistoryTo.SelectedDate:dd/MM/yyyy}",
                FontSize = 13,
                Foreground = System.Windows.Media.Brushes.Gray
            };

            headerPanel.Children.Add(title);
            headerPanel.Children.Add(period);

            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                Margin = new Thickness(20),
                FontSize = 12,
                MaxHeight = 300
            };

            dataGrid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
            {
                Header = "Data",
                Binding = new Binding("Date") { StringFormat = "dd/MM/yyyy" },
                Width = 120
            });

            dataGrid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
            {
                Header = "Stock",
                Binding = new Binding("Stock") { StringFormat = "N0" },
                Width = 100
            });

            dataGrid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
            {
                Header = "Variação",
                Binding = new Binding("Stock")
                {
                    Converter = new StockVariationConverter(),
                    ConverterParameter = history.DailyData
                },
                Width = 100
            });

            dataGrid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
            {
                Header = "Vendas",
                Binding = new Binding("Sales") { StringFormat = "N0" },
                Width = 100
            });

            dataGrid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
            {
                Header = "Compras",
                Binding = new Binding("Purchases") { StringFormat = "N0" },
                Width = 100
            });

            var dailyData = history.DailyData?.OrderBy(d => d.Date).ToList() ?? new List<DailyStockData>();
            dataGrid.ItemsSource = dailyData;

            var stackPanel = new StackPanel();
            stackPanel.Children.Add(headerPanel);
            stackPanel.Children.Add(dataGrid);

            chartWindow.Content = stackPanel;
            chartWindow.ShowDialog();
        }

        // ========== CLASSES PARA CACHE DE STOCK ==========

        public class DailyStockCache
        {
            public int ProductId { get; set; }
            public DateTime Date { get; set; }
            public decimal Stock { get; set; }
            public DateTime LastUpdated { get; set; }
        }

        public class ProductStockHistoryCache
        {
            private static Dictionary<string, DailyStockCache> _cache = new();
            private static DateTime _lastCacheUpdate = DateTime.MinValue;
            private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

            public static void ClearCache()
            {
                _cache.Clear();
                _lastCacheUpdate = DateTime.MinValue;
            }

            public static bool IsCacheValid()
            {
                return (DateTime.Now - _lastCacheUpdate) < CacheDuration;
            }

            public static void UpdateCacheTime()
            {
                _lastCacheUpdate = DateTime.Now;
            }

            public static string GetCacheKey(int productId, DateTime date)
            {
                return $"{productId}_{date:yyyyMMdd}";
            }

            public static bool TryGetFromCache(int productId, DateTime date, out decimal stock)
            {
                var key = GetCacheKey(productId, date);
                if (_cache.TryGetValue(key, out var cacheItem))
                {
                    stock = cacheItem.Stock;
                    return true;
                }

                stock = 0;
                return false;
            }

            public static void AddToCache(int productId, DateTime date, decimal stock)
            {
                var key = GetCacheKey(productId, date);
                _cache[key] = new DailyStockCache
                {
                    ProductId = productId,
                    Date = date,
                    Stock = stock,
                    LastUpdated = DateTime.Now
                };

                CleanOldCache();
            }

            private static void CleanOldCache()
            {
                var cutoffDate = DateTime.Now.AddDays(-7);
                var oldKeys = _cache.Where(kvp => kvp.Value.LastUpdated < cutoffDate)
                                   .Select(kvp => kvp.Key)
                                   .ToList();

                foreach (var key in oldKeys)
                {
                    _cache.Remove(key);
                }
            }

            public static int GetCacheCount()
            {
                return _cache.Count;
            }
        }

        public class StockVariationConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is decimal currentStock && parameter is List<DailyStockData> dailyData)
                {
                    return "+0";
                }
                return "0";
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }

        private async Task ShowComparisonResults(DateTime date1, DateTime date2, int? productId = null)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                DpHistoryFrom.SelectedDate = date1;
                DpHistoryTo.SelectedDate = date2;

                if (productId.HasValue)
                {
                    CmbHistoryProduct.SelectedValue = productId;
                }
                else
                {
                    CmbHistoryProduct.SelectedIndex = 0;
                }

                Mouse.OverrideCursor = null;
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                ShowErrorMessage($"Erro ao comparar datas: {ex.Message}");
            }
        }

        private async Task AnalyzeHistoryAsync()
        {
            try
            {
                if (!DpHistoryFrom.SelectedDate.HasValue || !DpHistoryTo.SelectedDate.HasValue)
                {
                    ShowErrorMessage("Selecione as datas inicial e final.");
                    return;
                }

                DateTime startDate = DpHistoryFrom.SelectedDate.Value;
                DateTime endDate = DpHistoryTo.SelectedDate.Value;

                if (startDate > endDate)
                {
                    ShowErrorMessage("A data inicial deve ser anterior à data final.");
                    return;
                }

                if ((endDate - startDate).TotalDays > 365)
                {
                    ShowErrorMessage("O período máximo de análise é de 1 ano.");
                    return;
                }

                Mouse.OverrideCursor = Cursors.Wait;

                int cacheCount = ProductStockHistoryCache.GetCacheCount();
                bool isCacheValid = ProductStockHistoryCache.IsCacheValid();

                string cacheStatus = isCacheValid ?
                    $"✅ Cache válido ({cacheCount} itens)" :
                    "🔄 Cache expirado, recalculando...";

                int? productId = null;
                if (CmbHistoryProduct.SelectedValue is int selectedProductId && selectedProductId > 0)
                {
                    productId = selectedProductId;
                }

                int? categoryId = null;
                if (CmbHistoryCategory.SelectedValue is int selectedCategoryId && selectedCategoryId > 0)
                {
                    categoryId = selectedCategoryId;
                }

                bool showOnlyChanges = ChkShowOnlyChanges.IsChecked ?? true;
                bool includeZeroStock = ChkIncludeZeroStock.IsChecked ?? false;

                var results = await AnalyzeStockHistory(startDate, endDate, productId, categoryId, includeZeroStock);

                if (showOnlyChanges)
                {
                    results = results.Where(r => r.HistoricalData.Variation != 0).ToList();
                }

                UpdateHistoryResults(results);
                UpdateHistoryStatistics(results);
                UpdateHistoryPeriodText();

                Mouse.OverrideCursor = null;

                ShowSuccessMessage($"Análise concluída: {results.Count} produtos analisados. {cacheStatus}");
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                ShowErrorMessage($"Erro ao analisar histórico: {ex.Message}");
            }
        }

        private void BtnShowChart_Click(object sender, RoutedEventArgs e)
        {
            var results = DgStockHistory.ItemsSource as List<StockComparisonResult>;
            if (results == null || results.Count == 0)
            {
                ShowErrorMessage("Execute uma análise primeiro para ver o gráfico.");
                return;
            }

            ShowStockChart(results);
        }

        private void ShowStockChart(List<StockComparisonResult> results)
        {
            var chartWindow = new Window
            {
                Title = "📈 Gráfico de Variações de Stock",
                Width = 800,
                Height = 600,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = System.Windows.Media.Brushes.White
            };

            var grid = new Grid();

            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                Margin = new Thickness(10),
                FontSize = 11
            };

            dataGrid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
            {
                Header = "Produto",
                Binding = new Binding("HistoricalData.ProductName"),
                Width = new DataGridLength(2, DataGridLengthUnitType.Star)
            });

            dataGrid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
            {
                Header = "Stock Inicial",
                Binding = new Binding("HistoricalData.StockAtStart") { StringFormat = "N0" },
                Width = 100
            });

            dataGrid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
            {
                Header = "Stock Final",
                Binding = new Binding("HistoricalData.StockAtEnd") { StringFormat = "N0" },
                Width = 100
            });

            dataGrid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
            {
                Header = "Variação",
                Binding = new Binding("HistoricalData.Variation") { StringFormat = "N0" },
                Width = 100
            });

            dataGrid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
            {
                Header = "Variação %",
                Binding = new Binding("HistoricalData.VariationPercentage") { StringFormat = "P1" },
                Width = 100
            });

            dataGrid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
            {
                Header = "Tendência",
                Binding = new Binding("Trend"),
                Width = 100
            });

            dataGrid.ItemsSource = results;

            chartWindow.Content = dataGrid;
            chartWindow.ShowDialog();
        }

        private void DgStockHistory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Opcional: implementar detalhes rápidos na seleção
        }

        private async void BtnPrintBarcodes_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedProducts = (DgBarcodeProducts.ItemsSource as List<BarcodeProduct>)?
                    .Where(p => p.IsSelected)
                    .ToList();

                if (selectedProducts == null || selectedProducts.Count == 0)
                {
                    ShowErrorMessage("Selecione pelo menos um produto para imprimir.");
                    return;
                }

                if (!int.TryParse(TxtBarcodeCopies.Text, out int copies) || copies < 1 || copies > 100)
                {
                    ShowErrorMessage("Número de cópias inválido (1-100).");
                    TxtBarcodeCopies.Focus();
                    return;
                }

                var config = await ExecuteDbAsync(async context =>
                    await context.RestaurantConfigs.FirstOrDefaultAsync());

                if (string.IsNullOrEmpty(config?.PrinterName))
                {
                    ShowErrorMessage("Configure uma impressora nas configurações do restaurante.");
                    return;
                }

                Mouse.OverrideCursor = Cursors.Wait;

                var printDialog = new PrintDialog();
                printDialog.PrintQueue = new System.Printing.PrintQueue(new System.Printing.PrintServer(), config.PrinterName);

                var printDocument = new FlowDocument
                {
                    PageWidth = printDialog.PrintableAreaWidth,
                    PageHeight = printDialog.PrintableAreaHeight,
                    PagePadding = new Thickness(20),
                    ColumnWidth = printDialog.PrintableAreaWidth
                };

                var stackPanel = new StackPanel();

                bool includeDividers = ChkIncludeDividers.IsChecked ?? true;
                int currentRow = 0;

                foreach (var product in selectedProducts)
                {
                    for (int i = 0; i < copies; i++)
                    {
                        var barcodeContainer = new Border
                        {
                            BorderBrush = System.Windows.Media.Brushes.LightGray,
                            BorderThickness = new Thickness(1),
                            Padding = new Thickness(10),
                            Margin = new Thickness(0, 0, 0, 10),
                            Background = System.Windows.Media.Brushes.White
                        };

                        var barcodeStack = new StackPanel
                        {
                            Width = 200,
                            HorizontalAlignment = HorizontalAlignment.Center
                        };

                        var nameText = new TextBlock
                        {
                            Text = product.Name.Length > 30 ? product.Name.Substring(0, 27) + "..." : product.Name,
                            FontWeight = FontWeights.Bold,
                            FontSize = 10,
                            TextAlignment = TextAlignment.Center,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 0, 0, 4)
                        };

                        var barcodeImage = GenerateBarcodeImage(
                            product.Barcode ?? GenerateBarcodeValue(product.Code, product.Price),
                            product.Name,
                            product.Price,
                            product.Code
                        );

                        var imageControl = new Image
                        {
                            Source = barcodeImage,
                            Stretch = System.Windows.Media.Stretch.Uniform,
                            Height = 60,
                            Width = 200,
                            Margin = new Thickness(0, 4, 0, 4)
                        };

                        var infoStack = new StackPanel();

                        var priceText = new TextBlock
                        {
                            Text = $"Preço: {product.Price:N2} MTn",
                            FontSize = 9,
                            TextAlignment = TextAlignment.Center
                        };

                        var codeText = new TextBlock
                        {
                            Text = $"Código: {product.Code}",
                            FontSize = 8,
                            Foreground = System.Windows.Media.Brushes.Gray,
                            TextAlignment = TextAlignment.Center
                        };

                        var barcodeText = new TextBlock
                        {
                            Text = product.Barcode ?? "Sem código",
                            FontSize = 7,
                            Foreground = System.Windows.Media.Brushes.DarkGray,
                            TextAlignment = TextAlignment.Center
                        };

                        infoStack.Children.Add(priceText);
                        infoStack.Children.Add(codeText);
                        infoStack.Children.Add(barcodeText);

                        barcodeStack.Children.Add(nameText);
                        barcodeStack.Children.Add(imageControl);
                        barcodeStack.Children.Add(infoStack);

                        barcodeContainer.Child = barcodeStack;
                        stackPanel.Children.Add(barcodeContainer);

                        currentRow++;

                        if (includeDividers && i < copies - 1)
                        {
                            var divider = new Rectangle
                            {
                                Height = 1,
                                Fill = System.Windows.Media.Brushes.LightGray,
                                Margin = new Thickness(0, 5, 0, 5)
                            };
                            stackPanel.Children.Add(divider);
                        }
                    }

                    if (includeDividers && product != selectedProducts.Last())
                    {
                        var productDivider = new Rectangle
                        {
                            Height = 2,
                            Fill = System.Windows.Media.Brushes.Gray,
                            Margin = new Thickness(0, 10, 0, 10)
                        };
                        stackPanel.Children.Add(productDivider);
                    }
                }

                printDocument.Blocks.Add(new BlockUIContainer(stackPanel));

                printDialog.PrintDocument(((IDocumentPaginatorSource)printDocument).DocumentPaginator,
                    $"Códigos de Barras - {DateTime.Now:dd/MM/yyyy HH:mm}");

                Mouse.OverrideCursor = null;

                ShowSuccessMessage($"{selectedProducts.Count} produtos impressos ({copies} cópias cada).");
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                ShowErrorMessage($"Erro ao imprimir códigos de barras: {ex.Message}");
            }
        }

        private void BtnRefreshPreview_Click(object sender, RoutedEventArgs e)
        {
            UpdateBarcodePreview();
        }

        private void DgBarcodeProducts_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateBarcodePreview();
        }

        // ========== EVENTOS DE COMPRAS ==========
        private void DgPurchases_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgPurchases.SelectedItem is PurchaseOrder purchase)
            {
                // Implementar visualização detalhada se necessário
            }
        }

        private void BtnViewPurchase_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is PurchaseOrder purchase)
            {
                var message = $"Compra: {purchase.PurchaseNumber}\n" +
                             $"Fornecedor: {purchase.Supplier.Name}\n" +
                             $"Data: {purchase.CreatedAt:dd/MM/yyyy}\n" +
                             $"Status: {purchase.Status}\n" +
                             $"Total: {purchase.TotalAmount:N2} MTn\n" +
                             $"Itens: {purchase.ItemCount}\n" +
                             $"Observações: {purchase.Notes}";

                MessageBox.Show(message, "Detalhes da Compra", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ========== CLASSES PARA ANÁLISE HISTÓRICA ==========

        public class ProductStockHistory
        {
            public int ProductId { get; set; }
            public string ProductName { get; set; } = string.Empty;
            public string ProductCode { get; set; } = string.Empty;
            public string CategoryName { get; set; } = string.Empty;

            public decimal StockAtStart { get; set; }
            public decimal StockAtEnd { get; set; }
            public decimal Variation { get; set; }
            public decimal VariationPercentage { get; set; }

            public List<DailyStockData> DailyData { get; set; } = new List<DailyStockData>();
        }

        public class DailyStockData
        {
            public DateTime Date { get; set; }
            public decimal Stock { get; set; }
            public decimal Sales { get; set; }
            public decimal Purchases { get; set; }
            public decimal Adjustments { get; set; }
        }

        public class StockComparisonResult
        {
            public ProductStockHistory HistoricalData { get; set; } = new ProductStockHistory();
            public string Trend { get; set; } = string.Empty;
            public string TrendIcon { get; set; } = string.Empty;
            public System.Windows.Media.Brush TrendColor { get; set; } = System.Windows.Media.Brushes.Gray;
        }

        // ========== CONVERTERS ==========
        public class BoolToStatusTextConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return value is bool boolValue && boolValue ? "Ativo" : "Inativo";
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }

        public class NullToVisibilityConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return string.IsNullOrEmpty(value?.ToString()) ? Visibility.Collapsed : Visibility.Visible;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }

        public class StatusToVisibilityConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return value?.ToString() == "Pendente" ? Visibility.Visible : Visibility.Collapsed;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }

        public class MovementTypeToSignConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return value?.ToString() == "Entrada" ? "+" : "-";
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }
    }
    // Classe para exibição no DataGrid (não é um model do banco)
    public class PurchaseDisplayItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string Icon { get; set; } = "";
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Subtotal { get; set; }
        public int OriginalId { get; set; } // ID do produto ou ingrediente
        public bool IsProduct { get; set; }

        public string DisplayName => $"{Icon} {Name}";
    }
    public class PurchaseItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string Icon { get; set; } = "";
        public bool IsProduct { get; set; }

        public string DisplayName => $"{Icon} {Name} ({Type})";
    }
    public class BarcodeProduct
    {
        public int Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public decimal Price { get; set; }
        public decimal Stock { get; set; }
        public string? Barcode { get; set; }
        public bool IsSelected { get; set; }
        public BitmapImage? BarcodeImage { get; set; }
        public bool HasBarcode { get; set; }
    }
}