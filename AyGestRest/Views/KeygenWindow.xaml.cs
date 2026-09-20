using System;
using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace AyGest
{
    public partial class AtivacaoKeygenWindow : Window
    {
        public AtivacaoKeygenWindow()
        {
            InitializeComponent();
        }

        private void btnGerar_Click(object sender, RoutedEventArgs e)
        {
            string hwid = txtHwid.Text.Trim();
            if (string.IsNullOrEmpty(hwid))
            {
                MessageBox.Show("Coloca o HWID primeiro.", "Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string key = GerarKey(hwid);
            txtKey.Text = key;
        }

        private void btnCopiar_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(txtKey.Text))
            {
                Clipboard.SetText(txtKey.Text);
                MessageBox.Show("Key copiada!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private string GerarKey(string hwid)
        {
            string segredo = "ChaveSecreta123"; // mesmo segredo que está no AtivacaoHelper

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(hwid + segredo));

                // aqui é igual ao AtivacaoHelper: Base64 + substring 16 + upper
                string key = Convert.ToBase64String(bytes).Substring(0, 16).ToUpper();
                return key;
            }
        }
    }
}
