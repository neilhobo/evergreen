using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using Globeweigh.Model;
using Globeweigh.Shared.Helpers;
using NLog;

namespace Globeweigh.Shared
{
    public static class ExceptionHelper
    {
        private static readonly Logger _logger = LogManager.GetLogger("App");

        static ExceptionHelper()
        {
            IsEnabled = true;
        }
        public static bool IsEnabled { get; set; }
        public static void Initialize()
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;
            Application.Current.DispatcherUnhandledException += Application_DispatcherUnhandledException;
        }
        static DispatcherUnhandledExceptionEventArgs arguments;

        private static void HandleUnhandledException(Exception exception)
        {
            try
            {
                if (exception.Message == "Show SplashScreen before calling this method.")
                    return;

                var frm = new frmError(null, exception);
                frm.ShowDialog();
                if (frm.DialogResult == true)
                {
                    ErrorLogging.LogError(exception, "HandleUnhandledException");
                }
            }
            catch (Exception exc)
            {
                if (exc.GetType() == typeof(System.Windows.Markup.XamlParseException))
                {
                    var result = Regex.Split(exc.InnerException.ToString(), "\r\n|\r|\n");
                    string fileNotFoundLine = result.FirstOrDefault(a => a.Contains("Could not load file or assembly"));
                    if(!string.IsNullOrEmpty(fileNotFoundLine))MessageBox.Show(fileNotFoundLine);
                }
                else
                {
//                    if (exc.InnerException != null) MessageBox.Show(exc.Message.ToString());
                }

                if (exc.Message != null) _logger.ErrorException(exc.Message, exc);
                if (exc.InnerException != null) _logger.ErrorException(exc.InnerException.ToString(), exc);
            }
            finally
            {
            }
        }

        static void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            var exception = e.Exception;
            HandleUnhandledException(exception);
        }

        static void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs unhandledExceptionEventArgs)
        {
            HandleUnhandledException(unhandledExceptionEventArgs.ExceptionObject as Exception);
        }

        public static string GetMessage(Exception ex)
        {
            if (ex == null)
                ex = (Exception)arguments.Exception;

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

        public static async void LogError(Exception ex, string customMessage)
        {
            try
            {
                ErrorLog errorLog = new ErrorLog();
                errorLog.ComputerName = Environment.MachineName;
                if (GlobalVariables.CurrentDevice != null)
                    errorLog.DeviceId = GlobalVariables.CurrentDevice.id;
                errorLog.TimeOfError = DateTime.Now;
                errorLog.Message = ex.Message;
                if (ex.InnerException != null) errorLog.InnerException = ex.InnerException.Message;
                if (GlobalVariables.CurrentGlobeweighUser != null)errorLog.GlobeweighUserId = GlobalVariables.CurrentGlobeweighUser.id;
                errorLog.CustomMessage = customMessage;
                await errorLog.Save(errorLog);
            }
            catch
            {
            }
        }

        private static async void LogError(string message)
        {
            try
            {
                ErrorLog errorLog = new ErrorLog();
                errorLog.ComputerName = Environment.MachineName;
                if (GlobalVariables.CurrentDevice != null)
                    errorLog.DeviceId = GlobalVariables.CurrentDevice.id;
                errorLog.TimeOfError = DateTime.Now;
                errorLog.Message = message;
                await errorLog.Save(errorLog);
            }
            catch
            {
            }
        }

        public static void ShowErrorWindow(Exception exception, string customMessage, bool shutdown)
        {
            try
            {
                if (exception.Message == "Show SplashScreen before calling this method.")
                    return;

                var frm = new frmError(null, exception);
                frm.ShowDialog();
                if (frm.DialogResult == true)
                {
                    ErrorLogging.LogError(exception, customMessage);
                    if (shutdown) Application.Current.Shutdown();
                }
            }
            catch (Exception exc)
            {
                if (exc.Message != null) _logger.ErrorException(exc.Message, exc);
                if (exc.InnerException != null) _logger.ErrorException(exc.InnerException.ToString(), exc);
            }
            finally
            {
            }
        }
    }
}
