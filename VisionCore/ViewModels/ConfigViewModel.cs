using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using VisionCore.Models;

namespace VisionCore.ViewModels
{
    public class ConfigViewModel : ViewModelBase
    {
        public Models.ConfigModel Settings => Models.ConfigModel.Instance;

        public ICommand SaveCommand { get; }

        public ConfigViewModel()
        {
            LoadConfig();
            SaveCommand = new RelayCommand(o => ExecuteSave());
        }

        private void LoadConfig()
        {
            Settings.Load();
        }

        private void ExecuteSave()
        {
            Settings.Save();
        }
    }
}
