using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Globeweigh.Model;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using MahApps.Metro.Controls;
using Microsoft.Win32;

namespace Globeweigh.Shared.Helpers
{
    public static class UtilitiesShared
    {
        public static MetroWindow MainWindow { get; set; }
        public static readonly bool InDesignMode = System.ComponentModel.DesignerProperties.GetIsInDesignMode(new DependencyObject());
        public static bool IsMyMachine => Environment.MachineName == "NEIL-XEON" || Environment.MachineName == "NEIL-XPS15";

        public static void CopyPropertiesTo<T, TU>(this T source, TU dest)
        {
            var sourceProps = typeof(T).GetProperties().Where(x => x.CanRead).ToList();
            var destProps = typeof(TU).GetProperties()
                .Where(x => x.CanWrite)
                .ToList();

            foreach (var sourceProp in sourceProps)
            {
                if (destProps.Any(x => x.Name == sourceProp.Name))
                {
                    var p = destProps.First(x => x.Name == sourceProp.Name);
                    if (p.CanWrite)
                    { // check if the property can be set or no.
                        p.SetValue(dest, sourceProp.GetValue(source, null), null);
                    }
                }

            }

        }

        public static System.Boolean IsNumeric(System.Object Expression)
        {
            if (Expression == null || Expression is DateTime)
                return false;

            if (Expression is Int16 || Expression is Int32 || Expression is Int64 || Expression is Decimal || Expression is Single || Expression is Double || Expression is Boolean)
                return true;

            try
            {
                if (Expression is string)
                    Double.Parse(Expression as string);
                else
                    Double.Parse(Expression.ToString());
                return true;
            }
            catch { } // just dismiss errors but return false
            return false;
        }

        public static bool CheckForOtherInstanceOfApplication()
        {
            if (IsMyMachine)
            {
                return false;
            }
            Process proc = Process.GetCurrentProcess();
            int count = Process.GetProcesses().Count(p => p.ProcessName == proc.ProcessName);
            if (count > 1)
            {
//                MessageBox.Show("Already an instance is running...");
                Application.Current.Shutdown();
                return true;
            }
            return false;
        }

        public static void SetModelDialogToOwnerSize(Window modalWindow, Window ownerWindow)
        {
            if (IsMyMachine)
            {
                modalWindow.Height = ownerWindow.ActualHeight;
                modalWindow.Width = ownerWindow.ActualWidth;
                modalWindow.Left = ownerWindow.Left;
                modalWindow.Top = ownerWindow.Top;
            }
        }

        public static void SetModelDialogToOwnerHeight(Window modalWindow, Window ownerWindow, int padding)
        {
            modalWindow.Height = ownerWindow.ActualHeight - padding;
        }

        public async static void RegisterDevice(IDeviceRepository deviceRepo, bool autoStartConfigured)
        {
            try
            {
                var existingDevice = await deviceRepo.GetCurrentDevice();
                if (existingDevice == null)
                {
                    Device newDevice = new Device();
                    newDevice.DeviceName = Environment.MachineName;
                    newDevice.IpAddress = GetLocalIPAddress();
                    newDevice.ApplicationName = System.Reflection.Assembly.GetEntryAssembly().GetName().Name;
                    newDevice.CurrentVersion = GlobalVariables.CurrentMsiVersion;
                    newDevice.DateCreated = DateTime.Now;
                    newDevice.DateOfLastPing = DateTime.Now;
                    newDevice.AutoStartConfigured = autoStartConfigured;
                    await deviceRepo.AddDeviceAsync(newDevice);
                    GlobalVariables.CurrentDevice = newDevice;
                }
                else
                {
                    existingDevice.IpAddress = GetLocalIPAddress();
                    existingDevice.ApplicationName = System.Reflection.Assembly.GetEntryAssembly().GetName().Name;
                    existingDevice.CurrentVersion = GlobalVariables.CurrentMsiVersion;
                    existingDevice.DateOfLastPing = DateTime.Now;
                    existingDevice.DateOfLastRestart = DateTime.Now;
                    existingDevice.AutoStartConfigured = autoStartConfigured;
                    existingDevice.RecycleRequested = false;
                    await deviceRepo.UpdateDeviceAsync(existingDevice);
                    GlobalVariables.CurrentDevice = existingDevice;
                }
            }
            catch (Exception ex)
            {
                ExceptionHelper.LogError(ex, "Register Device");
            }
        }

        public async static Task<bool> PollDevice(IDeviceRepository deviceRepo)
        {
            try
            {
                var existingDevice = await deviceRepo.GetCurrentDevice();
                if (existingDevice == null) return false;
                if (existingDevice.RecycleRequested)
                {
                    existingDevice.RecycleRequested = false;
                    await deviceRepo.UpdateDeviceAsync(existingDevice);
                    return true;
                }
                else
                {
                    existingDevice.DateOfLastPing = DateTime.Now;
                    await deviceRepo.UpdateDeviceAsync(existingDevice);
                    GlobalVariables.CurrentDevice = existingDevice;
                }
            }
            catch
            {
            }
            return false;
        }

        private static string GetPublishedVersion()
        {
            if (System.Deployment.Application.ApplicationDeployment.IsNetworkDeployed)
            {
                return System.Deployment.Application.ApplicationDeployment.CurrentDeployment.
                    CurrentVersion.ToString();
            }
            return "Not network deployed";
        }

        public static string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            return null;
        }

        public static int GetRandomNumber(int numberFrom, int numberTo)
        {
            Random rnd = new Random();
            int randomNumber = rnd.Next(numberFrom, numberTo);
            return randomNumber;
        }

        public static IEnumerable<TSource> DistinctBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
        {
            HashSet<TKey> seenKeys = new HashSet<TKey>();
            foreach (TSource element in source)
            {
                if (seenKeys.Add(keySelector(element)))
                {
                    yield return element;
                }
            }
        }

        public static ImageSource ByteToImage(byte[] imageData)
        {
            BitmapImage biImg = new BitmapImage();
            MemoryStream ms = new MemoryStream(imageData);
            biImg.BeginInit();
            biImg.StreamSource = ms;
            biImg.EndInit();

            ImageSource imgSrc = biImg;

            return imgSrc;
        }

        public static string CheckforUpdate(string currentVersion, string fileSetupStartsWith, out string newVersion)
        {
            try
            {
                newVersion = null;
//                if (IsMyMachine) return null;
                string searchPattern = fileSetupStartsWith +"*.*";  // This would be for you to construct your prefix

                DirectoryInfo di = new DirectoryInfo(GlobalVariables.GlobalDbSettings.UpdateUrl);
                var file = di.GetFiles(searchPattern).LastOrDefault();
                if (file == null) return null;


                string fileWithoutExtension = Path.GetFileNameWithoutExtension(file.Name).Trim();
                string releaseVersion = fileWithoutExtension.Replace(fileSetupStartsWith + "-","");
                newVersion = releaseVersion;

                int result = String.Compare(releaseVersion, currentVersion, StringComparison.Ordinal);
                if (result > 0)
                {
                    return file.FullName;
                }
            }
            catch (Exception e)
            {

            }
            newVersion = null;
            return null;
        }

        public static string GetCurrentMsiVersion()
        {
            try
            {
                var executingAssembly = Assembly.GetCallingAssembly().GetName().Name;
                RegistryKey rKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall");
                List<string> insApplication = new List<string>();

                if (rKey != null && rKey.SubKeyCount > 0)
                {
                    insApplication = rKey.GetSubKeyNames().ToList();
                }

                int i = 0;

                string version = "";
                foreach (string appName in insApplication)
                {
                    RegistryKey finalKey = rKey.OpenSubKey(insApplication[i]);
                    if (finalKey != null)
                    {
                        if (finalKey.GetValue("DisplayName") == null)
                        {
                            i++;
                            continue;
                        }
                        string installedApp = finalKey.GetValue("DisplayName").ToString();
                        Console.Out.WriteLine(installedApp);
                        if (installedApp == executingAssembly)
                        {
                            version = finalKey.GetValue("DisplayVersion").ToString();
                            return version;
                        }
                    }
                    i++;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
            return null;
        }

        public static void DeleteFilesWithExtension(string folderPath, string extension)
        {
            DirectoryInfo di = new DirectoryInfo(folderPath);
            FileInfo[] files = di.GetFiles("*." + extension)
                .Where(p => p.Extension == "." + extension).ToArray();
            foreach (FileInfo file in files)
                try
                {
                    file.Attributes = FileAttributes.Normal;
                    File.Delete(file.FullName);
                }
                catch { }
        }
    }
}
