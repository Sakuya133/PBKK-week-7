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
    public partial class tambah_produk : Window
    {
        public tambah_produk()
        {
            InitializeComponent();
        }

        private void BtnTambahFoto_Click(object sender, RoutedEventArgs e)
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
