using System;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using GalaSoft.MvvmLight.Command;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared.Helpers;

namespace Globeweigh.Touch
{
    public class TouchViewModelBase : INotifyPropertyChanged
    {
        private RelayCommand _GlobalLogoutCommand;
        public virtual RelayCommand GlobalLogoutCommand
        {
            get
            {
                return _GlobalLogoutCommand ?? (_GlobalLogoutCommand = new RelayCommand(() =>
                {
                    GlobalVariables.CurrentGlobeweighUser = null;
                    SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel =SimpleIoc.Default.GetInstance<LoginViewModel>();
                }));
            }
        }

        private bool _IsBusy;
        public bool IsBusy
        {
            get { return _IsBusy; }
            set { Set(ref _IsBusy, value); }
        }

        public string LoggedInUser
        {
            get
            {
                if (GlobalVariables.CurrentGlobeweighUser == null) return string.Empty;
                return GlobalVariables.CurrentGlobeweighUser.FirstName + " " +
                       GlobalVariables.CurrentGlobeweighUser.Surname;
            }
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
    }
}
