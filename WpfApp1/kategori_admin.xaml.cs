using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WpfApp1
{ 
    public partial class kategori_admin : Window
    {
        public kategori_admin()
        {
            InitializeComponent();
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void Kategori_Click(object sender, RoutedEventArgs e)
        {
            // do nothing.
        }
        private void Produk_Click(object sender, RoutedEventArgs e)
        {
            NavigationHelper.OpenProduk(this);
        }

        private void Penjualan_Click(object sender, RoutedEventArgs e)
        {
            NavigationHelper.OpenPenjualan(this);
        }
    }
}