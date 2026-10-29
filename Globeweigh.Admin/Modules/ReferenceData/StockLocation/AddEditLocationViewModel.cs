using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;

namespace Globeweigh.Admin
{
    public class AddEditLocationViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields


        private readonly IStockLocationRepository _stockLocationRepo = SimpleIoc.Default.GetInstance<IStockLocationRepository>();

        #endregion

        #region Properties

        public int LocationId { get; set; }

        private StockLocation _CurrentStockLocation;
        public StockLocation CurrentStockLocation
        {
            get { return _CurrentStockLocation; }
            private set { Set(ref _CurrentStockLocation, value); }
        }

        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion

        public async void Load(FrameworkElement element)
        {
            if (LocationId == 0)
            {
                CurrentStockLocation = new StockLocation();
                HeaderText = "Add New Location";
            }
            else
            {
                CurrentStockLocation = await _stockLocationRepo.GetStockLocation(LocationId);
                HeaderText = "Edit Location";
            }
        }


        private async void OnSave()
        {
            await _stockLocationRepo.AddUpdateStockLocationAsync(CurrentStockLocation);
            DialogResult = true;
        }

        private bool CanSave()
        {
            if (CurrentStockLocation == null) return false;
            return true;
        }

        public void Unload(FrameworkElement element)
        {
            CurrentStockLocation = null;
            LocationId = 0;
            DialogResult = null;
        }
    }
}
