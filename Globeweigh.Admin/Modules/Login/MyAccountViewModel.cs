using System;
using System.IO;
using System.Linq;
using MvvmDialogs;
using System.Windows;
using System.Windows.Controls;
// using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;


namespace Globeweigh.Admin
{
    public class MyAccountViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IGlobeweighUserRepository _GlobeweighUserRepo = SimpleIoc.Default.GetInstance<IGlobeweighUserRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private GlobeweighEntities _context = new GlobeweighEntities(GlobalVariables.ConnectionString);

        #endregion

        #region Properties

        // public int GlobeweighUserId { get; set; }

        private GlobeweighUser _CurrentGlobeweighUser;
        public GlobeweighUser CurrentGlobeweighUser
        {
            get { return _CurrentGlobeweighUser; }
            private set { Set(ref _CurrentGlobeweighUser, value); }
        }

        private string _NewPassword;
        public string NewPassword
        {
            get { return _NewPassword; }
            set
            {
                Set(ref _NewPassword, value);
                if (NewPassword != null)
                {
                    HasChanges = true;
                }

                if (string.IsNullOrEmpty(NewPassword)) HasChanges = false;
            }
        }

        private string _OldPassword;
        public string OldPassword
        {
            get { return _OldPassword; }
            set { Set(ref _OldPassword, value); }
        }

        private Byte[] _CurrentSignatureByte;
        public Byte[] CurrentSignatureByte
        {
            get { return _CurrentSignatureByte; }
            set { Set(ref _CurrentSignatureByte, value); }
        }

        private StrokeCollection _CurrentStrokesCollection;
        public StrokeCollection CurrentStrokesCollection
        {
            get { return _CurrentStrokesCollection; }
            set { Set(ref _CurrentStrokesCollection, value); }
        }

        private bool _Saved;
        public bool Saved
        {
            get { return _Saved; }
            set { Set(ref _Saved, value); }
        }

        #endregion

        #region Commands

        public RelayCommand SavePasswordCommand => new RelayCommand(OnSavePassword, CanSavePassword);
        public RelayCommand ClearCommand => new RelayCommand(OnClear);
        // public RelayCommand SaveSignatureCommand => new RelayCommand(OnSaveSignature, CanSaveSignature);
        public RelayCommand<object> SaveSignatureCommand => new RelayCommand<object>(OnSaveSignature, CanSaveSignature());

        #endregion


        private async void OnClear()
        {
            CurrentStrokesCollection = new StrokeCollection();
        }

        private async void OnSaveSignature(object param)
        {
            InkCanvas igSignature = (InkCanvas)param;
            CurrentGlobeweighUser.SignatureImage = ConvertInkCanvasToByteArray(igSignature);

            using (MemoryStream ms = new MemoryStream())
            {
                CurrentStrokesCollection.Save(ms);
                ms.Position = 0;
                CurrentStrokesCollection = new System.Windows.Ink.StrokeCollection(ms);
                CurrentGlobeweighUser.Signature = ms.ToArray();
            }

            await _GlobeweighUserRepo.AddUpdateGlobeweighUserAsync(CurrentGlobeweighUser);
            Saved = true;
            HasChanges = false;
        }

        private byte[] ConvertInkCanvasToByteArray(InkCanvas icSignature)
        {
            var rect = new Rect(icSignature.RenderSize);
            var visual = new DrawingVisual();

            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new VisualBrush(icSignature), null, rect);
            }

            var rtb = new RenderTargetBitmap(
                (int)rect.Width, (int)rect.Height, 96d, 96d, PixelFormats.Default);
            rtb.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }



        private bool CanSaveSignature()
        {
            if (CurrentStrokesCollection == null) return false;
            return true;
        }

        public async void Load(FrameworkElement element)
        {


            CurrentGlobeweighUser = await _GlobeweighUserRepo.GetGlobeweighUserAsync(GlobalVariables.CurrentGlobeweighUserId);
            if (CurrentGlobeweighUser.Signature != null)
            {

                using (MemoryStream ms = new MemoryStream(CurrentGlobeweighUser.Signature))
                {
                    ms.Position = 0;
                    CurrentStrokesCollection = new StrokeCollection(ms);
                }
            }
            else
            {
                CurrentStrokesCollection = new StrokeCollection();
            }

            CurrentGlobeweighUser = GlobalVariables.CurrentGlobeweighUser;
            CurrentGlobeweighUser.PropertyChanged += CurrentGlobeweighUser_PropertyChanged;
        }

        private void CurrentGlobeweighUser_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (!HasChanges) HasChanges = true;
        }

        private async void OnSavePassword()
        {
            await _GlobeweighUserRepo.UpdatePassword(CurrentGlobeweighUser.id, NewPassword, CurrentGlobeweighUser.Passcode);
            Saved = true;
            HasChanges = false;
        }

        private bool CanSavePassword()
        {
            if (!HasChanges) return false;
            if (!string.IsNullOrEmpty(NewPassword))
            {
                if (NewPassword.Contains(' '))
                {
                    return false;
                }
                if (CurrentGlobeweighUser.Password != OldPassword) return false;
            }
            if (!string.IsNullOrEmpty(CurrentGlobeweighUser.Passcode))
            {
                if (CurrentGlobeweighUser.Passcode.Length != 4) return false;
            }
            return true;
        }

        public void Unload(FrameworkElement element)
        {
            Saved = false;
            HasChanges = false;
            CurrentGlobeweighUser.PropertyChanged -= CurrentGlobeweighUser_PropertyChanged;
            NewPassword = null;
            OldPassword = null;
            CurrentGlobeweighUser = null;
            // GlobeweighUserId = 0;
            DialogResult = null;
        }
    }
}
