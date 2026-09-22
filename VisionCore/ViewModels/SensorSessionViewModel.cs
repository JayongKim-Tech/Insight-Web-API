using Cognex.InSight.Remoting.Serialization;
using Cognex.InSight.Web;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using VisionCore.Models;

namespace VisionCore.ViewModels
{
    /// <summary>
    /// 센서 1개(타일 1개)에 대응하는 연결/제어/결과 상태
    /// </summary>
    public class SensorSessionViewModel : ViewModelBase
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public CvsInSight Sensor { get; } = new CvsInSight();
        public DiscoveredDevice Device { get; }
        public string DisplayName => Device?.DisplayName;

        public ConfigModel Settings => ConfigModel.Instance;
        public DisplayViewModel DisplayVM { get; } = new DisplayViewModel();

        // Point 번호별 최신 결과 슬롯 (PointCount 개수만큼, 트리거될 때마다 해당 Point 슬롯만 갱신)
        public ObservableCollection<InspectionResultItem> Results { get; } = new ObservableCollection<InspectionResultItem>();

        // 타일 자유 배치(드래그/리사이즈)용 좌표·크기
        private double _x;
        public double X { get => _x; set { _x = value; OnPropertyChanged(); } }

        private double _y;
        public double Y { get => _y; set { _y = value; OnPropertyChanged(); } }

        private double _width = 480;
        public double Width { get => _width; set { _width = value; OnPropertyChanged(); } }

        private double _height = 420;
        public double Height { get => _height; set { _height = value; OnPropertyChanged(); } }

        private int _zIndex;
        public int ZIndex { get => _zIndex; set { _zIndex = value; OnPropertyChanged(); } }

        private bool _isMaximized;
        public bool IsMaximized { get => _isMaximized; set { _isMaximized = value; OnPropertyChanged(); } }

        // 전체화면 토글 시 복원할 이전 위치/크기 (MainWindow 코드비하인드에서 사용)
        public double PrevX { get; set; }
        public double PrevY { get; set; }
        public double PrevWidth { get; set; }
        public double PrevHeight { get; set; }

        // 타일의 CvsDisplay가 로드된 후 InSightBehavior가 연결해주는 콜백 (결과 이미지 미리보기용)
        public Action<string> ShowImage { get; set; }

        public ICommand DisconnectCommand { get; }
        public ICommand TriggerCommand { get; }
        public ICommand OpenJobCommand { get; }
        public ICommand SaveJobCommand { get; }
        public ICommand SaveAsJobCommand { get; }
        public ICommand PreviewImageCommand { get; }

        private string _imgUri;
        private string _selectedJobPath;

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            private set { _isConnected = value; OnPropertyChanged(); }
        }

        private bool _isOnline;
        public bool IsOnline
        {
            get => _isOnline;
            set
            {
                if (_isOnline == value) return;
                _isOnline = value;
                OnPropertyChanged();
                ChangeOnlineStateAsync(_isOnline);
            }
        }

        private bool _isLive;
        public bool IsLive
        {
            get => _isLive;
            set
            {
                _isLive = value;
                OnPropertyChanged();
                ExcuteLive(_isLive);
            }
        }

        public SensorSessionViewModel(DiscoveredDevice device)
        {
            Device = device;

            DisconnectCommand = new RelayCommand(async o => await DisconnectAsync());
            TriggerCommand = new RelayCommand(async o => await ExcuteTrigger());
            OpenJobCommand = new RelayCommand(o => ExcuteLoadJob());
            SaveJobCommand = new RelayCommand(async o => await ExcuteSaveJob());
            SaveAsJobCommand = new RelayCommand(async o => await ExecuteSaveAsJob());
            PreviewImageCommand = new RelayCommand(o => ExecutePreviewImage(o as InspectionResultItem));

            Settings.ConfigChanged += OnConfigChanged;
            SyncResultSlots();
        }

        private void OnConfigChanged(object sender, EventArgs e)
        {
            // Config 창에서 Point 구성을 바꾸면 재시작 없이 바로 슬롯에 반영
            System.Windows.Application.Current.Dispatcher.Invoke(SyncResultSlots);
        }

        // 설정된 Point 개수에 맞춰 결과 슬롯을 맞춤 (기존에 쌓인 결과는 번호가 유지되는 한 보존)
        private void SyncResultSlots()
        {
            int count = Math.Max(1, Settings.PointCount);
            var labels = Enumerable.Range(1, count).Select(i => $"Point{i}").ToList();

            for (int i = Results.Count - 1; i >= 0; i--)
            {
                if (!labels.Contains(Results[i].PointName)) Results.RemoveAt(i);
            }

            foreach (var label in labels)
            {
                if (!Results.Any(r => r.PointName == label))
                    Results.Add(new InspectionResultItem { PointName = label });
            }
        }

        private void ExecutePreviewImage(InspectionResultItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.ImagePath)) return;
            ShowImage?.Invoke(item.ImagePath);
        }

        public async Task<bool> ConnectAsync()
        {
            try
            {
                Logger.Info($"[{Device.DisplayName}] 센서 연결 시도 중...");

                var sessionInfo = new HmiSessionInfo
                {
                    SheetName = "Inspection",
                    CellNames = new string[1] { "A0:Z599" }
                };

                await Sensor.Connect($"{Device.IpAddress}:{Device.Port}", Settings.User, Settings.Password, sessionInfo);

                if (Sensor.Connected)
                {
                    IsConnected = true;
                    IsOnline = Sensor.Online;
                    Sensor.ResultsChanged += OnSensorResultsChanged;
                    Logger.Success($"[{Device.DisplayName}] 센서 연결 성공!");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"[{Device.DisplayName}] 연결 실패: {ex.Message}");
                System.Windows.MessageBox.Show($"연결 실패: {ex.Message}");
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            Settings.ConfigChanged -= OnConfigChanged;

            if (Sensor.Connected)
            {
                Sensor.ResultsChanged -= OnSensorResultsChanged;
                await Sensor.Disconnect();
                IsConnected = false;
                Logger.Info($"[{Device.DisplayName}] 센서 연결 해제 완료.");
            }
        }

        private async void ChangeOnlineStateAsync(bool targetValue)
        {
            if (!Sensor.Connected)
            {
                Logger.Warning($"[{Device.DisplayName}] 연결된 센서가 없어 상태를 변경할 수 없습니다.");
                _isOnline = !targetValue;
                OnPropertyChanged(nameof(IsOnline));
                return;
            }

            try
            {
                await Sensor.SetSoftOnlineAsync(targetValue);
                Logger.Success($"[{Device.DisplayName}] 센서 상태: [{(targetValue ? "ONLINE" : "OFFLINE")}]");
            }
            catch (Exception ex)
            {
                Logger.Error($"[{Device.DisplayName}] 상태 변경 오류: {ex}");
                _isOnline = !targetValue;
                OnPropertyChanged(nameof(IsOnline));
            }
        }

        private async void ExcuteLive(bool isLive)
        {
            try
            {
                if (!Sensor.Connected) return;

                await Sensor.SetLiveModeAsync(isLive);

                if (isLive)
                {
                    Logger.Info($"[{Device.DisplayName}] Live Mode ON");
                }
                else
                {
                    await Sensor.SendReady();
                    Logger.Info($"[{Device.DisplayName}] Live Mode OFF");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[{Device.DisplayName}] Live 모드 전환 실패: {ex}");
            }
        }

        private async Task ExcuteTrigger()
        {
            try
            {
                if (Sensor.Connected) await Sensor.ManualAcquire();
            }
            catch (Exception ex)
            {
                Logger.Error($"[{Device.DisplayName}] Trigger 실패: {ex.Message}");
            }
        }

        private async void ExcuteLoadJob()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "로드할 Job 파일을 선택하세요";
                dlg.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory;
                dlg.Filter = "In-Sight Job Files (*.jobx)|*.jobx|모든 파일 (*.*)|*.*";
                dlg.FilterIndex = 1;
                dlg.RestoreDirectory = true;

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _selectedJobPath = dlg.FileName;
                    Logger.Info($"[{Device.DisplayName}] 선택된 파일: {_selectedJobPath}");

                    try
                    {
                        await Sensor.LoadJob(_selectedJobPath);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"[{Device.DisplayName}] Job 로드 중 예외 발생: {ex.Message}");
                    }
                }
            }
        }

        private async Task ExecuteSaveAsJob()
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "Job 파일을 저장할 경로를 선택하세요";
                dlg.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory;
                dlg.Filter = "In-Sight Job Files (*.jobx)|*.jobx|모든 파일 (*.*)|*.*";
                dlg.FilterIndex = 1;
                dlg.RestoreDirectory = true;
                dlg.FileName = "NewJob.jobx";

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _selectedJobPath = dlg.FileName;
                    await Sensor.SaveJob(_selectedJobPath);
                    Logger.Info($"[{Device.DisplayName}] 해당 경로에 Job 저장 완료: {_selectedJobPath}");
                }
            }
        }

        private async Task ExcuteSaveJob()
        {
            if (!Sensor.Connected) return;

            if (string.IsNullOrEmpty(_selectedJobPath))
            {
                await ExecuteSaveAsJob();
                return;
            }

            try
            {
                await Sensor.SaveJob(_selectedJobPath);
                Logger.Info($"[{Device.DisplayName}] 해당 경로에 Job 저장 완료: {_selectedJobPath}");
            }
            catch (Exception ex)
            {
                Logger.Error($"[{Device.DisplayName}] Job Save 실패: {ex}");
            }
        }

        private async void OnSensorResultsChanged(object sender, EventArgs e)
        {
            try
            {
                if (Sensor.LiveMode) return;

                string uri = Sensor.GetMainImageUrl();
                if (_imgUri == uri) return;
                _imgUri = uri;

                await Sensor.GetLatestResult();
                var item = FileManagerModel.Instance.BuildPointResult(Sensor, DisplayName);
                if (item == null) return;

                byte[] bytes;
                try
                {
                    bytes = await _httpClient.GetByteArrayAsync(uri);
                }
                catch (Exception exDown)
                {
                    Logger.Error($"[{DisplayName}] 이미지 다운로드 실패: {exDown.Message}");
                    return;
                }

                // 결과 이미지를 즉시 저장 (썸네일 클릭 시 디스플레이 미리보기에 사용)
                try
                {
                    string dir = Path.Combine(Settings.SaveRootPath,
                        item.Timestamp.ToString("yyyy"), item.Timestamp.ToString("MM"), item.Timestamp.ToString("dd"),
                        SanitizeForPath(DisplayName), item.IsOk == true ? "OK" : "NG");
                    Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, $"{item.Timestamp:HH-mm-ss-fff}.jpg");

                    await Task.Run(() => File.WriteAllBytes(path, bytes));
                    item.ImagePath = path;
                }
                catch (Exception exSave)
                {
                    Logger.Error($"[{DisplayName}] 이미지 저장 실패: {exSave.Message}");
                }

                try
                {
                    var bmp = new BitmapImage();
                    using (var ms = new MemoryStream(bytes))
                    {
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = 160;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                    }
                    bmp.Freeze();
                    item.Thumbnail = bmp;
                }
                catch (Exception exImg)
                {
                    Logger.Error($"[{DisplayName}] 썸네일 로드 실패: {exImg.Message}");
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() => UpdatePointSlot(item));
            }
            catch (Exception ex)
            {
                Logger.Error($"[{Device.DisplayName}] {ex}");
            }
        }

        // 센서 표시 이름(예: "[Emulator] 127.0.0.1:8087")에는 ':' 등 경로에 쓸 수 없는 문자가
        // 포함될 수 있어 폴더명으로 그대로 쓰면 저장이 실패함 -> 치환해서 사용
        private static string SanitizeForPath(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Sensor";

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        private void UpdatePointSlot(InspectionResultItem item)
        {
            var existing = Results.FirstOrDefault(r => r.PointName == item.PointName);
            if (existing != null)
            {
                Results[Results.IndexOf(existing)] = item;
            }
            else
            {
                Results.Add(item);
            }
        }
    }
}
