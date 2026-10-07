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

        // 타일의 CvsDisplay가 로드된 후 InSightBehavior가 연결해주는 콜백 (결과 리스트에서 고른 행의 이미지 표시용)
        public Action<string> ShowImage { get; set; }

        public ICommand DisconnectCommand { get; }
        public ICommand TriggerCommand { get; }
        public ICommand OpenJobCommand { get; }
        public ICommand SaveJobCommand { get; }
        public ICommand SaveAsJobCommand { get; }

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

        // In-Sight 스프레드시트(편집기)가 이 센서에 붙어 있으면 센서가 ONLINE 전환을 거부함 (센서 자체 규칙, 우회 불가)
        private bool _isEditorAttached;
        public bool IsEditorAttached
        {
            get => _isEditorAttached;
            private set { _isEditorAttached = value; OnPropertyChanged(); }
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
                    IsEditorAttached = Sensor.EditorAttached;
                    Sensor.ResultsChanged += OnSensorResultsChanged;
                    Sensor.StateChanged += OnSensorStateChanged;
                    Sensor.EditorAttachedChanged += OnSensorStateChanged;
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
            if (Sensor.Connected)
            {
                Sensor.ResultsChanged -= OnSensorResultsChanged;
                Sensor.StateChanged -= OnSensorStateChanged;
                Sensor.EditorAttachedChanged -= OnSensorStateChanged;
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

            if (Sensor.EditorAttached)
            {
                Logger.Warning($"[{Device.DisplayName}] In-Sight 스프레드시트(편집기)가 연결돼 있어 센서가 ONLINE/OFFLINE 변경을 막고 있습니다. 편집기를 닫은 뒤 다시 시도하세요.");
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
                Logger.Warning($"[{Device.DisplayName}] 상태 변경 실패: {ex.GetBaseException().Message.Trim()}");
                _isOnline = !targetValue;
                OnPropertyChanged(nameof(IsOnline));
            }
        }

        // 센서 쪽에서 상태가 바뀌면(편집기 연결/해제, 편집기에서 Online 변경 등) 화면 토글을 실제 상태에 맞춤
        private void OnSensorStateChanged(object sender, EventArgs e)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                IsEditorAttached = Sensor.EditorAttached;
                if (_isOnline != Sensor.Online)
                {
                    _isOnline = Sensor.Online; // setter를 거치지 않음 (센서에 다시 명령 보내지 않도록)
                    OnPropertyChanged(nameof(IsOnline));
                }
            }));
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

                // 결과 저장(이미지/CSV)은 ONLINE(생산 검사) 상태에서만.
                // OFFLINE의 수동 트리거·결과 리스트 재검사 결과는 화면에만 표시하고 기록하지 않음
                if (!IsOnline) return;

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
                    ResultCsvService.Instance.Append(item); // 이미지가 없어도 판정 결과는 기록
                    return;
                }

                // 결과 이미지를 BMP로 저장 (무손실 - 결과 리스트에서 오프라인 재검사 시 원본과 같은 조건으로 검사)
                try
                {
                    string dir = Path.Combine(Settings.SaveRootPath,
                        item.Timestamp.ToString("yyyy"), item.Timestamp.ToString("MM"), item.Timestamp.ToString("dd"),
                        SanitizeForPath(DisplayName), item.IsOk == true ? "OK" : "NG");
                    Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, $"{item.Timestamp:HH-mm-ss-fff}.bmp");

                    await Task.Run(() => File.WriteAllBytes(path, ToBmpBytes(bytes)));
                    item.ImagePath = path;
                }
                catch (Exception exSave)
                {
                    Logger.Error($"[{DisplayName}] 이미지 저장 실패: {exSave.Message}");
                }

                ResultCsvService.Instance.Append(item);
            }
            catch (Exception ex)
            {
                Logger.Error($"[{Device.DisplayName}] {ex}");
            }
        }

        #region 오프라인 재검사 (결과 리스트에서 고른 이미지를 센서에 올려 잡 재실행)

        private string _pendingReinspectPath;
        private bool _isReinspecting;

        // 이미지 업로드(loadImage)를 지원하지 않는 센서/에뮬레이터는 404 -> 이후 조용히 이미지 표시만
        private bool _reinspectUnsupported;
        private bool _reinspectErrorLogged;

        /// <summary>
        /// 저장된 이미지를 센서에 올려 다시 검사 -> 센서 디스플레이 그래픽/판정이 그 이미지 기준으로 갱신됨
        /// OFFLINE에서만 호출 (결과 저장도 OFFLINE에선 안 되므로 기록에 섞이지 않음)
        /// 빠르게 연속 선택하면 진행 중인 1건이 끝난 뒤 마지막 선택만 처리
        /// </summary>
        public async void Reinspect(string imagePath)
        {
            if (_reinspectUnsupported) return;

            _pendingReinspectPath = imagePath;
            if (_isReinspecting) return;

            _isReinspecting = true;
            try
            {
                while (_pendingReinspectPath != null)
                {
                    string path = _pendingReinspectPath;
                    _pendingReinspectPath = null;

                    if (!Sensor.Connected || IsOnline) return;

                    try
                    {
                        byte[] bmp = await Task.Run(() => ToBmpBytes(File.ReadAllBytes(path)));
                        await Sensor.LoadImage(bmp);
                    }
                    catch (Exception ex)
                    {
                        // 로그는 세션당 1번만 (클릭마다 에러가 쌓이지 않도록)
                        if (ex.Message.Contains("404"))
                        {
                            _reinspectUnsupported = true;
                            _pendingReinspectPath = null;
                            Logger.Info($"[{DisplayName}] 이 센서는 이미지 재검사를 지원하지 않아 저장된 이미지만 표시합니다.");
                        }
                        else if (!_reinspectErrorLogged)
                        {
                            _reinspectErrorLogged = true;
                            Logger.Warning($"[{DisplayName}] 재검사 실패 (이미지만 표시): {ex.GetBaseException().Message.Trim()}");
                        }
                    }
                }
            }
            finally
            {
                _isReinspecting = false;
            }
        }

        private static bool _jpegSourceWarned;

        // 센서 이미지(JPEG/PNG/BMP 등)를 BMP로 변환. 흑백 이미지는 8bit 흑백 BMP로 유지됨 (GDI+)
        private static byte[] ToBmpBytes(byte[] imageBytes)
        {
            if (imageBytes.Length > 2 && imageBytes[0] == 'B' && imageBytes[1] == 'M') return imageBytes; // 이미 BMP

            if (!_jpegSourceWarned && imageBytes.Length > 2 && imageBytes[0] == 0xFF && imageBytes[1] == 0xD8)
            {
                _jpegSourceWarned = true;
                Logger.Warning("센서가 결과 이미지를 JPEG로 전송합니다. BMP로 저장하지만 원본 압축 손실은 복구되지 않습니다.");
            }

            using (var input = new MemoryStream(imageBytes))
            using (var image = System.Drawing.Image.FromStream(input))
            using (var output = new MemoryStream())
            {
                image.Save(output, System.Drawing.Imaging.ImageFormat.Bmp);
                return output.ToArray();
            }
        }

        #endregion

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
    }
}
