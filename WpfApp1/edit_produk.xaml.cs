using System.Windows;

namespace WpfApp1
{
    public partial class edit_produk : Window
    {
        public edit_produk()
        {
            InitializeComponent();
        }

        private void BtnGantiFoto_Click(object sender, RoutedEventArgs e)
        {
            // Logika ganti foto
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