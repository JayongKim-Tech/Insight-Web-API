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

    // 하단 Results 탭 (날짜별 CSV 결과 리스트)
    public ResultHistoryViewModel ResultHistory { get; } = new ResultHistoryViewModel();

    // 상단 헤더 디스크 사용량 표시 + 자동 삭제
    public DiskStatusViewModel Disk { get; } = new DiskStatusViewModel();

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

        ResultHistory.RowSelected += ShowRowImageOnSensor;
    }

    // 결과 리스트에서 고른 행의 이미지를 그 센서 타일 디스플레이에 표시
    private void ShowRowImageOnSensor(VisionCore.Models.ResultRow row)
    {
        if (string.IsNullOrEmpty(row.ImagePath)) return;

        var session = FindSessionForRow(row.Sensor);
        if (session == null)
        {
            Logger.Warning($"결과의 센서 '{row.Sensor}' 가 연결돼 있지 않아 이미지를 표시할 수 없습니다.");
            return;
        }

        if (!System.IO.File.Exists(row.ImagePath))
        {
            Logger.Warning($"이미지 파일이 없습니다: {row.ImagePath}");
            return;
        }

        // 먼저 이미지를 바로 띄우고, OFFLINE이면 센서로 재검사해서 그래픽/판정까지 갱신 (ONLINE은 생산 검사 보호를 위해 이미지만)
        session.ShowImage?.Invoke(row.ImagePath);
        if (!session.IsOnline) session.Reinspect(row.ImagePath);
    }

    /// <summary>
    /// 결과 행의 센서 이름 → 연결된 세션. 이름이 그대로 같으면 그 센서,
    /// 아니면 포트를 뺀 주소가 같은 센서 (에뮬레이터는 실행할 때마다 포트가 바뀜),
    /// 그래도 없고 센서가 1대뿐이면 그 센서.
    /// </summary>
    private SensorSessionViewModel FindSessionForRow(string sensorName)
    {
        // 센서 이름 표시 이전에 기록된 행은 "[Emulator] 127.0.0.1:port" 같은 스캔 이름 -> Device.DisplayName 으로도 비교
        var exact = Sensors.FirstOrDefault(s => s.DisplayName == sensorName || s.Device.DisplayName == sensorName);
        if (exact != null) return exact;

        string host = StripPort(sensorName);
        var sameHost = Sensors.Where(s => StripPort(s.DisplayName) == host || StripPort(s.Device.DisplayName) == host).ToList();
        if (sameHost.Count == 1) return sameHost[0];

        // 에뮬레이터 info 이름("localhost")으로 기록된 행 -> 에뮬레이터 세션이 1대면 그 센서
        if (string.Equals(sensorName, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            var emulators = Sensors.Where(s => s.Device.IsEmulator).ToList();
            if (emulators.Count == 1) return emulators[0];
        }

        return Sensors.Count == 1 ? Sensors[0] : null;
    }

    private static string StripPort(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        return System.Text.RegularExpressions.Regex.Replace(name, @":\d+\s*$", "").Trim();
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
            ArrangeTiles();
        }
    }

    // 대시보드(타일 영역) 실제 크기 - View의 SizeChanged에서 갱신
    public double ViewportWidth { get; set; } = 960;
    public double ViewportHeight { get; set; } = 840;

    // true = 격자 정렬 상태 (대시보드 크기가 바뀌면 자동 재정렬). 사용자가 타일을 드래그/리사이즈하면 false
    public bool IsAutoArranged { get; set; } = true;

    // 모든 타일을 센서 개수에 맞춰 대시보드 전체를 꽉 채우도록 세로로 정렬 (오른쪽은 결과 리스트)
    // (1~3개: 1열 세로, 4~6개: 2열, 7~9개: 3열 ...)
    public void ArrangeTiles()
    {
        IsAutoArranged = true;

        int count = Sensors.Count;
        if (count == 0) return;

        int columns = (count + 2) / 3;
        int rows = (int)Math.Ceiling((double)count / columns);

        double tileWidth = Math.Max(280, Math.Floor(ViewportWidth / columns));
        double tileHeight = Math.Max(220, Math.Floor(ViewportHeight / rows));

        for (int i = 0; i < count; i++)
        {
            var session = Sensors[i];
            session.IsMaximized = false;
            session.ZIndex = 0;
            session.Width = tileWidth;
            session.Height = tileHeight;
            session.X = (i % columns) * tileWidth;
            session.Y = (i / columns) * tileHeight;
        }
    }

    private async Task RemoveSensorAsync(SensorSessionViewModel session)
    {
        if (session == null) return;

        await session.DisconnectAsync();
        Sensors.Remove(session);
        ResultHistory.RemoveLatest(session.DisplayName);
        if (IsAutoArranged) ArrangeTiles();
    }

    private async Task ExecuteClose()
    {
        foreach (var session in Sensors.ToList())
        {
            await session.DisconnectAsync();
        }
        ResultCsvService.Instance.Shutdown(); // 남은 결과 CSV 기록 마무리
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
