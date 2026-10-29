using System;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Windows;
using Globeweigh.Shared.Helpers;

namespace Globeweigh.Shared
{
    public class ViewModelBase : INotifyPropertyChanged, IViewModel
    {

//        public ViewModelBase()
//        {
//            IsUnloading = false;
//        }

        private bool _IsBusy;
        public bool IsBusy
        {
            get { return _IsBusy; }
            set { Set(ref _IsBusy, value); }
        }


        private bool _IsLoading;
        public bool IsLoading
        {
            get { return _IsLoading; }
            set { Set(ref _IsLoading, value); }
        }


        private bool _IsUnloading;
        public bool IsUnloading
        {
            get { return _IsUnloading; }
            set { Set(ref _IsUnloading, value); }
        }

        private bool _NoDbConnection;
        public bool NoDbConnection
        {
            get { return _NoDbConnection; }
            set { Set(ref _NoDbConnection, value); }
        }

        private bool? _DialogResult;
        public virtual bool? DialogResult
        {
            get => _DialogResult;
            set => Set(ref _DialogResult, value);
        }


        public bool IsMyMachine
        {
            get { return UtilitiesShared.IsMyMachine; }
        }

        protected virtual void Set<T>(ref T member, T val,
            [CallerMemberName] string propertyName = null)
        {
            if (object.Equals(member, val)) return;

            member = val;
            PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
        public event PropertyChangedEventHandler PropertyChanged = delegate { };

        public void RaisePropertyChanged<T>(Expression<Func<T>> property)
        {
            PropertyChanged(this, new PropertyChangedEventArgs(property.Name));
        }

        public virtual void Load(FrameworkElement element)
        {

        }

        public virtual void Unload(FrameworkElement element)
        {
            DialogResult = null;
            NoDbConnection = false;
            IsBusy = false;
        }
    }
}
