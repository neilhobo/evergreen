using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using Globeweigh.Model;

namespace Globeweigh.Shared.Converters
{
    public class EnumEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return false;
            var current = value.ToString();
            var target = parameter.ToString();
            return string.Equals(current, target, StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Only update when a RadioButton becomes checked
            if (value is bool b && b && parameter != null && targetType.IsEnum)
            {
                return Enum.Parse(targetType, parameter.ToString(), ignoreCase: true);
            }
            return Binding.DoNothing;
        }
    }

    public class BoolToWindowStateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            (value as bool?) == true ? WindowState.Maximized : WindowState.Normal;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            (WindowState)value == WindowState.Maximized;
    }

    public class NullableBoolToRadioConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return parameter.ToString() == "Both";
            if (value is bool boolValue)
            {
                if (boolValue && parameter.ToString() == "Euro") return true;
                if (!boolValue && parameter.ToString() == "Sterling") return true;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if ((bool)value)
            {
                if (parameter.ToString() == "Euro") return true;
                if (parameter.ToString() == "Sterling") return false;
                if (parameter.ToString() == "Both") return null;
            }
            return Binding.DoNothing;
        }
    }

    public sealed class FalseToNavigationStyleNoneConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool)
            {
                if (!(bool) value) return DevExpress.Xpf.Grid.GridViewNavigationStyle.None;
            }

            return DevExpress.Xpf.Grid.GridViewNavigationStyle.Row;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class UserTypeEnumBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null)
            {
                return false; // or return parameter.Equals(YourEnumType.SomeDefaultValue);
            }
            return value.Equals((int)(eUserType)parameter);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value.Equals(true)) return (int)(eUserType)parameter;
            else return null;
        }
    }

    public sealed class IsFactoryUserToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return Visibility.Collapsed;
            if (value is int)
            {
                if ((int)value == (int)eUserRole.Factory || (int)value == 0) return Visibility.Collapsed;
            }

            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class EnumBooleanPalletDeliveryConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null)
            {
                return false; // or return parameter.Equals(YourEnumType.SomeDefaultValue);
            }
            return value.Equals((int)(ePalletDeliveryCondition)parameter);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value.Equals(true)) return (int)(ePalletDeliveryCondition)parameter;
            else return null;
        }
    }

    public class EnumBooleanCostingTypeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null)
            {
                return false; // or return parameter.Equals(YourEnumType.SomeDefaultValue);
            }
            return value.Equals((int)(eCostingType)parameter);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value.Equals(true)) return (int)(eCostingType)parameter;
            else return null;
        }
    }



}
