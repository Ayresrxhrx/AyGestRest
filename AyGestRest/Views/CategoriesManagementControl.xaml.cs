using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AyGestRest.Views
{
    public partial class CategoriesManagementControl : UserControl
    {
        private readonly AyGestRestContext _context;
        private ProductCategory _selectedCategory;

        public CategoriesManagementControl()
        {
            InitializeComponent();
            _context = new AyGestRestContext();
            LoadCategories();

            // Focar no campo de nome ao carregar
            Dispatcher.BeginInvoke(() =>
            {
                CategoryNameTextBox.Focus();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void LoadCategories(string filter = "")
        {
            try
            {
                var query = _context.ProductCategories.AsNoTracking();

                if (!string.IsNullOrWhiteSpace(filter))
                {
                    query = query.Where(c => c.Nome.Contains(filter));
                }

                var list = query.OrderBy(c => c.Nome).ToList();
                CategoriesDataGrid.ItemsSource = list;

                // Atualiza contador
                CountTextBlock.Text = $"{list.Count} categoria{(list.Count == 1 ? "" : "s")} encontrada{(list.Count == 1 ? "" : "s")}";
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Erro ao carregar categorias: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Adicionar nova categoria
        private void AddCategory_Click(object sender, RoutedEventArgs e)
        {
            string name = CategoryNameTextBox.Text.Trim();

            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Preencha o nome da categoria!", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                CategoryNameTextBox.Focus();
                return;
            }

            try
            {
                // Verificar se já existe
                if (_context.ProductCategories.Any(c => c.Nome.ToLower() == name.ToLower()))
                {
                    MessageBox.Show("Já existe uma categoria com esse nome!", "Erro",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    CategoryNameTextBox.SelectAll();
                    CategoryNameTextBox.Focus();
                    return;
                }

                var newCategory = new ProductCategory { Nome = name };
                _context.ProductCategories.Add(newCategory);
                _context.SaveChanges();

                MessageBox.Show("Categoria adicionada com sucesso!", "Sucesso",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                ClearFields();
                LoadCategories(SearchTextBox.Text);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Erro ao adicionar categoria: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Atualizar categoria selecionada
        private void UpdateCategory_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCategory == null)
            {
                MessageBox.Show("Selecione uma categoria para atualizar.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string newName = CategoryNameTextBox.Text.Trim();

            if (string.IsNullOrEmpty(newName))
            {
                MessageBox.Show("O nome da categoria não pode ficar vazio.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                CategoryNameTextBox.Focus();
                return;
            }

            try
            {
                // Verificar se já existe outra categoria com o mesmo nome
                if (_context.ProductCategories.Any(c =>
                    c.Nome.ToLower() == newName.ToLower() && c.Id != _selectedCategory.Id))
                {
                    MessageBox.Show("Já existe outra categoria com esse nome!", "Erro",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    CategoryNameTextBox.SelectAll();
                    CategoryNameTextBox.Focus();
                    return;
                }

                var category = _context.ProductCategories.Find(_selectedCategory.Id);
                if (category != null)
                {
                    // Verificar se existem produtos nesta categoria
                    bool hasProducts = _context.Products.Any(p => p.CategoryId == category.Id);

                    category.Nome = newName;
                    _context.SaveChanges();

                    string message = "Categoria atualizada com sucesso!";
                    if (hasProducts)
                    {
                        message += "\n\nOs produtos associados a esta categoria mantêm-se ligados a ela.";
                    }

                    MessageBox.Show(message, "Sucesso",
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    ClearFields();
                    LoadCategories(SearchTextBox.Text);
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Erro ao atualizar categoria: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Quando seleciona uma linha no grid
        private void CategoriesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CategoriesDataGrid.SelectedItem is ProductCategory selected)
            {
                _selectedCategory = selected;
                CategoryNameTextBox.Text = selected.Nome;
                CategoryNameTextBox.SelectAll();
                CategoryNameTextBox.Focus();

                UpdateButton.IsEnabled = true;
                AddButton.IsEnabled = false;
            }
            else
            {
                _selectedCategory = null;
            }
        }

        // Botão Novo (limpa os campos e prepara para adicionar)
        private void NewCategory_Click(object sender, RoutedEventArgs e)
        {
            ClearFields();
        }

        private void ClearFields()
        {
            CategoryNameTextBox.Text = string.Empty;
            _selectedCategory = null;
            CategoriesDataGrid.SelectedItem = null;
            UpdateButton.IsEnabled = false;
            AddButton.IsEnabled = true;

            Dispatcher.BeginInvoke(() =>
            {
                CategoryNameTextBox.Focus();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        // Botão Editar (foca na categoria selecionada)
        private void EditSelected_Click(object sender, RoutedEventArgs e)
        {
            if (CategoriesDataGrid.SelectedItem == null)
            {
                MessageBox.Show("Selecione uma categoria para editar.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);

                // Tentar selecionar a primeira linha se houver dados
                if (CategoriesDataGrid.Items.Count > 0)
                {
                    CategoriesDataGrid.SelectedIndex = 0;
                }
            }
            else
            {
                CategoryNameTextBox.SelectAll();
                CategoryNameTextBox.Focus();
            }
        }

        // Botão Atualizar (Refresh)
        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Text = string.Empty;
            LoadCategories();
            ClearFields();
        }

        // Pesquisa em tempo real com delay para melhor performance
        private System.Windows.Threading.DispatcherTimer _searchTimer;
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Implementação com timer para evitar busca a cada tecla digitada
            if (_searchTimer == null)
            {
                _searchTimer = new System.Windows.Threading.DispatcherTimer();
                _searchTimer.Interval = TimeSpan.FromMilliseconds(300);
                _searchTimer.Tick += (s, args) =>
                {
                    _searchTimer.Stop();
                    LoadCategories(SearchTextBox.Text.Trim());
                };
            }

            _searchTimer.Stop();
            _searchTimer.Start();
        }

   


    }
}