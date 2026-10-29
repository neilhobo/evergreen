using DevExpress.Xpf.Core;
using DevExpress.Xpf.Grid;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared;
using MvvmDialogs;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using System.Windows;



namespace Globeweigh.Admin.Helpers
{
    public static class Utilities
    {
        private static IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();

        public static MainWindow GetMainWindow()
        {
            return (from Window f in System.Windows.Application.Current.Windows where f.GetType() == typeof(MainWindow) select f).Cast<MainWindow>().FirstOrDefault();
        }

        public static void PrintExportGrid(TableView tableView, IViewModel vm)
        {

            var dialogViewModel = SimpleIoc.Default.GetInstance<PrintExportOptionsViewModel>();
            dialogViewModel.CurrentTableView = tableView;
            bool? success = _dialogService.ShowDialog<PrintExportOptionsView>(vm, dialogViewModel);
            if (success == true)
            {

            }
        }




        public static void ExportCompletedOrdersQuickExcel(GridControl grid, string filePath)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));

            var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Completed Orders");

            // Headers (removed 3 columns)
            ws.Cells["A1"].Value = "Haulier";
            ws.Cells["B1"].Value = "Docket No";
            ws.Cells["C1"].Value = "Vehicle Reg";
            ws.Cells["D1"].Value = "Product";
            ws.Cells["E1"].Value = "Supplier";
            ws.Cells["F1"].Value = "Bog Name";
            ws.Cells["G1"].Value = "Weighed In Date";
            ws.Cells["H1"].Value = "Cubic M3";

            ws.Cells["A1:H1"].Style.Font.Bold = true;
            ws.Cells["A1:H1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            int excelRow = 2;
            decimal cubicTotal = 0m;

            // Loop over visible rows
            for (int visibleIndex = 0; visibleIndex < grid.VisibleRowCount; visibleIndex++)
            {
                int rowHandle = grid.GetRowHandleByVisibleIndex(visibleIndex);

                // Skip invalid handles, group rows, or special rows
                if (rowHandle < 0 || grid.IsGroupRowHandle(rowHandle))
                    continue;

                if (rowHandle == GridControl.InvalidRowHandle || rowHandle == GridControl.NewItemRowHandle)
                    continue;

                object rowObject = grid.GetRow(rowHandle);

                if (rowObject is vwWeighbridgeComplete item)
                {
                    ws.Cells[excelRow, 1].Value = item.HaulierName;
                    ws.Cells[excelRow, 2].Value = item.DocketNumber;
                    ws.Cells[excelRow, 3].Value = item.VehicleReg;
                    ws.Cells[excelRow, 4].Value = item.Product;
                    ws.Cells[excelRow, 5].Value = item.Supplier;
                    ws.Cells[excelRow, 6].Value = item.BogName;
                    ws.Cells[excelRow, 7].Value = item.DateWeightIn?.ToString("dd/MM/yyyy HH:mm");
                    ws.Cells[excelRow, 8].Value = item.CubicM3;           // now in column H

                    cubicTotal += item.CubicM3 ?? 0m;

                    excelRow++;
                }
            }

            // Add blank row + total if data exists
            if (excelRow > 2)
            {
                int blankRow = excelRow;
                int totalRow = excelRow + 1;

                ws.InsertRow(blankRow, 1);

                ws.Cells[totalRow, 7].Value = "TOTAL:";               // Column G (right before Cubic M3)
                ws.Cells[totalRow, 8].Value = cubicTotal;             // Column H

                var totalRng = ws.Cells[totalRow, 1, totalRow, 8];
                totalRng.Style.Font.Bold = true;

                ws.Cells[totalRow, 7].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                ws.Cells[totalRow, 8].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                ws.Cells[totalRow, 8].Style.Numberformat.Format = "#,##0.00";

                // Format numeric column (only Cubic M3 now)
                var numRng = ws.Cells[2, 8, totalRow, 8];  // only column H
                numRng.Style.Numberformat.Format = "#,##0.00";
                numRng.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
            }
            else
            {
                ws.Cells[2, 1].Value = "No visible records after filtering.";
            }

            ws.Cells[ws.Dimension.Address].AutoFitColumns();
            ws.View.FreezePanes(2, 1);

            package.SaveAs(new FileInfo(filePath));
        }


        public static void ExportDataTableToExcel(DataTable table, string filePath)
        {
            // Make sure license is set earlier in the app lifecycle
            // (do NOT put it here if you call this method multiple times)
            var package = new ExcelPackage();   // <-- license must be set before this line


            var ws = package.Workbook.Worksheets.Add("Completed Orders");
            ws.Cells["A1"].LoadFromDataTable(table, true);

            // Optional: make header bold (your code is correct but can be simplified)
            ws.Cells[1, 1, 1, table.Columns.Count].Style.Font.Bold = true;

            ws.Cells.AutoFitColumns();

            package.SaveAs(new FileInfo(filePath));
        }




        public static async Task<bool> PingHost(string nameOrAddress)
        {
            bool pingable = false;
            Ping pinger = new Ping();
            try
            {
                var reply = await pinger.SendPingAsync(nameOrAddress);
                pingable = reply.Status == IPStatus.Success;
            }
            catch (PingException)
            {
                return false;
            }
            return pingable;
        }
    }
}
