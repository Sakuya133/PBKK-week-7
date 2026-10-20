
using System.Windows;

namespace WpfApp1;

public static class NavigationHelper
{
    public static void OpenKategori(Window current)
    {
        new kategori_admin().Show();
        current.Close();
    }

    public static void OpenProduk(Window current)
    {
        new produk_admin().Show();
        current.Close();
    }

    public static void OpenPenjualan(Window current)
    {
        new Penjualan(new DemoTransactionService()).Show();
        current.Close();
    }
}
