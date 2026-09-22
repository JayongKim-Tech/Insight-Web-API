using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using VisionCore.Models;
using VisionCore.ViewModels;

public class MainViewModel : ViewModelBase
{
    //Config Model (검사 Point 등 앱 공통 설정)
    public ConfigModel Settings => ConfigModel.Instance;

    public LoggerService logger => LoggerService.Instance;

    public ICommand ConnectCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand OpenConfigCommand { get; }
    public ICommand ScanDevicesCommand { get; }
    public ICommand RemoveSensorCommand { get; }
    public ICommand ArrangeTilesCommand { get; }

    public ObservableCollection<DiscoveredDevice> Devices { get; set; } = new ObservableCollection<DiscoveredDevice>();

    // 연결된 센서 = 화면에 표시되는 타일 목록 (자유 배치, 드래그/리사이즈 가능)
    public ObservableCollection<SensorSessionViewModel> Sensors { get; } = new ObservableCollection<SensorSessionViewModel>();

    private DiscoveredDevice _selectedDevice;
    public DiscoveredDevice SelectedDevice
    {
        get => _selectedDevice;
        set { _selectedDevice = value; OnPropertyChanged(); }
    }

    public MainViewModel()
    {
        ConnectCommand = new RelayCommand(async o => await ExecuteConnect());
        CloseCommand = new RelayCommand(async o => await ExecuteClose());
        OpenConfigCommand = new RelayCommand(o => ExecuteOpenConfig());
        ScanDevicesCommand = new RelayCommand(async o => await ScanDevices());
        RemoveSensorCommand = new RelayCommand(async o => await RemoveSensorAsync(o as SensorSessionViewModel));
        ArrangeTilesCommand = new RelayCommand(o => ArrangeTiles());
    }

    private async Task ScanDevices()
    {
        Devices.Clear();

        var discoveredDevices = await CameraControlModel.ScanDevicesAsync();

        foreach (var device in discoveredDevices)
        {
            Devices.Add(device);
        }

        if (Devices.Count > 0)
        {
            SelectedDevice = Devices[0];
            Logger.Info($"{Devices.Count}개의 장치를 찾았습니다.");
        }
        else
        {
            Logger.Info("No Devices");
        }
    }

    private async Task ExecuteConnect()
    {
        if (SelectedDevice == null)
        {
            Logger.Warning("연결할 장치를 먼저 선택하세요.");
            return;
        }

        if (Sensors.Any(s => s.Device.IpAddress == SelectedDevice.IpAddress && s.Device.Port == SelectedDevice.Port))
        {
            Logger.Warning($"[{SelectedDevice.DisplayName}] 이미 연결된 센서입니다.");
            return;
        }

        var session = new SensorSessionViewModel(SelectedDevice);
        bool connected = await session.ConnectAsync();

        if (connected)
        {
            Sensors.Add(session);
            ArrangeTile(session, Sensors.Count - 1);
        }
    }

    // 타일 1개의 위치/크기를 격자 자리에 맞게 재배치 (이후 사용자가 자유롭게 드래그/리사이즈 가능)
    private void ArrangeTile(SensorSessionViewModel session, int index)
    {
        const int columns = 2;

        session.IsMaximized = false;
        session.ZIndex = 0;
        session.Width = 480;
        session.Height = 420;
        session.X = (index % columns) * session.Width;
        session.Y = (index / columns) * session.Height;
    }

    // 모든 타일을 격자 형태로 다시 정렬 (드래그/리사이즈로 흐트러졌을 때 되돌리는 용도)
    private void ArrangeTiles()
    {
        for (int i = 0; i < Sensors.Count; i++)
        {
            ArrangeTile(Sensors[i], i);
        }
    }

    private async Task RemoveSensorAsync(SensorSessionViewModel session)
    {
        if (session == null) return;

        await session.DisconnectAsync();
        Sensors.Remove(session);
    }

    private async Task ExecuteClose()
    {
        foreach (var session in Sensors.ToList())
        {
            await session.DisconnectAsync();
        }
        System.Windows.Application.Current.Shutdown();
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
            Logger.Error("설정 창을 여는 중 오류 발생: " + ex.ToString());
        }
    }
}
