using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace AyGestRest.Views
{
    public partial class EmployeesViewControl : UserControl
    {
        private readonly AyGestRestContext _context = new();
        private Product? _currentProduct;
        private ObservableCollection<ProductIngredient> _currentRecipe = new();

        public EmployeesViewControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadCategories();
            LoadAllIngredients();
            RefreshProducts();
        }

        private void LoadCategories()
        {
            // Obter categorias ativas
            var categories = _context.ProductCategories
                .Where(c => c.Ativo)
                .OrderBy(c => c.Nome)
                .ToList();

            // Adiciona opção "Todas" no início da lista
            categories.Insert(0, new ProductCategory { Id = 0, Nome = "Todas as categorias" });

            // Liga a lista ao ComboBox
            CmbFilterCategory.ItemsSource = categories;
            CmbFilterCategory.SelectedIndex = 0;
        }

        private void LoadAllIngredients()
        {
            var ingredients = _context.Ingredients.OrderBy(i => i.Name).ToList();
            DgIngredients.ItemsSource = ingredients;
        }

        private void RefreshProducts()
        {
            var query = _context.Products.Include(p => p.Category).AsQueryable();

            // Filtro por categoria
            if (CmbFilterCategory.SelectedValue is int catId && catId > 0)
            {
                query = query.Where(p => p.CategoryId == catId);
            }

            // Busca por texto
            if (!string.IsNullOrWhiteSpace(TxtSearch.Text))
            {
                string search = TxtSearch.Text.ToLower();
                query = query.Where(p => p.Name.ToLower().Contains(search) ||
                                        (p.Code ?? "").ToLower().Contains(search));
            }

            DgProducts.ItemsSource = query.OrderBy(p => p.Name).ToList();
        }

        private void ClearProductDetails()
        {
            _currentProduct = null;
            _currentRecipe.Clear();
            PanelProductDetails.Visibility = Visibility.Collapsed;
        }

        private void FillProductDetails(Product product)
        {
            _currentProduct = product;

            TxtNameReadOnly.Text = product.Name;
            TxtCodeReadOnly.Text = product.Code ?? "";
            TxtCategoryReadOnly.Text = product.Category?.Nome ?? "";
            TxtSalePriceReadOnly.Text = product.Price.ToString("N2", CultureInfo.GetCultureInfo("pt-MZ")) + " MTn";
            TxtStockReadOnly.Text = product.Stock.ToString("N2");
            TxtDescriptionReadOnly.Text = product.Description ?? "(Sem descrição)";
            TxtStatusReadOnly.Text = product.Active ? "Produto Ativo" : "Produto Inativo";
            TxtTypeReadOnly.Text = product.IsComposite ? "Produto Composto" : "Produto Simples";

            // Imagem
            if (product.Image != null && product.Image.Length > 0)
            {
                using var ms = new MemoryStream(product.Image);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = ms;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                ImgProductReadOnly.Source = bitmap;
            }
            else
            {
                ImgProductReadOnly.Source = null;
            }

            // Receita (se composto)
            if (product.IsComposite)
            {
                _context.Entry(product).Collection(p => p.ProductIngredients).Load();
                _currentRecipe.Clear();
                foreach (var pi in product.ProductIngredients)
                {
                    _context.Entry(pi).Reference(r => r.Ingredient).Load();
                    _currentRecipe.Add(pi);
                }
                DgRecipeReadOnly.ItemsSource = _currentRecipe;
                CardRecipeReadOnly.Visibility = Visibility.Visible;

                decimal totalCost = _currentRecipe.Sum(pi => pi.QuantityUsed * pi.Ingredient.Cost);
                TxtTotalRecipeCostReadOnly.Text = $"Custo Total da Receita: {totalCost:N2} MTn";
            }
            else
            {
                CardRecipeReadOnly.Visibility = Visibility.Collapsed;
            }

            PanelProductDetails.Visibility = Visibility.Visible;
        }

        // Eventos do XAML - agora existem!
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshProducts();
        }

        private void CmbFilterCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Evitar execução durante inicialização
            if (CmbFilterCategory?.SelectedItem != null)
            {
                RefreshProducts();
            }
        }

        private void DgProducts_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgProducts.SelectedItem is Product p)
                FillProductDetails(p);
            else
                ClearProductDetails();
        }
    }
}