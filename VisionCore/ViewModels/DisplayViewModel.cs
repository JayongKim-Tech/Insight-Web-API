using Cognex.InSight.Web;
using Cognex.InSight.Web.Controls;
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
        private bool _isGridVisible = false;

        public bool IsGridVisible
        {
            get { return _isGridVisible; }
            set
            {
                if (_isGridVisible == value) return;

                _isGridVisible = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsVideoVisible));

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

        // Grid 모드일 때는 영상 대신 그리드가 타일 전체를 차지함 (오버레이가 아니라 화면 전환)
        public bool IsVideoVisible => !IsGridVisible;

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
                    Logger.Info("그래픽을 표시합니다.");
                }
                else
                {
                    Logger.Info("그래픽을 숨깁니다.");

                }
            }
        }


    }
}
