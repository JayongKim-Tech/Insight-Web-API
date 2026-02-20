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

            // 1. 모델에게 데이터 요청
            var sensor = CameraControl.IsInSightSensor;
            var newList = await fileManager.GetFileListAsync(SelectedLocationIndex, sensor);

            // 2. 리스트 업데이트
            foreach (var item in newList) Files.Add(item);

            // 3. 경로 텍스트 업데이트 (여기서 직접 수정해야 UI에 반영됨)
            CurrentDisplayPath = (SelectedLocationIndex == 0)
                ? "내 PC > 작업 폴더"
                : "비전 센서 > Flash 메모리";

            // UI에 경로가 바뀌었다고 알려줌
            OnPropertyChanged(nameof(CurrentDisplayPath));
        }


    }
}
