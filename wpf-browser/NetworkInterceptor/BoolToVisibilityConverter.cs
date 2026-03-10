using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PrivacyMonitor.NetworkInterceptor
{
    /// <summary>
    /// Converts a <see cref="bool"/> value to <see cref="Visibility"/>.
    /// True maps to Visible, false (or non-bool/nullable false) maps to Collapsed.
    /// Supports inversion via the <see cref="Invert"/> property, and also honors
    /// the "invert" parameter for one-off inversions in XAML.
    /// </summary>
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        /// <summary>
        /// Gets or sets whether to invert the conversion.
        /// </summary>
        public bool Invert { get; set; }

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool visible = value switch
            {
                bool b => b,
                _ => false
            };

            // Allow XAML to pass 'invert' as parameter for quick one-off inversion
            bool paramInvert = false;
            if (parameter is string paramStr && paramStr.Equals("invert", StringComparison.OrdinalIgnoreCase))
                paramInvert = true;

            if (Invert ^ paramInvert)
                visible = !visible;

            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is Visibility vis)
                return Invert ? vis != Visibility.Visible : vis == Visibility.Visible;

            throw new NotSupportedException($"Cannot convert back value of type {value?.GetType().Name ?? "null"} to bool.");
        }
    }
}
