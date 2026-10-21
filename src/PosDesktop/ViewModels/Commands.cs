using System.Windows.Input;

namespace PosDesktop.ViewModels;

public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => execute(parameter);
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null,
    Action<Exception>? onError = null) : ICommand
{
    private bool _running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke(parameter) ?? true);
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running = true;
        RaiseCanExecuteChanged();
        try { await execute(parameter); }
        catch (Exception exception) { onError?.Invoke(exception); }
        finally { _running = false; RaiseCanExecuteChanged(); }
    }
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed record NavigationItem(string Title, string Key, ICommand Command);
public sealed record CustomerChoice(int? Id, string Name);
public sealed record PaymentChoice(PosBackend.PaymentMethod Value, string Name);

public sealed class CartLineViewModel : ObservableObject
{
    private int _quantity = 1;
    public CartLineViewModel(int productId, string name, decimal unitPrice,
        Action<CartLineViewModel, int> step, Action<CartLineViewModel> remove)
    {
        ProductId = productId;
        Name = name;
        UnitPrice = unitPrice;
        IncreaseCommand = new RelayCommand(_ => step(this, 1));
        DecreaseCommand = new RelayCommand(_ => step(this, -1));
        RemoveCommand = new RelayCommand(_ => remove(this));
    }
    public int ProductId { get; }
    public string Name { get; }
    public decimal UnitPrice { get; }
    public int Quantity { get => _quantity; private set { if (Set(ref _quantity, Math.Max(1, value))) { Changed(nameof(Subtotal)); Changed(nameof(SubtotalText)); } } }
    public decimal Subtotal => UnitPrice * Quantity;
    public string UnitPriceText => UnitPrice.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("id-ID"));
    public string SubtotalText => Subtotal.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("id-ID"));
    public ICommand IncreaseCommand { get; }
    public ICommand DecreaseCommand { get; }
    public ICommand RemoveCommand { get; }
    public void ApplyStep(int delta) => Quantity = Math.Max(1, Quantity + delta);
}
