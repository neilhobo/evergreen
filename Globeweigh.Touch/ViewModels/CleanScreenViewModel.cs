using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Shared.Services;
using Globeweigh.Model;
using Globeweigh.Shared;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Shared.Helpers;
using System;
using System.Windows.Threading;
using MvvmDialogs;

namespace Globeweigh.Touch
{
    public class CleanScreenViewModel : BindableBase, IViewModel
    {
        private DispatcherTimer _timer;
        private int _timeLeft;

        private string _CountdownText;
        public string CountdownText
        {
            get { return _CountdownText; }
            set { Set(ref _CountdownText, value); }
        }

        public CleanScreenViewModel()
        {

        }

        private void timer_tick(object sender, EventArgs e)
        {
            if (_timeLeft > 0)
            {
                _timeLeft -= 1;
                CountdownText = _timeLeft.ToString();
            }
            else
            {
                _timer.Stop();
                SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel =SimpleIoc.Default.GetInstance<HomeViewModel>();
            }
        }

        public async void Load(FrameworkElement element)
        {
            if (UtilitiesShared.InDesignMode) return;
            _timeLeft = 25;
            CountdownText = _timeLeft.ToString();
            _timer = new DispatcherTimer();
            _timer.Interval = new TimeSpan(0, 0, 0, 1);
            _timer.Tick += timer_tick;
            _timer.Start();
        }

        public void Unload(FrameworkElement element)
        {
            _timer.Tick -= timer_tick;
            _timer = null;
        }
    }
}
