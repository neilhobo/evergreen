using System.Collections.Generic;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using Globeweigh.Model;
using MvvmDialogs;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;


namespace Globeweigh.Touch
{
    public class SelectHaulierViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IHaulierRepository _haulierRepo = SimpleIoc.Default.GetInstance<IHaulierRepository>();

        #endregion

        #region Properties

        private List<Haulier> _HaulierList;
        public List<Haulier> HaulierList
        {
            get { return _HaulierList; }
            set { Set(ref _HaulierList, value); }
        }

        private Haulier _SelectedHaulier;
        public Haulier SelectedHaulier
        {
            get { return _SelectedHaulier; }
            set { Set(ref _SelectedHaulier, value); }
        }


        #endregion

        #region Commands

        public RelayCommand SelectCommand => new RelayCommand(OnSelect, CanSelect);

        #endregion

        private bool CanSelect()
        {
            if (SelectedHaulier == null) return false;
            return true;
        }

        private async void RefreshProducts()
        {
            HaulierList = new List<Haulier>(await _haulierRepo.GetHaulierList());
        }

        public async void Load(FrameworkElement element)
        {
            RefreshProducts();
        }
        private async void OnSelect()
        {
            DialogResult = true;
        }

        private void OnCancel()
        {
            DialogResult = false;
        }

        public void Unload(FrameworkElement element)
        {
            HaulierList = null;
            SelectedHaulier = null;
            DialogResult = null;
        }
    }
}
