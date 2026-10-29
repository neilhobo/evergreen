using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using Globeweigh.Shared;
using MvvmDialogs;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Model;
using Globeweigh.Shared;



namespace Globeweigh.Admin
{
    public class DownloadNewUpdateViewModel : DialogViewModelBase, IViewModel, IModalDialogViewModel
    {
        private WebClient _webClient;

        #region Properties

        private int _ProgressValue;
        public int ProgressValue
        {
            get { return _ProgressValue; }
            set { Set(ref _ProgressValue, value); }
        }

        private string _BytesProgress;
        public string BytesProgress
        {
            get { return _BytesProgress; }
            set { Set(ref _BytesProgress, value); }
        }

        private bool _DownloadComplete;
        public bool DownloadComplete
        {
            get { return _DownloadComplete; }
            set { Set(ref _DownloadComplete, value); }
        }

        public string NewReleaseFullFileName { get; set; }
        public string NewReleaseFileName { get; set; }

        #endregion

        #region Commands

        public RelayCommand InstallCommand => new RelayCommand(OnInstall);


        #endregion

        public async void Load(FrameworkElement element)
        {
            try
            {
                //UtilitiesShared.DeleteFilesWithExtension(GlobalVariables.CommonApplicationDataFolder, "msi");
                NewReleaseFileName = Path.GetFileName(NewReleaseFullFileName);

                _webClient = new WebClient();
                _webClient.DownloadProgressChanged += WebClient_DownloadProgressChanged;
                _webClient.DownloadFileCompleted += WebClient_DownloadFileCompleted;
                _webClient.DownloadFileAsync(new Uri(NewReleaseFullFileName), GlobalVariables.CommonApplicationDataFolder + NewReleaseFileName);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

        }

        private void WebClient_DownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            DownloadComplete = true;
            OnInstall();
        }


        private void WebClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            try
            {
                ProgressValue = e.ProgressPercentage;
                BytesProgress = ((e.BytesReceived / 1024f) / 1024f).ToString("#.0") + " / " + ((e.TotalBytesToReceive / 1024f) / 1024f).ToString("#.0") + "Mb";
            }
            catch (Exception exception)
            {
                ExceptionHelper.ShowErrorWindow(exception, "WebClient_DownloadProgressChanged", false);
                DownloadComplete = true;
            }

        }

        private async void OnInstall()
        {
            string installerFilePath;
            installerFilePath = GlobalVariables.CommonApplicationDataFolder + NewReleaseFileName;
            Process installerProcess = System.Diagnostics.Process.Start(installerFilePath, "/passive");
            Application.Current.Shutdown();
            DialogResult = true;
        }

        public override RelayCommand CancelCommand
        {
            get
            {
                return _CancelCommand ?? (_CancelCommand = new RelayCommand(() =>
                {
                    _webClient.CancelAsync();
                    DialogResult = false;
                }));
            }
        }


        public void Unload(FrameworkElement element)
        {
            DialogResult = null;
            DownloadComplete = false;
            _webClient.DownloadProgressChanged -= WebClient_DownloadProgressChanged;
            _webClient.DownloadFileCompleted -= WebClient_DownloadFileCompleted;
            _webClient = null;
            ProgressValue = 0;
            BytesProgress = string.Empty;
            NewReleaseFileName = null;

        }
    }
}
