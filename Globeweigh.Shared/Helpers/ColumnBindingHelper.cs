using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using DevExpress.Xpf.Editors.Settings;
using DevExpress.Xpf.Grid;

namespace Globeweigh.Shared.Helpers
{
//    public class GridColumn : INotifyPropertyChanged
//    {
//        private string _fieldName;
//
//        public string FieldName
//        {
//            get { return _fieldName; }
//            set { _fieldName = value; }
//        }
//        private string _header;
//
//        public string Header
//        {
//            get { return _header; }
//            set { _header = value; }
//        }
//        public SettingsType Setting { get; set; }
//        public TextEditSettings EditSettings { get; set; }
//         
//
//        public event PropertyChangedEventHandler PropertyChanged;
//        protected void OnPropertyChanged(string name)
//        {
//            if (this.PropertyChanged != null)
//                this.PropertyChanged(this, new PropertyChangedEventArgs(name));
//        }
//    }
//    public enum SettingsType { Default, Text }

    public class TextColumn : GridColumn
    {
        public object Tag { get; set; }
        public string ToolTip { get; set; }
    }

    public class ColumnBindingHelper
    {
        public static readonly DependencyProperty BindingPathProperty = DependencyProperty.RegisterAttached("BindingPath", typeof(string), typeof(ColumnBindingHelper), new UIPropertyMetadata(null, new PropertyChangedCallback(OnBindingPathChanged)));

        public static string GetBindingPath(DependencyObject target)
        {
            return (string)target.GetValue(BindingPathProperty);
        }
        public static void SetBindingPath(DependencyObject target, string value)
        {
            target.SetValue(BindingPathProperty, value);
        }
        private static void OnBindingPathChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            OnBindingPathChanged(o, (string)e.OldValue, (string)e.NewValue);
        }
        private static void OnBindingPathChanged(DependencyObject o, string oldValue, string newValue)
        {
            var column = o as DevExpress.Xpf.Grid.GridColumn;
            column.DisplayMemberBinding = new Binding(newValue);
        }

    }
}
