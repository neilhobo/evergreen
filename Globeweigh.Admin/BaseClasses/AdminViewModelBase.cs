using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using DevExpress.Xpf.Grid;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Admin.Helpers;
using Globeweigh.Model;
using Globeweigh.Shared;

namespace Globeweigh.Admin
{
    public class AdminViewModelBase : ViewModelBase
    {
        private RelayCommand<TableView> _GlobalPrintExportCommand;
        public virtual RelayCommand<TableView> GlobalPrintExportCommand
        {
            get
            {
                return _GlobalPrintExportCommand ?? (_GlobalPrintExportCommand = new RelayCommand<TableView>(tableView =>
                {
                    if (tableView == null) return;
                    Utilities.PrintExportGrid(tableView, (IViewModel)tableView.DataContext);
                }));
            }
        }



        public bool IsAdmin
        {
            get { return GlobalVariables.IsGlobeweighAdminOrAdmin; }
        }
    }
}
