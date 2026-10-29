using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using DevExpress.PivotGrid.PivotTable;
using DevExpress.Xpf.Grid;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Admin.Helpers;
using Globeweigh.UI.Shared;
using Globeweigh.UI.Shared.Helpers;

namespace Globeweigh.Admin
{
    public class ViewModelBaseOld : INotifyPropertyChanged
    {
        public ViewModelBaseOld()
        {
            IsUnloading = false;
        }

//        private RelayCommand<TableView> _GlobalPrintExportCommand;
//        public virtual RelayCommand<TableView> GlobalPrintExportCommand
//        {
//            get
//            {
//                return _GlobalPrintExportCommand ?? (_GlobalPrintExportCommand = new RelayCommand<TableView>(tableView =>
//                {
//                    if (tableView == null) return;
//                    Utilities.PrintExportGrid(tableView, (IViewModel)tableView.DataContext);
//                }));
//            }
//        }

        private bool _IsBusy;
        public bool IsBusy
        {
            get { return _IsBusy; }
            set { Set(ref _IsBusy, value); }
        }

        private bool _IsUnloading;
        public bool IsUnloading
        {
            get { return _IsUnloading; }
            set { Set(ref _IsUnloading, value); }
        }

        private bool _IsMyMachine;
        public bool IsMyMachine
        {
            get { return UtilitiesShared.IsMyMachine; }
            set { Set(ref _IsMyMachine, value); }
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
