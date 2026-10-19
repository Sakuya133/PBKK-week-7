using System.Windows;

namespace WpfApp1
{
    public partial class edit_kategori : Window
    {
        public edit_kategori()
        {
            InitializeComponent();
        }

        private void BtnGantiLogo_Click(object sender, RoutedEventArgs e)
        {
            // Logika ganti logo
        }

        private void BtnBatal_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }

        private void BtnSimpan_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
        }
    }
}