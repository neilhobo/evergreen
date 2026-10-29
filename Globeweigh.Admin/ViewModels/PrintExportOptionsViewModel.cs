using Globeweigh.Shared;
using MvvmDialogs;
using System.Windows;
using DevExpress.Xpf.Grid;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Admin.Helpers;
using SaveFileDialog = System.Windows.Forms.SaveFileDialog;

namespace Globeweigh.Admin
{
    public class PrintExportOptionsViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        #endregion

        #region Properties

        private string _ExportFilePath;
        public string ExportFilePath
        {
            get => _ExportFilePath;
            set => Set(ref _ExportFilePath, value);
        }

        public TableView CurrentTableView { get; set; }

        #endregion

        #region Commands

        public RelayCommand ExportToExcelCommand => new RelayCommand(OnExportToExcel);
        public RelayCommand ExportToPdfCommand => new RelayCommand(OnExportToPdf);
        public RelayCommand ExportToCsvCommand => new RelayCommand(OnExportToCsv);
        public RelayCommand PrintPreviewCommand => new RelayCommand(OnPrintPreview);



        #endregion

        private void OnPrintPreview()
        {
            
            CurrentTableView.ShowPrintPreview(Utilities.GetMainWindow());
            DialogResult = false;
        }

        private void OnExportToExcel()
        {
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Filter = "EXCEL (*.xls*)|*.xls*";
            dlg.DefaultExt = "xls";
            dlg.AddExtension = true;
            dlg.InitialDirectory = "C:";

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string sFileName = dlg.FileName;
                CurrentTableView.ExportToXls(sFileName);
                ExportFilePath = sFileName;
            }
            else
            {
                ExportFilePath = null;
            }

        }

        private void OnExportToPdf()
        {
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Filter = "PDF (*.pdf*)|*.pdf*";
            dlg.DefaultExt = "pdf";
            dlg.AddExtension = true;
            dlg.InitialDirectory = "C:";

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string sFileName = dlg.FileName;
                CurrentTableView.ExportToPdf(sFileName);
                ExportFilePath = sFileName;
            }
            else
            {
                ExportFilePath = null;
            }

        }

        private void OnExportToCsv()
        {
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Filter = "CSV (*.csv*)|*.csv*";
            dlg.DefaultExt = "csv";
            dlg.AddExtension = true;
            dlg.InitialDirectory = "C:";

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string sFileName = dlg.FileName;
                CurrentTableView.ExportToCsv(sFileName);
                ExportFilePath = sFileName;
            }
            else
            {
                ExportFilePath = null;
            }

        }

        public async void Load(FrameworkElement element)
        {
        }


        public void Unload(FrameworkElement element)
        {
            DialogResult = null;
            CurrentTableView = null;
            ExportFilePath = null;
        }
    }
}
