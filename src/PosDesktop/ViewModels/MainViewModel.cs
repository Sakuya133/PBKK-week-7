using System.Collections.ObjectModel;
using System.Globalization;
using PosBackend;
using PosClient;

namespace PosDesktop.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly PosRemoteServices _services;
    private readonly ClientConfiguration _configuration;
    private readonly List<AsyncRelayCommand> _busyCommands = [];
    private UserDto? _currentUser;
    private bool _isBusy;
    private bool _apiAvailable;
    private string _apiStatus = "Memeriksa API…";
    private string _username = "";
    private string _password = "";
    private string _errorMessage = "";
    private string _successMessage = "";
    private string _currentPage = "Dashboard";
    private string _productName = "";
    private string _productPriceInput = "";
    private string _productSearch = "";
    private bool _productActive = true;
    private int? _editingProductId;
    private CategoryDto? _selectedCategory;
    private ProductDto? _selectedProduct;
    private CustomerChoice? _selectedCustomer;
    private PaymentChoice _selectedPayment;
    private TransactionSummaryDto? _selectedTransaction;
    private TransactionDto? _transactionDetail;
    private DailySalesDto? _dailySales;
    private bool _showCheckoutConfirmation;
    private Guid? _pendingRequestId;
    private bool _isGuestOrdering;
    private bool _guestOrderComplete;
    private string _guestName = "";
    private string _guestOrderNumber = "";
    private GuestOrderDto? _selectedGuestOrder;
    private GuestOrderRequest? _pendingGuestOrderRequest;

    public MainViewModel(PosRemoteServices services, ClientConfiguration configuration)
    {
        _services = services;
        _configuration = configuration;
        _selectedPayment = PaymentChoices[0];
        LoginCommand = Command(LoginAsync);
        BeginGuestOrderingCommand = Command(BeginGuestOrderingAsync);
        ExitGuestOrderingCommand = new RelayCommand(_ => ExitGuestOrdering());
        SubmitGuestOrderCommand = Command(SubmitGuestOrderAsync);
        StartGuestOrderCommand = new RelayCommand(_ => StartAnotherGuestOrder());
        RetryApiCommand = Command(CheckApiAsync);
        LogoutCommand = Command(LogoutAsync);
        NavigateCommand = Command(NavigateAsync);
        RefreshCommand = Command(RefreshCurrentPageAsync);
        SaveProductCommand = Command(SaveProductAsync);
        NewProductCommand = new RelayCommand(_ => ResetProductEditor());
        DeactivateProductCommand = Command(DeactivateProductAsync);
        AddSelectedProductCommand = new RelayCommand(_ => AddSelectedProduct(), _ => SelectedProduct is not null);
        IncreaseQuantityCommand = new RelayCommand(o => ChangeQuantity(o as CartLineViewModel, 1));
        DecreaseQuantityCommand = new RelayCommand(o => ChangeQuantity(o as CartLineViewModel, -1));
        RemoveCartLineCommand = new RelayCommand(o => RemoveCartLine(o as CartLineViewModel));
        BeginCheckoutCommand = new RelayCommand(_ => BeginCheckout(), _ => Cart.Count > 0 && !IsBusy);
        ConfirmCheckoutCommand = Command(ConfirmCheckoutAsync);
        CancelCheckoutCommand = new RelayCommand(_ => ShowCheckoutConfirmation = false);
        LoadTransactionDetailCommand = Command(LoadTransactionDetailAsync);
        CompleteGuestOrderCommand = new AsyncRelayCommand(CompleteGuestOrderAsync,
            _ => SelectedGuestOrder is not null && !IsBusy,
            ex => ErrorMessage = $"Gagal mengonfirmasi pesanan: {ex.Message}");
        ApplyProductSearchCommand = new RelayCommand(_ => Changed(nameof(FilteredProducts)));

        _busyCommands.AddRange([LoginCommand, BeginGuestOrderingCommand, SubmitGuestOrderCommand, RetryApiCommand,
            LogoutCommand, NavigateCommand, RefreshCommand, SaveProductCommand, DeactivateProductCommand,
            ConfirmCheckoutCommand, LoadTransactionDetailCommand, CompleteGuestOrderCommand]);
        Cart.CollectionChanged += (_, _) => { Changed(nameof(CartTotalText)); Changed(nameof(CartCount)); Changed(nameof(CartIsEmpty)); BeginCheckoutCommand.RaiseCanExecuteChanged(); };
        Products.CollectionChanged += (_, _) => { Changed(nameof(FilteredProducts)); Changed(nameof(FilteredProductsEmpty)); };
        Transactions.CollectionChanged += (_, _) => Changed(nameof(TransactionsEmpty));
        PendingGuestOrders.CollectionChanged += (_, _) => Changed(nameof(PendingGuestOrdersEmpty));
    }

    public ObservableCollection<ProductDto> Products { get; } = [];
    public ObservableCollection<CategoryDto> Categories { get; } = [];
    public ObservableCollection<CustomerChoice> Customers { get; } = [];
    public ObservableCollection<CartLineViewModel> Cart { get; } = [];
    public ObservableCollection<TransactionSummaryDto> Transactions { get; } = [];
    public ObservableCollection<GuestOrderDto> PendingGuestOrders { get; } = [];
    public IReadOnlyList<PaymentChoice> PaymentChoices { get; } =
    [new(PaymentMethod.Tunai, "Tunai"), new(PaymentMethod.Qris, "QRIS")];
    public ObservableCollection<NavigationItem> NavigationItems { get; } = [];

    public AsyncRelayCommand LoginCommand { get; }
    public AsyncRelayCommand BeginGuestOrderingCommand { get; }
    public RelayCommand ExitGuestOrderingCommand { get; }
    public AsyncRelayCommand SubmitGuestOrderCommand { get; }
    public RelayCommand StartGuestOrderCommand { get; }
    public AsyncRelayCommand RetryApiCommand { get; }
    public AsyncRelayCommand LogoutCommand { get; }
    public AsyncRelayCommand NavigateCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SaveProductCommand { get; }
    public RelayCommand NewProductCommand { get; }
    public AsyncRelayCommand DeactivateProductCommand { get; }
    public RelayCommand AddSelectedProductCommand { get; }
    public RelayCommand IncreaseQuantityCommand { get; }
    public RelayCommand DecreaseQuantityCommand { get; }
    public RelayCommand RemoveCartLineCommand { get; }
    public RelayCommand BeginCheckoutCommand { get; }
    public AsyncRelayCommand ConfirmCheckoutCommand { get; }
    public RelayCommand CancelCheckoutCommand { get; }
    public AsyncRelayCommand LoadTransactionDetailCommand { get; }
    public AsyncRelayCommand CompleteGuestOrderCommand { get; }
    public RelayCommand ApplyProductSearchCommand { get; }

    public UserDto? CurrentUser { get => _currentUser; private set { if (Set(ref _currentUser, value)) { Changed(nameof(IsLoggedIn)); Changed(nameof(IsLogin)); Changed(nameof(IsAdmin)); Changed(nameof(IsCashier)); Changed(nameof(IsCustomer)); Changed(nameof(CurrentUserSummary)); } } }
    public bool IsLoggedIn => CurrentUser is not null;
    public bool IsLogin => CurrentUser is null && !IsGuestOrdering;
    public bool IsGuestOrdering { get => _isGuestOrdering; private set { if (Set(ref _isGuestOrdering, value)) { Changed(nameof(IsLogin)); Changed(nameof(IsGuestKioskPage)); Changed(nameof(IsGuestOrderComplete)); } } }
    public bool IsGuestKioskPage => IsGuestOrdering && !IsGuestOrderComplete;
    public bool IsGuestOrderComplete { get => _guestOrderComplete; private set { if (Set(ref _guestOrderComplete, value)) Changed(nameof(IsGuestKioskPage)); } }
    public string GuestName { get => _guestName; set => Set(ref _guestName, value); }
    public string GuestOrderNumber { get => _guestOrderNumber; private set => Set(ref _guestOrderNumber, value); }
    public bool IsAdmin => CurrentUser?.Role == UserRole.Admin;
    public bool IsCashier => CurrentUser?.Role == UserRole.Kasir;
    public bool IsCustomer => CurrentUser?.Role == UserRole.Pelanggan;
    public string CurrentUserSummary => CurrentUser is null ? "" : $"{CurrentUser.FullName} · {CurrentUser.Role}";
    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) { Changed(nameof(BusyLabel)); foreach (var command in _busyCommands) command.RaiseCanExecuteChanged(); foreach (var item in NavigationItems) if (item.Command is AsyncRelayCommand command) command.RaiseCanExecuteChanged(); BeginCheckoutCommand.RaiseCanExecuteChanged(); } } }
    public string BusyLabel => IsBusy ? "Sedang memuat atau menyimpan…" : "";
    public bool ApiAvailable { get => _apiAvailable; private set { if (Set(ref _apiAvailable, value)) Changed(nameof(ApiStatusColor)); } }
    public string ApiStatus { get => _apiStatus; private set => Set(ref _apiStatus, value); }
    public string ApiStatusColor => ApiAvailable ? "#138A62" : "#B54736";
    public string Username { get => _username; set => Set(ref _username, value); }
    public string Password { get => _password; set => Set(ref _password, value); }
    public string ErrorMessage { get => _errorMessage; private set { if (Set(ref _errorMessage, value)) Changed(nameof(HasError)); } }
    public string SuccessMessage { get => _successMessage; private set { if (Set(ref _successMessage, value)) Changed(nameof(HasSuccess)); } }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasSuccess => !string.IsNullOrWhiteSpace(SuccessMessage);
    public bool HasConfigurationWarning => !string.IsNullOrWhiteSpace(ConfigurationWarning);
    public string ApiUrl => _configuration.ApiBaseAddress.ToString();
    public string? ConfigurationWarning => _configuration.Warning;
    public string CurrentPage { get => _currentPage; private set { if (Set(ref _currentPage, value)) { Changed(nameof(PageTitle)); Changed(nameof(IsDashboardPage)); Changed(nameof(IsProductsPage)); Changed(nameof(IsCashierPage)); Changed(nameof(IsHistoryPage)); Changed(nameof(IsCatalogPage)); } } }
    public string PageTitle => CurrentPage switch { "Dashboard" => "Dashboard", "Products" => "Produk", "Cashier" => "Kasir", "History" => "Riwayat transaksi", "Catalog" => "Katalog produk", _ => "POS Desktop" };
    public bool IsDashboardPage => CurrentPage == "Dashboard";
    public bool IsProductsPage => CurrentPage == "Products";
    public bool IsCashierPage => CurrentPage == "Cashier";
    public bool IsHistoryPage => CurrentPage == "History";
    public bool IsCatalogPage => CurrentPage == "Catalog";
    public string ProductName { get => _productName; set => Set(ref _productName, value); }
    public string ProductPriceInput { get => _productPriceInput; set => Set(ref _productPriceInput, value); }
    public string ProductSearch { get => _productSearch; set { if (Set(ref _productSearch, value)) { Changed(nameof(FilteredProducts)); Changed(nameof(FilteredProductsEmpty)); } } }
    public bool ProductActive { get => _productActive; set => Set(ref _productActive, value); }
    public int? EditingProductId { get => _editingProductId; private set { if (Set(ref _editingProductId, value)) { Changed(nameof(IsEditingProduct)); Changed(nameof(ProductEditorTitle)); } } }
    public bool IsEditingProduct => EditingProductId.HasValue;
    public string ProductEditorTitle => IsEditingProduct ? "Edit produk" : "Tambah produk";
    public CategoryDto? SelectedCategory { get => _selectedCategory; set => Set(ref _selectedCategory, value); }
    public ProductDto? SelectedProduct { get => _selectedProduct; set { if (Set(ref _selectedProduct, value)) { Changed(nameof(SelectedProductIsNull)); AddSelectedProductCommand.RaiseCanExecuteChanged(); if (IsAdmin && value is not null) SetProductEditor(value); } } }
    public bool SelectedProductIsNull => SelectedProduct is null;
    public CustomerChoice? SelectedCustomer { get => _selectedCustomer; set { if (Set(ref _selectedCustomer, value)) Changed(nameof(SelectedCustomerLabel)); } }
    public PaymentChoice SelectedPayment { get => _selectedPayment; set { if (Set(ref _selectedPayment, value)) Changed(nameof(SelectedPaymentLabel)); } }
    public GuestOrderDto? SelectedGuestOrder { get => _selectedGuestOrder; set { if (Set(ref _selectedGuestOrder, value)) { Changed(nameof(SelectedGuestOrderTitle)); CompleteGuestOrderCommand.RaiseCanExecuteChanged(); } } }
    public TransactionSummaryDto? SelectedTransaction { get => _selectedTransaction; set { if (Set(ref _selectedTransaction, value)) { Changed(nameof(HasSelectedTransaction)); Changed(nameof(SelectedTransactionTitle)); } } }
    public TransactionDto? TransactionDetail { get => _transactionDetail; private set { if (Set(ref _transactionDetail, value)) { Changed(nameof(TransactionDetailIsNull)); Changed(nameof(TransactionCashierText)); Changed(nameof(TransactionCustomerText)); Changed(nameof(SelectedTransactionTitle)); } } }
    public bool TransactionDetailIsNull => TransactionDetail is null;
    public string TransactionCashierText => $"Kasir: {TransactionDetail?.CashierName ?? "—"}";
    public string TransactionCustomerText => $"Pelanggan: {TransactionDetail?.CustomerName ?? "Umum"}";
    public DailySalesDto? DailySales { get => _dailySales; private set { if (Set(ref _dailySales, value)) { Changed(nameof(RevenueText)); Changed(nameof(TransactionCountText)); Changed(nameof(DailyTopProductsEmpty)); } } }
    public bool DailyTopProductsEmpty => DailySales?.TopProducts.Count is null or 0;
    public string RevenueText => (DailySales?.Revenue ?? 0m).ToString("C0", CultureInfo.GetCultureInfo("id-ID"));
    public string TransactionCountText => (DailySales?.TransactionCount ?? 0).ToString("N0", CultureInfo.GetCultureInfo("id-ID"));
    public bool ShowCheckoutConfirmation { get => _showCheckoutConfirmation; set => Set(ref _showCheckoutConfirmation, value); }
    public string CartTotalText => Cart.Sum(line => line.Subtotal).ToString("C0", CultureInfo.GetCultureInfo("id-ID"));
    public int CartCount => Cart.Sum(line => line.Quantity);
    public bool CartIsEmpty => Cart.Count == 0;
    public bool TransactionsEmpty => Transactions.Count == 0;
    public bool PendingGuestOrdersEmpty => PendingGuestOrders.Count == 0;
    public bool HasSelectedTransaction => SelectedTransaction is not null;
    public string SelectedTransactionTitle => TransactionDetail is null ? "Pilih transaksi untuk melihat detail" : $"{TransactionDetail.Number} · {TransactionDetail.CreatedAt.ToLocalTime():dd MMM yyyy HH:mm}";
    public string SelectedTransactionTotal => TransactionDetail?.Total.ToString("C0", CultureInfo.GetCultureInfo("id-ID")) ?? "—";
    public string SelectedTransactionPayment => TransactionDetail?.PaymentMethod.ToString() ?? "—";
    public string SelectedPaymentLabel => $"Metode: {SelectedPayment.Name}";
    public string SelectedCustomerLabel => $"Pelanggan: {SelectedCustomer?.Name ?? "Umum"}";
    public string SelectedGuestOrderTitle => SelectedGuestOrder is null ? "Pilih pesanan dari antrean." :
        $"{SelectedGuestOrder.Number} · {SelectedGuestOrder.GuestName} · {SelectedGuestOrder.PaymentMethod} · {SelectedGuestOrder.Total.ToString("C0", CultureInfo.GetCultureInfo("id-ID"))}";
    public IEnumerable<ProductDto> FilteredProducts => string.IsNullOrWhiteSpace(ProductSearch) ? Products :
        Products.Where(p => p.Name.Contains(ProductSearch, StringComparison.CurrentCultureIgnoreCase) || p.CategoryName.Contains(ProductSearch, StringComparison.CurrentCultureIgnoreCase));
    public bool FilteredProductsEmpty => !FilteredProducts.Any();

    public async Task InitializeAsync()
    {
        if (ConfigurationWarning is not null) ErrorMessage = ConfigurationWarning;
        await CheckApiAsync(null);
    }

    private AsyncRelayCommand Command(Func<object?, Task> action) => new(action, _ => !IsBusy, ex => ErrorMessage = $"Operasi gagal: {ex.Message}");

    private async Task CheckApiAsync(object? _)
    {
        ApiStatus = "Memeriksa API…";
        var health = await _services.CheckHealthAsync();
        ApiAvailable = health.Success;
        ApiStatus = health.Success ? $"API terhubung · {ApiUrl}" : $"API belum terhubung · {ApiUrl}";
        if (!health.Success && !HasError) ErrorMessage = "API tidak tersedia. Jendela login tetap dapat digunakan; coba lagi setelah API aktif.";
        else if (health.Success && ErrorMessage.Contains("API tidak tersedia", StringComparison.OrdinalIgnoreCase)) ErrorMessage = "";
    }

    private async Task LoginAsync(object? _)
    {
        ClearMessages();
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password))
        { ErrorMessage = "Masukkan username dan password."; return; }
        IsBusy = true;
        try
        {
            var result = await _services.Auth.LoginAsync(Username.Trim(), Password);
            if (!result.Success || result.Value is null)
            {
                ErrorMessage = result.Message;
                if (result.Error == ErrorCode.ServiceUnavailable) MarkApiUnavailable();
                return;
            }
            CurrentUser = result.Value;
            Password = "";
            ApiAvailable = true;
            ApiStatus = $"API terhubung · {ApiUrl}";
            ConfigureNavigation();
            if (IsAdmin) await LoadDashboardAsync();
            else if (IsCashier) await LoadCashierAsync();
            else await LoadProductsAsync();
            SuccessMessage = $"Selamat datang, {CurrentUser.FullName}.";
        }
        finally { IsBusy = false; }
    }

    private async Task BeginGuestOrderingAsync(object? _)
    {
        ClearUserState();
        IsGuestOrdering = true;
        ClearMessages();
        IsBusy = true;
        try
        {
            var result = await _services.Kiosk.GetCatalogAsync();
            if (!result.Success)
            {
                ErrorMessage = result.Message;
                if (result.Error == ErrorCode.ServiceUnavailable) MarkApiUnavailable();
                return;
            }
            Products.Clear();
            foreach (var product in result.Value ?? []) Products.Add(product);
            ApiAvailable = true;
            ApiStatus = $"Menu tersedia · {ApiUrl}";
        }
        finally { IsBusy = false; }
    }

    private void ExitGuestOrdering()
    {
        ClearUserState();
        IsGuestOrdering = false;
    }

    private void StartAnotherGuestOrder()
    {
        Cart.Clear();
        OnCartChanged();
        GuestName = "";
        GuestOrderNumber = "";
        IsGuestOrderComplete = false;
        SelectedProduct = null;
        ClearMessages();
    }

    private async Task SubmitGuestOrderAsync(object? _)
    {
        ClearMessages();
        if (string.IsNullOrWhiteSpace(GuestName) || GuestName.Trim().Length > 80)
        { ErrorMessage = "Isi nama (maksimal 80 karakter)."; return; }
        if (Cart.Count == 0) { ErrorMessage = "Pilih menu terlebih dahulu."; return; }
        if (SelectedPayment is null) { ErrorMessage = "Pilih metode pembayaran."; return; }
        var items = Cart.Select(line => new CheckoutItem(line.ProductId, line.Quantity)).ToArray();
        if (_pendingGuestOrderRequest is not null &&
            (_pendingGuestOrderRequest.GuestName != GuestName.Trim() ||
             _pendingGuestOrderRequest.PaymentMethod != SelectedPayment.Value ||
             !_pendingGuestOrderRequest.Items.SequenceEqual(items)))
        {
            ErrorMessage = "Pesanan sebelumnya belum terkonfirmasi. Kembalikan nama, menu, dan pembayaran ke data yang sama untuk mencoba lagi.";
            return;
        }
        var request = _pendingGuestOrderRequest ??= new GuestOrderRequest(GuestName.Trim(), SelectedPayment.Value,
            items, Guid.NewGuid());
        IsBusy = true;
        try
        {
            var result = await _services.Kiosk.PlaceOrderAsync(request);
            if (!result.Success || result.Value is null)
            {
                ErrorMessage = result.Message;
                if (result.Error == ErrorCode.Validation) _pendingGuestOrderRequest = null;
                if (result.Error == ErrorCode.ServiceUnavailable) MarkApiUnavailable();
                return;
            }
            GuestName = result.Value.GuestName;
            GuestOrderNumber = result.Value.Number;
            IsGuestOrderComplete = true;
            Cart.Clear();
            _pendingGuestOrderRequest = null;
            OnCartChanged();
            SuccessMessage = "Pesanan sudah masuk antrean. Tunjukkan nomor pesanan di kasir untuk membayar.";
        }
        finally { IsBusy = false; }
    }

    private async Task LogoutAsync(object? _)
    {
        ClearMessages();
        IsBusy = true;
        try
        {
            var result = await _services.Auth.LogoutAsync();
            if (!result.Success) ErrorMessage = result.Message;
        }
        finally
        {
            ClearUserState();
            CurrentUser = null;
            NavigationItems.Clear();
            IsBusy = false;
            _ = CheckApiAsync(null);
        }
    }

    private async Task NavigateAsync(object? parameter)
    {
        if (parameter is not string key) return;
        CurrentPage = key;
        ClearMessages();
        IsBusy = true;
        try
        {
            if (key == "Dashboard") await LoadDashboardAsync();
            else if (key is "Products" or "Catalog") await LoadProductsAsync();
            else if (key == "Cashier") await LoadCashierAsync();
            else if (key == "History") await LoadHistoryAsync();
        }
        finally { IsBusy = false; }
    }

    private Task RefreshCurrentPageAsync(object? _) => NavigateAsync(CurrentPage);

    private async Task LoadDashboardAsync()
    {
        var result = await _services.Dashboard.GetTodayAsync();
        if (result.Success) DailySales = result.Value;
        else ErrorMessage = result.Message;
    }

    private async Task LoadProductsAsync()
    {
        var result = IsGuestOrdering ? await _services.Kiosk.GetCatalogAsync()
            : IsAdmin ? await _services.Products.GetAllAsync() : await _services.Products.GetCatalogAsync();
        if (!result.Success) { ErrorMessage = result.Message; return; }
        Products.Clear();
        foreach (var product in result.Value ?? []) Products.Add(product);
        if (IsAdmin)
        {
            var categories = await _services.Products.GetCategoriesAsync();
            if (categories.Success)
            {
                Categories.Clear();
                foreach (var category in categories.Value ?? []) Categories.Add(category);
            }
            else ErrorMessage = categories.Message;
        }
        SelectedProduct = null;
    }

    private async Task LoadCashierAsync()
    {
        await LoadProductsAsync();
        if (ErrorMessage.Length > 0) return;
        var options = await _services.Transactions.GetCustomerOptionsAsync();
        if (!options.Success) { ErrorMessage = options.Message; return; }
        Customers.Clear();
        Customers.Add(new CustomerChoice(null, "Umum"));
        foreach (var customer in options.Value ?? []) Customers.Add(new CustomerChoice(customer.Id, customer.FullName));
        SelectedCustomer = Customers.FirstOrDefault();
        var pending = await _services.Kiosk.GetPendingOrdersAsync();
        if (!pending.Success) { ErrorMessage = pending.Message; return; }
        PendingGuestOrders.Clear();
        foreach (var order in pending.Value ?? []) PendingGuestOrders.Add(order);
        SelectedGuestOrder = null;
    }

    private async Task LoadHistoryAsync()
    {
        var result = await _services.Transactions.GetTransactionsPageAsync(1, 50);
        if (!result.Success) { ErrorMessage = result.Message; return; }
        Transactions.Clear();
        foreach (var transaction in result.Value?.Items ?? []) Transactions.Add(transaction);
        TransactionDetail = null;
        Changed(nameof(SelectedTransactionTitle));
    }

    private async Task SaveProductAsync(object? _)
    {
        ClearMessages();
        if (string.IsNullOrWhiteSpace(ProductName) || ProductName.Trim().Length > 160)
        { ErrorMessage = "Nama produk wajib diisi (maksimal 160 karakter)."; return; }
        if (SelectedCategory is null) { ErrorMessage = "Pilih kategori produk."; return; }
        if (!decimal.TryParse(ProductPriceInput, NumberStyles.Number, CultureInfo.CurrentCulture, out var price) &&
            !decimal.TryParse(ProductPriceInput, NumberStyles.Number, CultureInfo.InvariantCulture, out price))
        { ErrorMessage = "Harga harus berupa angka."; return; }
        if (price <= 0 || decimal.Round(price, 2) != price) { ErrorMessage = "Harga harus positif dan maksimal dua angka desimal."; return; }
        IsBusy = true;
        try
        {
            var input = new ProductInput(ProductName.Trim(), SelectedCategory.Id, price, ProductActive);
            var result = EditingProductId is int id
                ? await _services.Products.UpdateAsync(id, input)
                : await _services.Products.CreateAsync(input);
            if (!result.Success) { ErrorMessage = result.Message; return; }
            SuccessMessage = EditingProductId is null ? "Produk berhasil ditambahkan." : "Produk berhasil diperbarui.";
            ResetProductEditor();
            await LoadProductsAsync();
        }
        finally { IsBusy = false; }
    }

    private async Task DeactivateProductAsync(object? _)
    {
        ClearMessages();
        if (SelectedProduct is null) { ErrorMessage = "Pilih produk yang akan dinonaktifkan."; return; }
        IsBusy = true;
        try
        {
            var result = await _services.Products.DeactivateAsync(SelectedProduct.Id);
            if (!result.Success) { ErrorMessage = result.Message; return; }
            SuccessMessage = "Produk dinonaktifkan.";
            ResetProductEditor();
            await LoadProductsAsync();
        }
        finally { IsBusy = false; }
    }

    private async Task LoadTransactionDetailAsync(object? _)
    {
        ClearMessages();
        if (SelectedTransaction is null) { ErrorMessage = "Pilih transaksi terlebih dahulu."; return; }
        IsBusy = true;
        try
        {
            var result = await _services.Transactions.GetDetailAsync(SelectedTransaction.Id);
            if (result.Success) TransactionDetail = result.Value;
            else ErrorMessage = result.Message;
            Changed(nameof(SelectedTransactionTitle));
            Changed(nameof(SelectedTransactionTotal));
            Changed(nameof(SelectedTransactionPayment));
        }
        finally { IsBusy = false; }
    }

    private void AddSelectedProduct()
    {
        if (SelectedProduct is null || !SelectedProduct.IsActive || (!IsCashier && !IsGuestOrdering)) return;
        var existing = Cart.FirstOrDefault(line => line.ProductId == SelectedProduct.Id);
        if (existing is not null) { ChangeQuantity(existing, 1); return; }
        Cart.Add(new CartLineViewModel(SelectedProduct.Id, SelectedProduct.Name, SelectedProduct.Price, ChangeQuantity, RemoveCartLine));
        OnCartChanged();
    }

    private void ChangeQuantity(CartLineViewModel? line, int delta)
    {
        if (line is null || !Cart.Contains(line)) return;
        if (IsGuestOrdering && delta > 0 && line.Quantity >= 100)
        { ErrorMessage = "Maksimal 100 untuk satu menu."; return; }
        if (delta < 0 && line.Quantity == 1) { Cart.Remove(line); OnCartChanged(); return; }
        line.ApplyStep(delta);
        OnCartChanged();
    }

    private void RemoveCartLine(CartLineViewModel? line)
    {
        if (line is not null && Cart.Remove(line)) OnCartChanged();
    }

    private void OnCartChanged()
    {
        _pendingRequestId = null;
        Changed(nameof(CartTotalText));
        Changed(nameof(CartCount));
        Changed(nameof(CartIsEmpty));
        BeginCheckoutCommand.RaiseCanExecuteChanged();
    }

    private void BeginCheckout()
    {
        ClearMessages();
        if (Cart.Count == 0) { ErrorMessage = "Keranjang masih kosong."; return; }
        ShowCheckoutConfirmation = true;
    }

    private async Task ConfirmCheckoutAsync(object? _)
    {
        if (Cart.Count == 0 || SelectedPayment is null) { ErrorMessage = "Keranjang atau metode pembayaran tidak valid."; return; }
        ClearMessages();
        _pendingRequestId ??= Guid.NewGuid();
        var request = new CheckoutRequest(SelectedCustomer?.Id, SelectedPayment.Value,
            Cart.Select(line => new CheckoutItem(line.ProductId, line.Quantity)).ToArray(), _pendingRequestId.Value);
        IsBusy = true;
        try
        {
            var result = await _services.Transactions.CheckoutAsync(request);
            if (!result.Success || result.Value is null)
            {
                ErrorMessage = result.Message;
                if (result.Error == ErrorCode.ServiceUnavailable) MarkApiUnavailable();
                return;
            }
            ShowCheckoutConfirmation = false;
            SuccessMessage = $"Transaksi {result.Value.Number} tersimpan · {result.Value.Total.ToString("C0", CultureInfo.GetCultureInfo("id-ID"))}.";
            Cart.Clear();
            _pendingRequestId = null;
            OnCartChanged();
            await LoadHistoryAsync();
            if (IsAdmin) await LoadDashboardAsync();
        }
        finally { IsBusy = false; }
    }

    private async Task CompleteGuestOrderAsync(object? _)
    {
        ClearMessages();
        if (SelectedGuestOrder is null) { ErrorMessage = "Pilih pesanan kiosk terlebih dahulu."; return; }
        IsBusy = true;
        try
        {
            var result = await _services.Kiosk.CompleteOrderAsync(SelectedGuestOrder.Id);
            if (!result.Success || result.Value is null)
            {
                ErrorMessage = result.Message;
                if (result.Error == ErrorCode.ServiceUnavailable) MarkApiUnavailable();
                return;
            }
            SuccessMessage = $"Pembayaran diterima · transaksi {result.Value.Number} tersimpan.";
            await LoadHistoryAsync();
            var pending = await _services.Kiosk.GetPendingOrdersAsync();
            if (pending.Success)
            {
                PendingGuestOrders.Clear();
                foreach (var order in pending.Value ?? []) PendingGuestOrders.Add(order);
            }
            SelectedGuestOrder = null;
            if (IsAdmin) await LoadDashboardAsync();
        }
        finally { IsBusy = false; }
    }

    private void ConfigureNavigation()
    {
        NavigationItems.Clear();
        var keys = CurrentUser?.Role switch
        {
            UserRole.Admin => new[] { ("Dashboard", "Dashboard"), ("Produk", "Products"), ("Transaksi", "History") },
            UserRole.Kasir => new[] { ("Kasir", "Cashier"), ("Riwayat saya", "History") },
            UserRole.Pelanggan => new[] { ("Katalog", "Catalog"), ("Riwayat pembelian", "History") },
            _ => []
        };
        foreach (var (title, key) in keys)
        {
            var command = new AsyncRelayCommand(_ => NavigateAsync(key), _ => !IsBusy,
                ex => ErrorMessage = $"Navigasi gagal: {ex.Message}");
            NavigationItems.Add(new NavigationItem(title, key, command));
        }
        CurrentPage = keys.FirstOrDefault().Item2 ?? "Dashboard";
    }

    private void SetProductEditor(ProductDto product)
    {
        EditingProductId = product.Id;
        ProductName = product.Name;
        ProductPriceInput = product.Price.ToString("0.00", CultureInfo.InvariantCulture);
        ProductActive = product.IsActive;
        SelectedCategory = Categories.FirstOrDefault(c => c.Id == product.CategoryId);
    }

    private void ResetProductEditor()
    {
        EditingProductId = null;
        ProductName = "";
        ProductPriceInput = "";
        ProductActive = true;
        SelectedCategory = null;
    }

    private void ClearUserState()
    {
        IsGuestOrdering = false;
        IsGuestOrderComplete = false;
        GuestName = "";
        GuestOrderNumber = "";
        ProductSearch = "";
        _pendingGuestOrderRequest = null;
        Cart.Clear();
        Products.Clear();
        Categories.Clear();
        Customers.Clear();
        Transactions.Clear();
        PendingGuestOrders.Clear();
        DailySales = null;
        TransactionDetail = null;
        SelectedTransaction = null;
        SelectedProduct = null;
        SelectedCustomer = null;
        SelectedGuestOrder = null;
        NavigationItems.Clear();
        ResetProductEditor();
        CurrentPage = "Dashboard";
        ShowCheckoutConfirmation = false;
        _pendingRequestId = null;
        ClearMessages();
    }

    private void ClearMessages() { ErrorMessage = ""; SuccessMessage = ""; }

    private void MarkApiUnavailable()
    {
        ApiAvailable = false;
        ApiStatus = $"API belum terhubung · {ApiUrl}";
    }
}
