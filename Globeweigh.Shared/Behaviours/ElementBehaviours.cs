using DevExpress.Xpf.Editors;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Globeweigh.Shared
{
    public static class ElementBehaviours
    {
        #region ComboBoxEdit PopupSuppress

        public static Boolean GetPopupSuppressed(FrameworkElement element)
        {
            return (Boolean)element.GetValue(PopupSuppressedProperty);
        }

        public static void SetPopupSuppressed(FrameworkElement element, Boolean value)
        {
            element.SetValue(PopupSuppressedProperty, value);
        }

        public static readonly DependencyProperty PopupSuppressedProperty = DependencyProperty.RegisterAttached("PopupSuppressed", typeof(Boolean), typeof(ElementBehaviours), new PropertyMetadata(false, OnPopupSuppressedChanged));

        private static void OnPopupSuppressedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ComboBoxEdit element = d as ComboBoxEdit;
            if (element != null)
            {
                element.PopupOpening += (s, e2) =>
                {
                    e2.Cancel = true;
                };
            }
        }

        #endregion

        #region ComboBoxEdit MouseUpCommand

        public static string GetMouseUpCommandName(DependencyObject obj)
        {
            return (string)obj.GetValue(MouseUpCommandNameProperty);
        }

        public static void SetMouseUpCommandName(DependencyObject obj, string value)
        {
            obj.SetValue(MouseUpCommandNameProperty, value);
        }

        public static readonly DependencyProperty MouseUpCommandNameProperty =
            DependencyProperty.RegisterAttached("MouseUpCommandName",
            typeof(string), typeof(ElementBehaviours), new PropertyMetadata(null, OnMouseUpCommandNameChanged));

        private static void OnMouseUpCommandNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            FrameworkElement element = d as FrameworkElement;
            if (element != null)
            {
                element.MouseUp += (s, e2) =>
                {
                    var viewModel = element.DataContext;
                    if (viewModel == null) return;
                    var methodInfo = viewModel.GetType().GetMethod(e.NewValue.ToString());
                    if (methodInfo != null) methodInfo.Invoke(viewModel, new[] { element.Name });
                };
            }
        }

        #endregion

        #region TextBlock TextBlockMouseUpCommand

        public static string GetTextBlockMouseUpCommandName(DependencyObject obj)
        {
            return (string)obj.GetValue(TextBlockMouseUpCommandNameProperty);
        }

        public static void SetTextBlockMouseUpCommandName(DependencyObject obj, string value)
        {
            obj.SetValue(MouseUpCommandNameProperty, value);
        }

        public static readonly DependencyProperty TextBlockMouseUpCommandNameProperty
            = DependencyProperty.RegisterAttached("TextBlockMouseUpCommandName", typeof(string), typeof(ElementBehaviours), new PropertyMetadata(null, OnTextBlockMouseUpCommandNameChanged));

        private static void OnTextBlockMouseUpCommandNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            FrameworkElement element = d as FrameworkElement;
            if (element != null)
            {
                element.MouseUp += (s, e2) =>
                {
                    var viewModel = element.DataContext;
                    if (viewModel == null) return;
                    var methodInfo = viewModel.GetType().GetMethod(e.NewValue.ToString());
                    if (methodInfo != null) methodInfo.Invoke(viewModel, new[] { element.Name });
                };
            }
        }

        #endregion

        #region TextBoxFocus

        public static bool GetIsFocused(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsFocusedProperty);
        }

        public static void SetIsFocused(DependencyObject obj, bool value)
        {
            obj.SetValue(IsFocusedProperty, value);
        }

        public static readonly DependencyProperty IsFocusedProperty = DependencyProperty.RegisterAttached("IsFocused", typeof(bool), typeof(ElementBehaviours), new UIPropertyMetadata(false, OnIsFocusedPropertyChanged));

        private static void OnIsFocusedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var uie = (UIElement)d;
            if ((bool)e.NewValue)
            {
                if (uie.GetType() == typeof(TextBox))
                {
                    ((TextBox)uie).Focus();
                    ((TextBox)uie).CaretIndex = ((TextBox)uie).Text.Length;
                }
                else
                {
                    uie.Focus();
                }
            }
        }

        #endregion

        #region TextBlock TextBlockMouseClickCommand

        public static readonly DependencyProperty InputBindingsProperty =
                DependencyProperty.RegisterAttached("InputBindings", typeof(InputBindingCollection), typeof(ElementBehaviours),
                new FrameworkPropertyMetadata(new InputBindingCollection(),
                (sender, e) =>
                {
                    var element = sender as UIElement;
                    if (element == null) return;
                    element.InputBindings.Clear();
                    element.InputBindings.AddRange((InputBindingCollection)e.NewValue);
                }));

        public static InputBindingCollection GetInputBindings(UIElement element)
        {
            return (InputBindingCollection)element.GetValue(InputBindingsProperty);
        }

        public static void SetInputBindings(UIElement element, InputBindingCollection inputBindings)
        {
            element.SetValue(InputBindingsProperty, inputBindings);
        }

        #endregion
    }
}
