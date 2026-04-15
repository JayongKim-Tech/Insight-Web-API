using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using VisionCore.Models;
using VisionCore.Properties;

namespace VisionCore.ViewModels
{
    public class ConfigViewModel : ViewModelBase
    {
        public ConfigModel Settings => ConfigModel.Instance;

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
