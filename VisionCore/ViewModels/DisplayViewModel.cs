using Cognex.InSight.Web;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionCore.ViewModels
{
    public class DisplayViewModel : ViewModelBase
    {

        private bool _isGridVisible = false;
        public bool IsGridVisible
        {
            get { return _isGridVisible; }
            set { _isGridVisible = value; OnPropertyChanged(); }
        }

    }
}
