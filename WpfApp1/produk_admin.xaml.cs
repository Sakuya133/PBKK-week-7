using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace WpfApp1
{
    public partial class produk_admin : Window
    {
        public produk_admin()
        {
            InitializeComponent();
        }
        private void Kategori_Click(object sender, RoutedEventArgs e)
        {
            NavigationHelper.OpenKategori(this);
        }

        private void Produk_Click(object sender, RoutedEventArgs e)
        {
            // do nothing.
        }

        private void Penjualan_Click(object sender, RoutedEventArgs e)
        {
            NavigationHelper.OpenPenjualan(this);
        }
    }
}
