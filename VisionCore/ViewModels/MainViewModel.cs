using Cognex.InSight.Web;
using Cognex.InSight.Web.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using VisionCore.Models;

namespace VisionCore.ViewModels
{
    public class MainViewModel : ViewModelBase
    {

        public DisplayViewModel DisplayVM { get; set; } = new DisplayViewModel();

        private CvsInSight _isInSightSensor = new CvsInSight();

        public CvsInSight IsInSightSensor
        {
            get { return _isInSightSensor; }
            set { _isInSightSensor = value; OnPropertyChanged(); }
        }

        public ICommand ConnectCommand { get; }

        public MainViewModel()
        {
            ConnectCommand = new RelayCommand(async o =>
            {
                var model = new CameraControl();
                await model.ConnectAsync(IsInSightSensor, "127.0.0.1:52494", "admin", "");

                if (IsInSightSensor.Connected)
                {
                    OnPropertyChanged(nameof(IsInSightSensor));
                }
            });
        }
    }
}
