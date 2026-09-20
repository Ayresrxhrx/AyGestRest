using AyGestRest.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace AyGestRest.Views
{
    public partial class IngredientPopup : Window
    {
        public Ingredient SelectedIngredient { get; private set; }
        public int Quantity { get; private set; }

        public IngredientPopup(List<Ingredient> ingredients, Ingredient? selected = null, int quantity = 1)
        {
            InitializeComponent();

            cmbIngredients.ItemsSource = ingredients;
            if (selected != null)
            {
                cmbIngredients.SelectedItem = ingredients.FirstOrDefault(i => i.Id == selected.Id);
                txtQuantity.Text = quantity.ToString();
            }
            else
            {
                txtQuantity.Text = "1";
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (cmbIngredients.SelectedItem is Ingredient ingredient &&
                int.TryParse(txtQuantity.Text, out int qty) && qty > 0)
            {
                SelectedIngredient = ingredient;
                Quantity = qty;
                this.DialogResult = true;
                this.Close();
            }
            else
            {
                MessageBox.Show("Selecione um ingrediente válido e uma quantidade maior que 0.");
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
