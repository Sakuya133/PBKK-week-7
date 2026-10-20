using System.Globalization;
using System.Text;
using System.Windows;
using PosBackend;

namespace WpfApp1;

public partial class Penjualan : Window
{
    private readonly ITransactionService _transactions;

    private int _page = 1;
    private const int PageSize = 50;

    public Penjualan(ITransactionService transactions)
    {
        InitializeComponent();

        _transactions = transactions;

        Loaded += async (_, _) => await LoadTransactionsAsync();
    }

    private async Task LoadTransactionsAsync()
    {
        StatusText.Text = "Memuat transaksi demo...";
        PreviousButton.IsEnabled = false;
        NextButton.IsEnabled = false;
        DetailButton.IsEnabled = false;

        try
        {
            var result = await _transactions.GetTransactionsPageAsync(
                _page, PageSize);

            if (!result.Success || result.Value is null)
            {
                StatusText.Text = result.Message;
                return;
            }

            TransactionsGrid.ItemsSource = result.Value.Items;

            PageInfo.Text = $"Halaman {result.Value.Page}";
            PreviousButton.IsEnabled = _page > 1;
            NextButton.IsEnabled = result.Value.HasMore;

            StatusText.Text = result.Value.Items.Count == 0
                ? "Belum ada transaksi pada halaman ini."
                : $"DEMO: Menampilkan {result.Value.Items.Count} transaksi contoh.";

            if (result.Value.Items.Count > 0)
                TransactionsGrid.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Gagal memuat transaksi: {ex.Message}";
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadTransactionsAsync();
    }

    private async void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (_page <= 1)
            return;

        _page--;
        await LoadTransactionsAsync();
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        _page++;
        await LoadTransactionsAsync();
    }

    private async void Detail_Click(object sender, RoutedEventArgs e)
    {
        if (TransactionsGrid.SelectedItem is not TransactionSummaryDto selected)
        {
            MessageBox.Show(
                "Pilih transaksi terlebih dahulu.",
                "Detail Transaksi");
            return;
        }

        DetailButton.IsEnabled = false;
        StatusText.Text = "Memuat detail transaksi demo...";

        try
        {
            var result = await _transactions.GetDetailAsync(selected.Id);

            if (!result.Success || result.Value is null)
            {
                StatusText.Text = result.Message;
                return;
            }

            var transaction = result.Value;
            var culture = CultureInfo.GetCultureInfo("id-ID");
            var details = new StringBuilder();

            details.AppendLine("DATA DEMO â€” BUKAN TRANSAKSI ASLI");
            details.AppendLine();
            details.AppendLine($"No. Transaksi: {transaction.Number}");
            details.AppendLine(
                $"Tanggal: {transaction.CreatedAt:dd/MM/yyyy HH:mm}");
            details.AppendLine($"Kasir: {transaction.CashierName}");
            details.AppendLine(
                $"Pelanggan: {transaction.CustomerName ?? "Umum"}");
            details.AppendLine($"Pembayaran: {transaction.PaymentMethod}");
            details.AppendLine();
            details.AppendLine("PRODUK");

            foreach (var item in transaction.Items)
            {
                details.AppendLine();
                details.AppendLine(item.ProductName);
                details.AppendLine($"  Harga satuan: {item.UnitPrice.ToString("C0", culture)}");
                details.AppendLine($"  Jumlah: {item.Quantity}");
                details.AppendLine($"  Subtotal: {item.Subtotal.ToString("C0", culture)}");
            }

            details.AppendLine();
            details.AppendLine(
                $"TOTAL: {transaction.Total.ToString("C0", culture)}");

            MessageBox.Show(
                details.ToString(),
                "Detail Transaksi",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            StatusText.Text = "Detail demo berhasil ditampilkan.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Gagal memuat detail: {ex.Message}";
        }
        finally
        {
            DetailButton.IsEnabled =
                TransactionsGrid.SelectedItem is TransactionSummaryDto;
        }
    }

    private void TransactionsGrid_SelectionChanged(
        object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        DetailButton.IsEnabled =
            TransactionsGrid.SelectedItem is TransactionSummaryDto;
    }

    private void Kategori_Click(object sender, RoutedEventArgs e)
    {
        NavigationHelper.OpenKategori(this);
    }
    private void Produk_Click(object sender, RoutedEventArgs e)
    {
        NavigationHelper.OpenProduk(this);
    }

}
