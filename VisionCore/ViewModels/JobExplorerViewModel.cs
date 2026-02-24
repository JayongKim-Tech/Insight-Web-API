using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using VisionCore.Models;

namespace VisionCore.ViewModels
{
    public class JobExplorerViewModel : ViewModelBase
    {
        public ObservableCollection<FileItem> Files { get; } = new ObservableCollection<FileItem>();
        public FileManagerModel fileManager => FileManagerModel.Instance;
        public CameraControlModel CameraControl => CameraControlModel.Instance;


        private FileItem _selectedFile;
        public FileItem SelectedFile
        {
            get => _selectedFile;
            set { _selectedFile = value; OnPropertyChanged(); }
        }

        private int _selectedLocationIndex = 0; // 0: PC, 1: Sensor
        public int SelectedLocationIndex
        {
            get => _selectedLocationIndex;
            set
            {
                _selectedLocationIndex = value;
                RefreshFileList();
                OnPropertyChanged();
            }
        }

        public string CurrentDisplayPath { get; set; }

        public ICommand OpenCommand { get; }
        public Action<bool> CloseAction { get; set; }

        public JobExplorerViewModel()
        {
            OpenCommand = new RelayCommand(o => {
                if (SelectedFile != null) CloseAction?.Invoke(true);
            });

            RefreshFileList(); // 첫 실행 시 PC 파일 로드
        }

        private async void RefreshFileList()
        {
            Files.Clear();
            CurrentDisplayPath =  AppDomain.CurrentDomain.BaseDirectory;
            var sensor = CameraControl.IsInSightSensor;
            var newList = await fileManager.GetFileListAsync(SelectedLocationIndex, sensor, CurrentDisplayPath);


            foreach (var item in newList) Files.Add(item);

            OnPropertyChanged(nameof(CurrentDisplayPath));
        }


    }
}
