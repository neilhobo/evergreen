using Globeweigh.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Globeweigh.Shared.Helpers
{
    public static class ErrorLogging
    {
        public static async void LogError(Exception ex, string customMessage)
        {
            try
            {
                ErrorLog errorLog = new ErrorLog();
                errorLog.ComputerName = Environment.MachineName;
                if (GlobalVariables.CurrentDevice != null)
                    errorLog.DeviceId = GlobalVariables.CurrentDevice.id;
                errorLog.TimeOfError = DateTime.Now;
                errorLog.Message = GetMessage(ex);
                errorLog.CustomMessage = customMessage;
                await errorLog.Save(errorLog);
            }
            catch
            {
            }
        }

        static string GetMessage(Exception ex)
        {
            var result = new StringBuilder();
            if (Assembly.GetEntryAssembly() != null)
            {
                result.Append("EntryAssembly: ");
                result.Append(Assembly.GetEntryAssembly().Location);
                result.AppendLine();
            }
            result.AppendLine("UnhandledException:");
            PackException(ex, result);

            return result.ToString();
        }
        static void PackException(Exception ex, StringBuilder stringBuilder, int index = 0)
        {
            AppendLine(stringBuilder, ex.Message, index);
            if (!string.IsNullOrWhiteSpace(ex.StackTrace))
            {
                AppendLine(stringBuilder, "StackTrace:", index);
                AppendLine(stringBuilder, ex.StackTrace, index);
            }
            if (ex.InnerException != null)
            {
                AppendLine(stringBuilder, "InnerException:", index);
                PackException(ex.InnerException, stringBuilder, ++index);
            }
        }
        static void AppendLine(StringBuilder stringBuilder, string text, int index)
        {
            string tabOffset = new string('\t', index);
            stringBuilder.Append(tabOffset);
            var regex = new Regex("\r\n*\\s");
            stringBuilder.AppendLine(regex.Replace(text, "\r\n" + tabOffset));
        }
    }
}
