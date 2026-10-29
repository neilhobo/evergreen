using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Globeweigh.Admin.Helpers
{
    public static class ExcelHelper
    {
        static string sSheetName = null;
        static string sConnection = null;
        static DataTable dtTablesList = default(DataTable);
        static OleDbCommand oleExcelCommand = default(OleDbCommand);
        static OleDbDataReader oleExcelReader = default(OleDbDataReader);
        static OleDbConnection oleExcelConnection = default(OleDbConnection);

        public static DataTable GetDataTableFromCsv(string path, bool isFirstRowHeader)
        {
            string header = isFirstRowHeader ? "Yes" : "No";

            string pathOnly = Path.GetDirectoryName(path);
            string fileName = Path.GetFileName(path);

            string sql = @"SELECT * FROM [" + fileName + "]";

            using (OleDbConnection connection = new OleDbConnection(
                @"Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" + pathOnly +
                ";Extended Properties=\"Text;HDR=" + header + "\""))
            using (OleDbCommand command = new OleDbCommand(sql, connection))
            using (OleDbDataAdapter adapter = new OleDbDataAdapter(command))
            {
                DataTable dataTable = new DataTable();
                dataTable.Locale = CultureInfo.CurrentCulture;
                adapter.Fill(dataTable);
                return dataTable;
            }
        }

        public static DataTable ConvertCSVtoDataTable(string strFilePath)
        {
            DataTable dt = new DataTable();
            using (StreamReader sr = new StreamReader(strFilePath))
            {
                string[] headers = sr.ReadLine().Split(',');
                foreach (string header in headers)
                {
                    dt.Columns.Add(header);
                }
                while (!sr.EndOfStream)
                {
                    string[] rows = sr.ReadLine().Split(',');
                    DataRow dr = dt.NewRow();
                    for (int i = 0; i < headers.Length; i++)
                    {
                        dr[i] = rows[i];
                    }
                    dt.Rows.Add(dr);
                }

            }


            return dt;
        }

        public static DataTable ExcelToDataTable1(string pathName, string sheetName)
        {
            DataTable tbContainer = new DataTable();
            string strConn = string.Empty;
            if (string.IsNullOrEmpty(sheetName)) { sheetName = "Sheet1"; }
            FileInfo file = new FileInfo(pathName);
            if (!file.Exists) { throw new Exception("Error, file doesn't exists!"); }
            string extension = file.Extension;
            switch (extension)
            {
                case ".xls":
                    strConn = "Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" + pathName + ";Extended Properties='Excel 8.0;HDR=Yes;IMEX=1;'";
                    break;
                case ".xlsx":
                    strConn = "Provider=Microsoft.ACE.OLEDB.12.0;Data Source=" + pathName + ";Extended Properties='Excel 12.0;HDR=Yes;IMEX=1;'";
                    break;
                default:
                    strConn = "Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" + pathName + ";Extended Properties='Excel 8.0;HDR=Yes;IMEX=1;'";
                    break;
            }
            OleDbConnection cnnxls = new OleDbConnection(strConn);
            OleDbDataAdapter oda = new OleDbDataAdapter(string.Format("select * from [{0}$]", sheetName), cnnxls);
            DataSet ds = new DataSet();
            oda.Fill(tbContainer);
            return tbContainer;
        }

        public static DataTable ExcelToDataTable(string sourceFileLocation)
        {
            DataTable dt = new DataTable();
            sConnection = "Provider=Microsoft.ACE.OLEDB.12.0;Data Source=" + sourceFileLocation + ";Extended Properties=\"Excel 12.0;HDR=No;IMEX=1\"";
            
            oleExcelConnection = new OleDbConnection(sConnection);
            oleExcelConnection.Open();
            
            dtTablesList = oleExcelConnection.GetSchema("Tables");
            
            if (dtTablesList.Rows.Count > 0)
            {
                sSheetName = dtTablesList.Rows[0]["TABLE_NAME"].ToString();
            }
            
            dtTablesList.Clear();
            dtTablesList.Dispose();
            
            
            if (!string.IsNullOrEmpty(sSheetName))
            {
                oleExcelCommand = oleExcelConnection.CreateCommand();
                oleExcelCommand.CommandText = "Select * From [" + sSheetName + "]";
                oleExcelCommand.CommandType = CommandType.Text;
            
            
                using (OleDbDataAdapter da = new OleDbDataAdapter())
                {
                    da.SelectCommand = oleExcelCommand;
                    da.Fill(dt);
                    return dt;
                }
            }
            oleExcelConnection.Close();
            
            return new DataTable();
        }

        public static void TestFileOpen(string sourceFileLocation)
        {
            sConnection = "Provider=Microsoft.ACE.OLEDB.12.0;Data Source=" + sourceFileLocation + ";Extended Properties=\"Excel 12.0;HDR=No;IMEX=1\"";
            using (oleExcelConnection = new OleDbConnection(sConnection))
            {
                oleExcelConnection.Open();
            }
        }

        public static DataTable ImportExceltoDatatable(string filepath)
        {
            // string sqlquery= "Select * From [SheetName$] Where YourCondition";



            string sqlquery = "Select * From [SheetName$] Where Id='ID_007'";
            DataSet ds = new DataSet();
            string constring = @"Provider=Microsoft.ACE.OLEDB.12.0;Data Source=" + filepath + ";Extended Properties=\"Excel 12.0;HDR=YES;\"";
            OleDbConnection con = new OleDbConnection(constring + "");
            OleDbDataAdapter da = new OleDbDataAdapter(sqlquery, con);
            da.Fill(ds);
            DataTable dt = ds.Tables[0];
            return dt;
        }
    }
}
