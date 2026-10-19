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
    /// <summary>
    /// Interaction logic for tambah_produk.xaml
    /// </summary>
    public partial class tambah_kategori : Window
    {
        public tambah_kategori()
        {
            InitializeComponent();
        }

        private void BtnTambahLogo_Click(object sender, RoutedEventArgs e)
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
