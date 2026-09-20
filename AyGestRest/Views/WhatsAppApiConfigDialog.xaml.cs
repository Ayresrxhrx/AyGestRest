// WhatsAppApiConfigDialog.xaml.cs
using System.Windows;

namespace AyGestRest.Views
{
    public partial class WhatsAppApiConfigDialog : Window
    {
        public string AccessToken { get; private set; }
        public string PhoneNumberId { get; private set; }

        public WhatsAppApiConfigDialog()
        {
            InitializeComponent();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            AccessToken = txtAccessToken.Text;
            PhoneNumberId = txtPhoneNumberId.Text;

            if (string.IsNullOrWhiteSpace(AccessToken) || string.IsNullOrWhiteSpace(PhoneNumberId))
            {
                MessageBox.Show("Preencha todos os campos obrigatórios.", "Validação",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            string helpText = "Como obter as credenciais:\n\n" +
                             "1. Acesse: developers.facebook.com\n" +
                             "2. Crie um aplicativo Business\n" +
                             "3. Adicione o produto 'WhatsApp'\n" +
                             "4. Vá para 'API Setup' para obter:\n" +
                             "   - Token de acesso temporário\n" +
                             "   - ID do número de telefone\n\n" +
                             "O token permanente será gerado após aprovação.";

            MessageBox.Show(helpText, "Ajuda - Configuração da API",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}