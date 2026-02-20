using Cognex.InSight.Web;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using VisionCore;
using VisionCore.Models;
using VisionCore.ViewModels;
using VisionCore.Views;

public class MainViewModel : ViewModelBase
{

    //Config Model
    public ConfigModel Settings => ConfigModel.Instance;
    //Cmaera Control Model
    public CameraControlModel controlModel => CameraControlModel.Instance;
    public FileManagerModel fileManagerModel => FileManagerModel.Instance;

    public DisplayViewModel DisplayVM { get; set; } = new DisplayViewModel();

    public LoggerService loggerService => LoggerService.Instance;


    public ICommand ConnectCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand OpenJobCommand { get; }

    public ICommand OpenConfigCommand { get; }

    public MainViewModel()
    {
        ConnectCommand = new RelayCommand(async o => await ExecuteConnect());

        CloseCommand = new RelayCommand(async o => await ExecuteClose());

        OpenConfigCommand = new RelayCommand(o => ExecuteOpenConfig());
        OpenJobCommand = new RelayCommand(async o => await ExcuteLoadJob());

    }

    private async Task ExecuteConnect()
    {
        try
        {

            Logger.Info($"센서 연결 시도 중...{Settings.IP}:{Settings.Port}");

            await controlModel.ConnectAsync(controlModel.IsInSightSensor, $"{Settings.IP}:{Settings.Port}", $"{Settings.User}", $"{Settings.Password}");

            if (controlModel.IsInSightSensor.Connected)
            {
                // 카메라 Online 상태 동기화
                IsOnline = controlModel.IsInSightSensor.Online;
                OnPropertyChanged(nameof(controlModel.IsInSightSensor));

                Logger.Success($"센서 연결 성공!");

            }
        }
        catch (Exception ex)
        {
            Logger.Error($"연결 실패: {ex.Message}"); // 에러 로그
            System.Windows.MessageBox.Show($"연결 실패: {ex.Message}");
        }
    }


    private async Task ExecuteClose()
    {
        if (controlModel.IsInSightSensor != null && controlModel.IsInSightSensor.Connected)
        {
            Logger.Info("센서 연결 해제 중...");
            await controlModel.DisconnectAsync(controlModel.IsInSightSensor);
            Logger.Info("센서 연결 해제 완료.");
        }
        System.Windows.Application.Current.Shutdown();
    }

    private bool _isOnline;
    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            _isOnline = value;

            OnPropertyChanged(nameof(IsOnline));

            ChangeOnlineStateAsync(_isOnline);

        }
    }
    private async void ChangeOnlineStateAsync(bool targetValue)
    {
        if (controlModel.IsInSightSensor == null || !controlModel.IsInSightSensor.Connected)
        {
            Logger.Warning("연결된 센서가 없어 상태를 변경할 수 없습니다.");


            _isOnline = !targetValue;
            OnPropertyChanged(nameof(IsOnline));
            return;
        }

        try
        {
            var result = await controlModel.IsInSightSensor.SetSoftOnlineAsync(targetValue);

            if (targetValue)
                Logger.Success("센서 상태: [ONLINE]");
            else
                Logger.Info("센서 상태: [OFFLINE]");

        }
        catch (Exception ex)
        {
            Logger.Error($"상태 변경 오류: {ex.Message}");

            if (targetValue)
            {
                _isOnline = !targetValue;
            }

            OnPropertyChanged(nameof(IsOnline));
        }
    }



    private void ExecuteOpenConfig()
    {
        try
        {
            Logger.Info("시스템 설정 창을 엽니다.");

            var configWin = new VisionCore.Views.ConfigWindow();
            var configVM = new VisionCore.ViewModels.ConfigViewModel();

            configWin.DataContext = configVM;
            configWin.Owner = System.Windows.Application.Current.MainWindow; // 부모 창 중앙에 띄우기

            bool? result = configWin.ShowDialog(); // 설정을 다 하고 닫을 때까지 대기

            if (result == true || !configWin.IsVisible)
            {
                Logger.Info("설정 창이 닫혔습니다.");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("설정 창을 여는 중 오류 발생: " + ex.Message);
        }
    }

    private async Task ExcuteLoadJob()
    {
        try
        {
            Logger.Info("JobFile Explorer Open");

            var win = new JobBrowserWindow();
            var vm = new JobExplorerViewModel();

            FileItem fileitem = new FileItem();

            win.DataContext = vm;
            win.Owner = System.Windows.Application.Current.MainWindow;

            bool? result = win.ShowDialog();

            if (result == true || !win.IsVisible)
            {
                Logger.Info("JobFile Explorer Close");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("JobFile Explorer Open Error: " + ex.Message);
        }


    }


}