using System.Globalization;

namespace PosDesktop.ViewModels;

public static class ProductPriceText
{
    private const NumberStyles InputStyles = NumberStyles.AllowLeadingWhite |
        NumberStyles.AllowTrailingWhite | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    public static string Format(decimal price, CultureInfo culture) => price.ToString("0.##", culture);

    public static bool TryParse(string? input, CultureInfo culture, out decimal price)
    {
        if (decimal.TryParse(input, InputStyles, culture, out price)) return true;
        return decimal.TryParse(input, InputStyles, CultureInfo.InvariantCulture, out price);
    }
}
