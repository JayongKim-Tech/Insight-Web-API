using Cognex.InSight.Web;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VisionCore.Models;

namespace VisionCore.ViewModels
{
    public class DisplayViewModel : ViewModelBase
    {
        public CameraControlModel CameraControl => CameraControlModel.Instance;
        private bool _isGridVisible = false;

        public bool IsGridVisible
        {
            get { return _isGridVisible; }
            set
            {
                if (_isGridVisible == value) return;

                _isGridVisible = value;
                OnPropertyChanged();

                if (_isGridVisible)
                {
                    Logger.Info("스프레드시트 편집 창을 표시합니다.");
                }
                else
                {
                    Logger.Info("스프레드시트 편집 창을 숨깁니다.");
                }
            }
        }

        private bool _isGraphicVisible = true;
        public bool IsGraphicVisible
        {
            get { return _isGraphicVisible; }
            set
            {
                if (_isGraphicVisible == value) return;

                _isGraphicVisible = value;
                OnPropertyChanged();

                if (_isGraphicVisible)
                {
                    CameraControl.cvsDisplay.ToggleOverlay(_isGraphicVisible);
                    Logger.Info("그래픽을 표시합니다.");
                }
                else
                {
                    CameraControl.cvsDisplay.ToggleOverlay(_isGraphicVisible);
                    Logger.Info("그래픽을 숨깁니다.");
                }
            }
        }

    }
}
