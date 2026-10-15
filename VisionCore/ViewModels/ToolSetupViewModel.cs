using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Cognex.InSight.Web;
using VisionCore.Models;
using VisionCore.Models.JobEditor;

namespace VisionCore.ViewModels
{
    /// <summary>
    /// 툴 파라미터 설정 창: 포인트 선택 → 그 포인트의 모든 툴 파라미터를 카드로 한 화면에 표시
    /// (수정 즉시 SetCellExpression으로 센서에 반영)
    /// </summary>
    public class ToolSetupViewModel : ViewModelBase
    {
        private readonly CvsInSight _sensor;
        private readonly ToolCatalog _catalog = ToolCatalog.Default;
        private readonly Dictionary<int, List<ToolItemViewModel>> _toolCache = new Dictionary<int, List<ToolItemViewModel>>();
        private readonly SemaphoreSlim _sensorLock = new SemaphoreSlim(1, 1);

        public string Title { get; }
        public ObservableCollection<PointItemViewModel> Points { get; } = new ObservableCollection<PointItemViewModel>();
        public ObservableCollection<ToolItemViewModel> Tools { get; } = new ObservableCollection<ToolItemViewModel>();
        public ObservableCollection<SetupLogEntry> Logs { get; } = new ObservableCollection<SetupLogEntry>();
        public ICommand RefreshCommand { get; }
        public ICommand ClearLogCommand { get; }

        private PointItemViewModel _selectedPoint;
        public PointItemViewModel SelectedPoint
        {
            get => _selectedPoint;
            set
            {
                if (_selectedPoint == value) return;
                _selectedPoint = value;
                OnPropertyChanged();
                LoadTools(value);
            }
        }

        private bool _showHelperTools;
        /// <summary>보조 툴(그래픽, 후처리 DetectBlobs 등)도 표시 (개발자 모드에서만)</summary>
        public bool ShowHelperTools
        {
            get => _showHelperTools;
            set { _showHelperTools = value; OnPropertyChanged(); ShowTools(); }
        }

        private bool _showLockedParams = true;
        /// <summary>입력 이미지(Image) 인자까지 표시 (개발자 모드에서만, 기본: 모든 인자 표시)</summary>
        public bool ShowLockedParams
        {
            get => _showLockedParams;
            set
            {
                _showLockedParams = value;
                OnPropertyChanged();
                RebuildAllSections();
            }
        }

        private bool _selectParamsMode;
        /// <summary>
        /// 표시 파라미터 선택 모드 = 모든 인자 + ☆/★ 토글 (함수 단위, keyparams.json 저장).
        /// 끄면 ★ 지정된 인자만 표시 (함수에 ★ 가 없으면 전부). 🔗 공용 연결은 모드와 무관하게 항상 사용.
        /// </summary>
        public bool SelectParamsMode
        {
            get => _selectParamsMode;
            set
            {
                if (_selectParamsMode == value) return;
                _selectParamsMode = value;
                OnPropertyChanged();
                RebuildAllSections();
            }
        }

        internal bool ShowImageInputs => _showLockedParams;

        private void RebuildAllSections()
        {
            foreach (List<ToolItemViewModel> list in _toolCache.Values)
                foreach (ToolItemViewModel t in list) t.BuildSections(ShowImageInputs);
        }

        // ---- 이미지 + ROI ----
        private static readonly System.Net.Http.HttpClient _http = new System.Net.Http.HttpClient();
        private Dictionary<string, string> _cellMap = new Dictionary<string, string>();

        public ObservableCollection<RoiViewModel> Rois { get; } = new ObservableCollection<RoiViewModel>();
        public ICommand RefreshImageCommand { get; private set; }
        public ICommand TriggerCommand { get; private set; }

        private System.Windows.Media.Imaging.BitmapSource _image;
        public System.Windows.Media.Imaging.BitmapSource Image
        {
            get => _image;
            set { _image = value; OnPropertyChanged(); }
        }

        private ToolItemViewModel _selectedTool;
        /// <summary>카드 클릭 / ROI 클릭으로 선택된 툴 (선택된 ROI만 핸들 표시)</summary>
        public ToolItemViewModel SelectedTool
        {
            get => _selectedTool;
            set
            {
                if (_selectedTool == value) return;
                if (_selectedTool != null) _selectedTool.IsSelected = false;
                _selectedTool = value;
                if (_selectedTool != null) _selectedTool.IsSelected = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedRoi));
            }
        }

        public RoiViewModel SelectedRoi
        {
            get => _selectedTool != null ? _selectedTool.Roi : null;
            set { SelectedTool = value != null ? value.Tool : null; }
        }

        /// <summary>
        /// 인자 값을 숫자로: 리터럴은 그대로, 셀 참조($C$49)는 최신 결과 값. 수식이면 null.
        /// </summary>
        public double? ResolveNumber(CellArg arg)
        {
            if (arg.Kind == CellArgKind.Number) return arg.Number;
            if (arg.Kind != CellArgKind.Reference) return null;
            string text;
            double v;
            if (_cellMap.TryGetValue(arg.Text.Replace("$", ""), out text)
                && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
                return v;
            return null;
        }

        /// <summary>
        /// 센서의 최신 이미지 + 셀 값(Fixture 참조용) 다시 읽기
        /// </summary>
        public async Task RefreshImageAsync()
        {
            if (!_sensor.Connected) return;
            try
            {
                await _sensor.GetLatestResult();
                _cellMap = JobToolScanner.ReadCellMap(_sensor);

                string url = _sensor.GetMainImageUrl();
                if (!string.IsNullOrEmpty(url))
                {
                    byte[] bytes = await _http.GetByteArrayAsync(url);
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    using (var ms = new System.IO.MemoryStream(bytes))
                    {
                        bmp.BeginInit();
                        bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                    }
                    bmp.Freeze();
                    Image = bmp;
                }

                foreach (RoiViewModel r in Rois) r.Reload();
            }
            catch (Exception ex)
            {
                AddLog("이미지 읽기 실패: " + ex.Message, true);
            }
        }

        private async Task TriggerAsync()
        {
            if (!_sensor.Connected) return;
            try
            {
                await _sensor.ManualAcquire();
                await Task.Delay(500); // 검사 완료 대기
                await RefreshImageAsync();
            }
            catch (Exception ex)
            {
                AddLog("촬영 실패: " + ex.Message, true);
            }
        }

        private ToolParamViewModel _helpParam;
        /// <summary>설명 패널에 표시할 파라미터 (마우스를 올리거나 입력칸 포커스)</summary>
        public ToolParamViewModel HelpParam
        {
            get => _helpParam;
            set { if (_helpParam != value) { _helpParam = value; OnPropertyChanged(); } }
        }

        private string _toolSummary;
        public string ToolSummary
        {
            get => _toolSummary;
            set { _toolSummary = value; OnPropertyChanged(); }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        private readonly Func<Task<string>> _saveJob;

        public ICommand SaveCommand { get; }

        private int _unsavedCount;
        /// <summary>마지막 저장 이후 센서에 적용한 변경 수 (창 닫을 때 저장 여부 확인용)</summary>
        public int UnsavedCount
        {
            get => _unsavedCount;
            private set { _unsavedCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(UnsavedText)); }
        }

        public string UnsavedText => _unsavedCount > 0 ? $"저장 안 된 변경 {_unsavedCount}건" : "저장됨";

        public ToolSetupViewModel(CvsInSight sensor, string sensorName, Func<Task<string>> saveJob)
        {
            _sensor = sensor;
            _saveJob = saveJob;
            Title = "툴 파라미터 설정 - " + sensorName;
            RefreshCommand = new RelayCommand(async o => await InitializeAsync(true));
            ClearLogCommand = new RelayCommand(o => Logs.Clear());
            SaveCommand = new RelayCommand(async o => await SaveAsync());
            RefreshImageCommand = new RelayCommand(async o => await RefreshImageAsync());
            TriggerCommand = new RelayCommand(async o => await TriggerAsync());
            Overlay = new OverlayViewModel(this, sensor);
            Align = new AlignSetupViewModel(this, sensor);
        }

        /// <summary>
        /// Ctrl+S / [저장]: 센서의 현재 잡에 덮어쓰기
        /// </summary>
        public async Task<bool> SaveAsync()
        {
            if (!_sensor.Connected)
            {
                AddLog("센서가 연결되어 있지 않아 저장할 수 없습니다.", true);
                return false;
            }

            IsBusy = true;
            await _sensorLock.WaitAsync();
            try
            {
                string name = await _saveJob();
                if (name == null)
                {
                    AddLog("센서의 잡 이름을 알 수 없어 저장하지 못했습니다. 타일의 [Save As]로 저장하세요.", true);
                    return false;
                }
                UnsavedCount = 0;
                AddLog($"저장 완료: 센서의 '{name}' 에 덮어썼습니다.", false);
                return true;
            }
            catch (Exception ex)
            {
                AddLog("저장 실패: " + ex.Message, true);
                Logger.Error("[ToolSetup] 저장 실패: " + ex.Message);
                return false;
            }
            finally
            {
                _sensorLock.Release();
                IsBusy = false;
            }
        }

        // 툴 검색 결과 캐시 (센서+잡 이름 기준, 창을 닫았다 열어도 유지). 값 = 진행 중이거나 끝난 검색 Task.
        // 이 창에서 수정한 값은 ToolCell에 바로 반영되므로 캐시와 어긋나지 않음. Explorer에서 고쳤으면 [새로고침].
        private static readonly Dictionary<string, Dictionary<int, Task<List<ToolCell>>>> _scanCache =
            new Dictionary<string, Dictionary<int, Task<List<ToolCell>>>>();
        private Dictionary<int, Task<List<ToolCell>>> _jobScans = new Dictionary<int, Task<List<ToolCell>>>();

        /// <summary>새 잡 로드 시 (같은 잡 이름이라도 내용이 바뀌었을 수 있음)</summary>
        public static void ClearScanCache()
        {
            lock (_scanCache) _scanCache.Clear();
            lock (_refExpr) _refExpr.Clear();
        }

        // 툴 인자가 참조하는 셀의 식 (Button 참조 → 버튼으로 표시). 셀 위치 → 식
        private static readonly Dictionary<string, string> _refExpr = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private async Task ReadRefExpressionsAsync(List<ToolCell> cells)
        {
            List<string> locs;
            lock (_refExpr)
                locs = cells.SelectMany(c => c.Params).Where(p => p.Arg.Kind == CellArgKind.Reference)
                    .Select(p => p.Arg.Text.Replace("$", "")).Where(l => !_refExpr.ContainsKey(l))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (locs.Count == 0) return;
            var read = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await JobToolScanner.ReadExpressionsAsync(_sensor, locs, read);
            lock (_refExpr)
                foreach (KeyValuePair<string, string> kv in read) _refExpr[kv.Key] = kv.Value;
        }

        /// <summary>참조 셀의 식 (모르면 null)</summary>
        internal string RefExpressionOf(CellArg arg)
        {
            if (arg.Kind != CellArgKind.Reference) return null;
            string expr;
            lock (_refExpr) return _refExpr.TryGetValue(arg.Text.Replace("$", ""), out expr) ? expr : null;
        }

        /// <summary>버튼 셀 누르기 (예: Train Event → 모델 재등록). 누른 뒤 이미지/결과 다시 읽음</summary>
        internal async Task PressButtonAsync(ToolItemViewModel tool, ToolParamViewModel param, string loc)
        {
            if (!_sensor.Connected) return;
            await _sensorLock.WaitAsync();
            try
            {
                await _sensor.SetCellValue(loc, "1");
                await Task.Delay(300);
                UnsavedCount++;
                AddLog($"{tool.Point.Info.Code} {tool.Cell.FunctionName} · {param.FullName}: 버튼 {loc} 누름", false);
                Logger.Info($"[ToolSetup] button {loc} pressed ({tool.Cell.Location} {param.FullName})");
            }
            catch (Exception ex)
            {
                AddLog($"버튼 {loc} 누르기 실패: {ex.Message}", true);
                return;
            }
            finally
            {
                _sensorLock.Release();
            }
            await RefreshImageAsync();
        }

        /// <summary>
        /// 트리거 인자(Event 등)용 버튼 셀 생성: 툴과 같은 행 오른쪽의 빈 셀에 Button("이름") 을 넣고 인자를 그 셀 참조로 변경
        /// </summary>
        internal async Task CreateButtonAsync(ToolItemViewModel tool, ToolParamViewModel param)
        {
            if (!_sensor.Connected) return;
            string toolLoc = tool.Cell.Location;
            int colIndex = JobTemplateLayout.Columns.IndexOf(char.ToUpperInvariant(toolLoc[0]));
            string row = toolLoc.Substring(1);
            var candidates = JobTemplateLayout.Columns.Substring(colIndex + 1).Select(c => c + row).ToList();
            var exprs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await JobToolScanner.ReadExpressionsAsync(_sensor, candidates, exprs);
            string target = candidates.FirstOrDefault(l => string.IsNullOrWhiteSpace(Get(exprs, l)));
            if (target == null)
            {
                AddLog($"{toolLoc} 오른쪽에 빈 셀이 없어 버튼을 만들 수 없습니다.", true);
                return;
            }

            string caption = param.FullName.IndexOf("Train", StringComparison.OrdinalIgnoreCase) >= 0 ? "Train" : param.FullName;
            string buttonExpr = "Button(\"" + caption.Replace("\"", "") + "\")";
            await _sensorLock.WaitAsync();
            try
            {
                await _sensor.SetCellExpression(target, buttonExpr);
                lock (_refExpr) _refExpr[target] = buttonExpr;
                UnsavedCount++;
                AddLog($"{tool.Point.Info.Code} {target} 에 {buttonExpr} 생성", false);
            }
            catch (Exception ex)
            {
                AddLog($"{target} 버튼 생성 실패: {ex.Message}", true);
                return;
            }
            finally
            {
                _sensorLock.Release();
            }
            await ApplyAsync(tool, param, "$" + target[0] + "$" + row);
        }

        private static string Get(Dictionary<string, string> map, string loc)
        {
            string v;
            return map.TryGetValue(loc, out v) ? v : null;
        }

        private Task<List<ToolCell>> GetToolsAsync(PointInfo point)
        {
            Task<List<ToolCell>> task;
            if (!_jobScans.TryGetValue(point.Index, out task) || task.IsFaulted)
            {
                task = ScanToolsAsync(point);
                _jobScans[point.Index] = task;
            }
            return task;
        }

        private async Task<List<ToolCell>> ScanToolsAsync(PointInfo point)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            List<ToolCell> cells = await JobToolScanner.LoadToolsAsync(_sensor, point, _catalog);
            await ReadRefExpressionsAsync(cells);
            AddLog($"{point.Code} 툴 {cells.Count}개 검색 ({sw.ElapsedMilliseconds / 1000.0:0.0}초)", false);
            return cells;
        }

        /// <summary>
        /// 사용 중인 나머지 포인트를 백그라운드로 미리 검색 → 포인트를 눌렀을 때 바로 표시
        /// </summary>
        private async void PrefetchTools()
        {
            foreach (PointItemViewModel p in Points.Where(p => p.IsUsed).ToList())
            {
                try { await GetToolsAsync(p.Info); }
                catch { /* 해당 포인트를 열 때 다시 시도 */ }
            }
        }

        public Task InitializeAsync()
        {
            return InitializeAsync(false);
        }

        public async Task InitializeAsync(bool rescan)
        {
            if (!_sensor.Connected)
            {
                AddLog("센서가 연결되어 있지 않습니다.", true);
                return;
            }

            IsBusy = true;
            try
            {
                int? keepIndex = _selectedPoint != null ? _selectedPoint.Info.Index : (int?)null;
                _toolCache.Clear();

                string jobKey = Title + "|" + await _sensor.GetJobName();
                if (rescan || !_scanCache.TryGetValue(jobKey, out _jobScans))
                {
                    _jobScans = new Dictionary<int, Task<List<ToolCell>>>();
                    _scanCache[jobKey] = _jobScans;
                }
                Tools.Clear();
                Points.Clear();

                List<PointInfo> points;
                await _sensorLock.WaitAsync();
                try { points = await JobToolScanner.LoadPointsAsync(_sensor, ConfigModel.Instance.PointCount); }
                finally { _sensorLock.Release(); }

                foreach (PointInfo p in points) Points.Add(new PointItemViewModel(p));
                AddLog($"잡 읽기 완료: 사용 중인 포인트 {Points.Count(p => p.IsUsed)}개", false);
                await RefreshImageAsync();

                _selectedPoint = null;
                SelectedPoint = Points.FirstOrDefault(p => keepIndex.HasValue ? p.Info.Index == keepIndex.Value : p.IsUsed) ?? Points.FirstOrDefault();
                PrefetchTools();
            }
            catch (Exception ex)
            {
                AddLog("포인트 읽기 실패: " + ex.Message, true);
                Logger.Error("[ToolSetup] 포인트 읽기 실패: " + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void LoadTools(PointItemViewModel point)
        {
            foreach (PointItemViewModel p in Points) p.IsSelected = p == point;
            Tools.Clear();
            ToolSummary = null;
            if (point == null) return;

            if (!_toolCache.ContainsKey(point.Info.Index))
            {
                IsBusy = true;
                ToolSummary = point.Info.Code + " 툴 검색 중...";
                try
                {
                    List<ToolCell> cells = await GetToolsAsync(point.Info);
                    if (_toolCache.ContainsKey(point.Info.Index)) { if (_selectedPoint == point) ShowTools(); return; }
                    _toolCache[point.Info.Index] = cells.Select(c => new ToolItemViewModel(this, point, c, ShowImageInputs)).ToList();
                }
                catch (Exception ex)
                {
                    ToolSummary = null;
                    AddLog(point.Info.Code + " 툴 검색 실패: " + ex.Message, true);
                    Logger.Error("[ToolSetup] 툴 검색 실패: " + ex.Message);
                    return;
                }
                finally
                {
                    IsBusy = false;
                }
            }

            // 검색하는 사이 다른 포인트를 골랐으면 무시
            if (_selectedPoint != point) return;
            ShowTools();
        }

        private void ShowTools()
        {
            Tools.Clear();
            Rois.Clear();
            SelectedTool = null;
            List<ToolItemViewModel> all;
            if (_selectedPoint == null || !_toolCache.TryGetValue(_selectedPoint.Info.Index, out all)) return;

            bool showHelpers = _showHelperTools;
            foreach (ToolItemViewModel t in all.Where(t => showHelpers || !t.IsHelper))
            {
                Tools.Add(t);
                if (t.Roi != null)
                {
                    t.Roi.Reload();
                    Rois.Add(t.Roi);
                }
            }
            SelectedTool = Tools.FirstOrDefault(t => t.Roi != null);

            int helpers = all.Count(t => t.IsHelper);
            ToolSummary = $"{_selectedPoint.Info.Code}  검사 툴 {all.Count - helpers}개" + (helpers > 0 ? $" · 보조 툴 {helpers}개{(showHelpers ? "" : " (숨김)")}" : "");
        }

        /// <summary>
        /// 파라미터 1개 변경 → 새 식을 센서에 전송. 실패하면 이전 값으로 되돌림.
        /// </summary>
        internal async Task ApplyAsync(ToolItemViewModel tool, ToolParamViewModel param, string newText)
        {
            await _sensorLock.WaitAsync();
            string oldText = tool.Cell.Params[param.Index].Arg.Text;
            try
            {
                if (oldText == newText) return;
                string expr = tool.Cell.SetText(param.Index, newText);
                await _sensor.SetCellExpression(tool.Cell.Location, expr);
                UnsavedCount++;
                AddLog($"{tool.Point.Info.Code} {tool.Cell.Location} {tool.Cell.FunctionName} · {param.FullName}: {oldText} → {newText}", false);
                Logger.Info($"[ToolSetup] {tool.Cell.Location} {param.FullName}: {oldText} -> {newText}");
            }
            catch (Exception ex)
            {
                tool.Cell.SetText(param.Index, oldText);
                AddLog($"{tool.Cell.Location} {param.FullName} 적용 실패 (원래 값 {oldText} 유지): {ex.Message}", true);
                Logger.Error($"[ToolSetup] {tool.Cell.Location} {param.FullName} 적용 실패: {ex.Message}");
            }
            finally
            {
                tool.RefreshParams();
                _sensorLock.Release();
            }
        }

        /// <summary>
        /// ROI 드래그: 여러 인자를 한 식으로 묶어 1번에 전송. 실패하면 이전 식으로 되돌림.
        /// </summary>
        internal async Task ApplyManyAsync(ToolItemViewModel tool, IDictionary<int, string> changes)
        {
            await _sensorLock.WaitAsync();
            string oldExpr = tool.Cell.Expression.Source;
            string summary = string.Join(", ", changes.Select(c => tool.Cell.Params[c.Key].Name + "=" + c.Value));
            try
            {
                string expr = tool.Cell.SetTexts(changes);
                await _sensor.SetCellExpression(tool.Cell.Location, expr);
                UnsavedCount++;
                AddLog($"{tool.Point.Info.Code} {tool.Cell.Location} {tool.Cell.FunctionName} · 영역: {summary}", false);
                Logger.Info($"[ToolSetup] {tool.Cell.Location} 영역 변경: {summary}");
            }
            catch (Exception ex)
            {
                tool.Cell.Restore(oldExpr);
                AddLog($"{tool.Cell.Location} 영역 적용 실패 (원래 위치 유지): {ex.Message}", true);
                Logger.Error($"[ToolSetup] {tool.Cell.Location} 영역 적용 실패: {ex.Message}");
            }
            finally
            {
                tool.RefreshParams();
                _sensorLock.Release();
            }
        }

        /// <summary>참조 인자($C$49)가 가리키는 셀의 최신 값 (결과 표시용). 참조가 아니거나 모르면 null</summary>
        public string ResolveCellText(CellArg arg)
        {
            if (arg.Kind != CellArgKind.Reference) return null;
            string text;
            return _cellMap.TryGetValue(arg.Text.Replace("$", ""), out text) ? text : null;
        }

        /// <summary>
        /// 참조 인자가 가리키는 셀의 값을 바꿈 (툴 식은 그대로 → 다른 툴과의 연결 유지).
        ///  - 숫자/문자 상수 셀: 식을 새 값으로
        ///  - EditInt/EditFloat/EditString 등 값 입력 셀: SetCellValue (식/범위 유지)
        ///  - 그 외 수식 셀: 값을 직접 못 바꿈 → 식 칸에서 수정하도록 안내
        /// </summary>
        internal async Task ApplyLinkedValueAsync(ToolItemViewModel tool, ToolParamViewModel param, CellArg arg, string newValue)
        {
            string loc = arg.Text.Replace("$", "");
            await _sensorLock.WaitAsync();
            try
            {
                string oldValue = ResolveCellText(arg);
                if (oldValue == newValue) return;
                string targetExpr = (await _sensor.GetCellExpression(loc) ?? "").Trim();
                double dummy;
                bool isConstant = targetExpr.Length == 0
                    || double.TryParse(targetExpr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dummy)
                    || (targetExpr.StartsWith("\"") && targetExpr.EndsWith("\""));

                if (isConstant)
                {
                    double d;
                    bool isNumber = double.TryParse(newValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d);
                    await _sensor.SetCellExpression(loc, isNumber ? newValue : "\"" + newValue.Replace("\"", "") + "\"");
                }
                else if (targetExpr.StartsWith("Edit", StringComparison.OrdinalIgnoreCase))
                    await _sensor.SetCellValue(loc, newValue);
                else
                {
                    AddLog($"{loc} 는 수식 셀({targetExpr})이라 값만 바꿀 수 없습니다. 왼쪽 식 칸에서 직접 수정하세요.", true);
                    return;
                }

                _cellMap[loc] = newValue;
                UnsavedCount++;
                AddLog($"{tool.Point.Info.Code} {tool.Cell.Location} {tool.Cell.FunctionName} · {param.FullName} (→ {loc}): {oldValue} → {newValue}", false);
                Logger.Info($"[ToolSetup] {loc} (linked from {tool.Cell.Location} {param.FullName}): {oldValue} -> {newValue}");
            }
            catch (Exception ex)
            {
                AddLog($"{loc} 값 적용 실패: {ex.Message}", true);
                Logger.Error($"[ToolSetup] {loc} 값 적용 실패: {ex.Message}");
            }
            finally
            {
                // 같은 셀을 참조하는 다른 툴(공용 파라미터)도 표시 갱신
                foreach (ToolItemViewModel t in _toolCache.Values.SelectMany(l => l))
                    if (t == tool || t.Cell.Params.Any(p => p.Arg.Kind == CellArgKind.Reference && string.Equals(p.Arg.Text.Replace("$", ""), loc, StringComparison.OrdinalIgnoreCase)))
                        t.RefreshParams();
                _sensorLock.Release();
            }
        }

        // ---- 공용 파라미터: 같은 함수 툴끼리 인자를 공용 셀 1개로 연결 ----
        // 스프레드시트의 'SHARED PARAM' 라벨 셀 아래 행들 = 슬롯 (라벨 열 = 이름, 오른쪽 열 = 값).
        // 연결 = 각 툴의 인자를 값 셀 참조($X$n)로 바꿈 → 센서 잡 안에서 연결 유지, 값 1번 수정으로 전부 반영.
        public const string SharedLabel = "SHARED PARAM";
        private const int MaxSharedRows = 64;
        private Dictionary<string, string> _sharedMapRef;
        private int _sharedCol = -1, _sharedHeaderRow = -1;

        private bool TryFindSharedArea(out int col, out int headerRow)
        {
            if (!ReferenceEquals(_sharedMapRef, _cellMap))
            {
                _sharedMapRef = _cellMap;
                _sharedCol = _sharedHeaderRow = -1;
                foreach (KeyValuePair<string, string> kv in _cellMap)
                {
                    int c, r;
                    if (!string.Equals((kv.Value ?? "").Trim(), SharedLabel, StringComparison.OrdinalIgnoreCase) || !AlignLayout.TryParse(kv.Key, out c, out r) || c >= 25) continue;
                    if (_sharedHeaderRow < 0 || r < _sharedHeaderRow) { _sharedCol = c; _sharedHeaderRow = r; }
                }
            }
            col = _sharedCol;
            headerRow = _sharedHeaderRow;
            return headerRow >= 0;
        }

        private string CellText(string loc)
        {
            string v;
            return _cellMap.TryGetValue(loc, out v) ? (v ?? "").Trim() : "";
        }

        /// <summary>참조 인자가 공용 슬롯의 값 셀이면 슬롯 이름, 아니면 null</summary>
        public string SharedNameOf(CellArg arg)
        {
            if (arg == null || arg.Kind != CellArgKind.Reference) return null;
            int c, r, sc, hr;
            if (!AlignLayout.TryParse(arg.Text.Replace("$", ""), out c, out r) || !TryFindSharedArea(out sc, out hr)) return null;
            if (c != sc + 1 || r <= hr || r > hr + MaxSharedRows) return null;
            string name = CellText(AlignLayout.Loc(sc, r));
            return name.Length > 0 ? name : null;
        }

        /// <summary>같은 함수를 쓰는 다른 툴 (사용 중인 모든 포인트, 보조 툴 포함). 안 열어 본 포인트는 여기서 검색.</summary>
        internal async Task<List<ToolItemViewModel>> GetSameFunctionToolsAsync(ToolItemViewModel source, int paramIndex)
        {
            var result = new List<ToolItemViewModel>();
            foreach (PointItemViewModel p in Points.Where(p => p.IsUsed).ToList())
            {
                List<ToolItemViewModel> list;
                if (!_toolCache.TryGetValue(p.Info.Index, out list))
                {
                    List<ToolCell> cells;
                    try { cells = await GetToolsAsync(p.Info); }
                    catch (Exception ex) { AddLog(p.Info.Code + " 툴 검색 실패: " + ex.Message, true); continue; }
                    if (!_toolCache.TryGetValue(p.Info.Index, out list))
                        _toolCache[p.Info.Index] = list = cells.Select(c => new ToolItemViewModel(this, p, c, ShowImageInputs)).ToList();
                }
                result.AddRange(list.Where(t => t != source && t.Cell.Params.Count > paramIndex
                    && string.Equals(t.Cell.FunctionName, source.Cell.FunctionName, StringComparison.OrdinalIgnoreCase)));
            }
            return result;
        }

        /// <summary>
        /// source 인자 + targets 의 같은 인자를 공용 셀 1개로 연결.
        /// source 가 이미 공용 셀을 참조하면 그 셀에 묶고, 아니면 빈 슬롯을 만들어 현재 값을 옮김.
        /// </summary>
        internal async Task LinkParamAsync(ToolItemViewModel source, ToolParamViewModel param, IList<ToolItemViewModel> targets, string name)
        {
            IsBusy = true;
            await _sensorLock.WaitAsync();
            try
            {
                CellArg arg = source.Cell.Params[param.Index].Arg;
                string reference;
                var all = new List<ToolItemViewModel>(targets);

                if (SharedNameOf(arg) != null)
                    reference = arg.Text;
                else
                {
                    if (!arg.IsLiteral)
                    {
                        AddLog($"{source.Cell.Location} {param.FullName}: 이미 다른 셀 참조/수식이라 연결할 수 없습니다. 먼저 값(숫자/문자)으로 바꾸세요.", true);
                        return;
                    }

                    await _sensor.GetLatestResult();
                    _cellMap = JobToolScanner.ReadCellMap(_sensor);
                    int col, hr;
                    if (!TryFindSharedArea(out col, out hr))
                    {
                        AddLog($"스프레드시트에 '{SharedLabel}' 라벨 셀이 없습니다. 빈 곳에 라벨을 만들어 주세요 (아래 행들: 라벨 열 = 이름, 오른쪽 열 = 값).", true);
                        return;
                    }

                    int row = -1;
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int r = hr + 1; r <= hr + MaxSharedRows; r++)
                    {
                        string n = CellText(AlignLayout.Loc(col, r));
                        if (n.Length > 0) names.Add(n);
                        else if (row < 0 && CellText(AlignLayout.Loc(col + 1, r)).Length == 0) row = r;
                    }
                    if (row < 0)
                    {
                        AddLog($"'{SharedLabel}' 아래 빈 행이 없습니다 ({MaxSharedRows}행).", true);
                        return;
                    }

                    string baseName = string.IsNullOrWhiteSpace(name) ? source.Cell.FunctionName + "." + param.Label : name.Trim().Replace("\"", "");
                    string unique = baseName;
                    for (int i = 2; names.Contains(unique); i++) unique = baseName + " " + i;

                    string nameCell = AlignLayout.Loc(col, row), valueCell = AlignLayout.Loc(col + 1, row);
                    await _sensor.SetCellExpression(nameCell, "\"" + unique + "\"");
                    await _sensor.SetCellExpression(valueCell, arg.Text);
                    _cellMap[nameCell] = unique;
                    _cellMap[valueCell] = arg.Kind == CellArgKind.String ? arg.Text.Trim('"') : arg.Text;
                    reference = "$" + AlignLayout.Cols[col + 1] + "$" + row;
                    UnsavedCount++;
                    AddLog($"공용 파라미터 '{unique}' 생성: {valueCell} = {arg.Text}", false);
                    all.Insert(0, source);
                }

                int ok = 0;
                foreach (ToolItemViewModel t in all)
                {
                    string old = t.Cell.Params[param.Index].Arg.Text;
                    if (old == reference) continue;
                    try
                    {
                        string expr = t.Cell.SetText(param.Index, reference);
                        await _sensor.SetCellExpression(t.Cell.Location, expr);
                        UnsavedCount++;
                        ok++;
                        Logger.Info($"[ToolSetup] {t.Cell.Location} {param.FullName}: {old} -> {reference} (공용)");
                    }
                    catch (Exception ex)
                    {
                        t.Cell.SetText(param.Index, old);
                        AddLog($"{t.Point.Info.Code} {t.Cell.Location} 연결 실패 (원래 값 {old} 유지): {ex.Message}", true);
                    }
                    finally
                    {
                        t.RefreshParams();
                    }
                }
                AddLog($"{source.Cell.FunctionName} · {param.FullName}: 툴 {ok}개를 {reference} 에 연결 (값 {CellText(reference.Replace("$", ""))})", false);
            }
            catch (Exception ex)
            {
                AddLog("공용 파라미터 연결 실패: " + ex.Message, true);
                Logger.Error("[ToolSetup] 공용 파라미터 연결 실패: " + ex.Message);
            }
            finally
            {
                _sensorLock.Release();
                IsBusy = false;
            }
        }

        /// <summary>이 툴만 공용 셀 연결 해제 → 현재 공용 값을 상수로 넣음 (다른 툴은 그대로 연결)</summary>
        internal Task UnlinkParamAsync(ToolItemViewModel tool, ToolParamViewModel param)
        {
            string value = ResolveCellText(tool.Cell.Params[param.Index].Arg) ?? "";
            double d;
            string literal = double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)
                ? CellExpression.FormatNumber(d)
                : "\"" + value.Replace("\"", "") + "\"";
            return ApplyAsync(tool, param, literal);
        }

        // ---- 오버레이 탭 (OverlayViewModel) 에서 사용 ----
        public OverlayViewModel Overlay { get; private set; }

        /// <summary>캘리브레이션 / 원점 등록 / 데이터 맵 탭</summary>
        public AlignSetupViewModel Align { get; private set; }
        public CvsInSight Sensor => _sensor;
        internal SemaphoreSlim SensorLock => _sensorLock;
        internal void MarkUnsaved() => UnsavedCount++;
        internal void Log(string text, bool isError) => AddLog(text, isError);

        private void AddLog(string text, bool isError)
        {
            Logs.Insert(0, new SetupLogEntry { Time = DateTime.Now.ToString("HH:mm:ss"), Text = text, IsError = isError });
            while (Logs.Count > 200) Logs.RemoveAt(Logs.Count - 1);
        }
    }

    public class SetupLogEntry
    {
        public string Time { get; set; }
        public string Text { get; set; }
        public bool IsError { get; set; }
    }

    public class PointItemViewModel : ViewModelBase
    {
        public PointInfo Info { get; }

        public PointItemViewModel(PointInfo info)
        {
            Info = info;
        }

        // Pos 0(또는 비어 있음) = 사용하지 않는 포인트
        public bool IsUsed => !string.IsNullOrEmpty(Info.Position) && Info.Position != "0";
        public string Code => Info.Code;
        public string Name => Info.Name;
        public string Detail => (IsUsed ? "Pos " + Info.Position : "미사용") + (Info.Pass ? " · Pass" : "");
        public string Result => IsUsed ? Info.Result : "";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }
    }

    /// <summary>
    /// 툴 카드 안의 묶음 (검사 설정 / 영역 / 기준 위치 / 연결된 값)
    /// </summary>
    public class ParamSection
    {
        public string Header { get; set; }
        public List<ToolParamViewModel> Params { get; set; }
    }

    public class ToolItemViewModel : ViewModelBase
    {
        private static readonly Dictionary<string, string> _friendlyNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "FindLine", "직선 엣지 찾기" }, { "FindCircle", "원 찾기" }, { "FindSegment", "선분 찾기" },
            { "FindPatterns", "패턴 찾기" }, { "FindPatMaxPatterns", "PatMax 패턴 찾기" }, { "FindBlobs", "블롭 찾기" },
            { "DetectBlobs", "블롭 검출" }, { "ExtractBlobs", "블롭 추출" }, { "Histogram", "밝기 히스토그램" },
            { "ViDiELSegment", "EdgeAI 세그먼트" }, { "ViDiELClassify", "EdgeAI 분류" },
            { "Caliper", "캘리퍼" }, { "FindCurve", "곡선 찾기" }, { "ReadIDMax", "코드 읽기" }, { "ReadText", "문자 읽기" }
        };

        private readonly ToolSetupViewModel _owner;
        private readonly List<ToolParamViewModel> _allParams = new List<ToolParamViewModel>();

        public ToolCell Cell { get; }
        public PointItemViewModel Point { get; }
        public bool IsHelper { get; }

        public string Title => Cell.Location + "  " + Cell.FunctionName;
        public string FriendlyName
        {
            get
            {
                string[] easy = EasyHelp.Default.Function(Cell.FunctionName);
                if (easy != null) return easy[0];
                string name;
                return _friendlyNames.TryGetValue(Cell.FunctionName, out name) ? name : null;
            }
        }

        /// <summary>툴이 하는 일 한 줄 (Resources\ToolEasy.ko.json)</summary>
        public string FunctionTip
        {
            get
            {
                string[] easy = EasyHelp.Default.Function(Cell.FunctionName);
                return easy != null ? easy[1] : null;
            }
        }

        // 인자 개수가 도움말과 다르면 이름이 밀려 있을 수 있음
        public string Warning => Cell.IsUnknownFunction
            ? "도움말에 없는 함수라 인자 이름을 모릅니다 (번호로 표시)."
            : Cell.ArgCountMatches ? null : $"식의 인자가 도움말(26.2)보다 많습니다 (식 {Cell.Params.Count}개 / 도움말 {Cell.CatalogArgCount}개). 센서 펌웨어에서 추가된 인자는 '인자 번호'로 표시됩니다.";

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        private List<ParamSection> _sections;
        public List<ParamSection> Sections
        {
            get => _sections;
            private set { _sections = value; OnPropertyChanged(); }
        }

        public int ChangedCount => _allParams.Count(p => p.IsChanged);

        /// <summary>사각 검사 영역이 있는 툴만 (이미지 위 ROI)</summary>
        public RoiViewModel Roi { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public ToolItemViewModel(ToolSetupViewModel owner, PointItemViewModel point, ToolCell cell, bool showLocked)
        {
            _owner = owner;
            Point = point;
            Cell = cell;
            IsHelper = cell.IsHelper;
            IsExpanded = !IsHelper;
            foreach (ToolParam p in cell.Params) _allParams.Add(new ToolParamViewModel(this, p));
            BuildSections(showLocked);
            Roi = RoiViewModel.TryCreate(this, owner.ResolveNumber);
        }

        internal ToolSetupViewModel Owner => _owner;

        internal Task ApplyManyAsync(IDictionary<int, string> changes)
        {
            return _owner.ApplyManyAsync(this, changes);
        }

        /// <summary>
        /// 함수의 모든 인자를 그룹별로 (빠짐없이). 검사 설정이 맨 위, 영역/기준 위치는 아래.
        /// 참조/수식 인자도 같은 자리에 두고 식 칸 + 참조 셀 값 칸으로 수정
        /// showLocked=false 면 Image 같은 입력 연결 인자(그룹 "Image")만 접어 둠
        /// </summary>
        internal void BuildSections(bool showLocked)
        {
            var sections = new List<ParamSection>();
            // 평소: ★ 지정 파라미터만 (이 함수에 ★ 가 하나도 없으면 전부). 선택 모드: 전부 + ☆ 토글
            bool keyOnly = !_owner.SelectParamsMode && KeyParamStore.Default.HasAny(Cell.FunctionName);
            var all = _allParams.Where(p => (showLocked || !p.IsImageInput) && (!keyOnly || p.IsKey)).ToList();
            foreach (ToolParamViewModel p in _allParams) p.NotifyMode();

            Add(sections, "검사 설정", all.Where(p => p.Group == null));
            foreach (string g in all.Select(p => p.Group).Where(g => g != null && g != "Fixture").Distinct())
                Add(sections, g == "Region" ? "검사 영역 (Region)" : g, all.Where(p => p.Group == g));
            Add(sections, "기준 위치 (Fixture)", all.Where(p => p.Group == "Fixture"));

            Sections = sections;
        }

        internal string ResolveCellText(CellArg arg)
        {
            return _owner.ResolveCellText(arg);
        }

        internal Task ApplyLinkedValueAsync(ToolParamViewModel param, CellArg arg, string value)
        {
            return _owner.ApplyLinkedValueAsync(this, param, arg, value);
        }

        private static void Add(List<ParamSection> sections, string header, IEnumerable<ToolParamViewModel> items)
        {
            List<ToolParamViewModel> list = items.ToList();
            if (list.Count > 0) sections.Add(new ParamSection { Header = header, Params = list });
        }

        internal Task ApplyAsync(ToolParamViewModel param, string newText)
        {
            return _owner.ApplyAsync(this, param, newText);
        }

        /// <summary>
        /// ToolCell이 식을 다시 파싱하면 ToolParam 객체가 새로 만들어지므로 VM이 잡고 있는 참조를 교체
        /// </summary>
        internal void RefreshParams()
        {
            foreach (ToolParamViewModel vm in _allParams)
                vm.Reload(Cell.Params[vm.Index]);
            OnPropertyChanged(nameof(ChangedCount));
            if (Roi != null) Roi.Reload();
        }
    }

    public class OptionItem
    {
        public string Value { get; set; }
        public string Label { get; set; }
        public override string ToString() => Label + "  (" + Value + ")";
    }

    public class ToolParamViewModel : ViewModelBase
    {
        private readonly ToolItemViewModel _tool;
        private readonly string _originalText;
        private ToolParam _param;

        public ToolParamViewModel(ToolItemViewModel tool, ToolParam param)
        {
            _tool = tool;
            _param = param;
            _originalText = param.Arg.Text;

            if (param.Def != null && param.Def.Options != null && param.Arg.Kind == CellArgKind.Number)
                Options = param.Def.Options.Select(o => new OptionItem { Value = o[0], Label = o[1] }).ToList();
        }

        public int Index => _param.Index;
        public string Group => _param.Group;
        public string Label => Easy != null ? Easy[0] : _param.Def != null ? _param.Def.Name : _param.Name;
        public string FullName => _param.Name;

        // 쉬운 이름 + 한 줄 설명 (Resources\ToolEasy.ko.json)
        private string[] Easy => EasyHelp.Default.Param(_tool.Cell.FunctionName, FullName, _param.Def != null ? _param.Def.Name : null);
        public string EasyTip => Easy != null ? Easy[1] : null;
        public bool HasEasyTip => !string.IsNullOrEmpty(EasyTip);
        public List<OptionItem> Options { get; }

        // ---- 설명 패널용 ----
        public string HelpTitle => _tool.Cell.FunctionName + " · " + FullName;
        public string HelpKorean => ToolHelp.Default.Find(_tool.Cell.FunctionName, FullName, _param.Def != null ? _param.Def.Description : null);
        public string HelpEnglish => _param.Def != null ? _param.Def.Description : null;
        public bool HasHelpKorean => !string.IsNullOrEmpty(HelpKorean);

        public string HelpFacts
        {
            get
            {
                var lines = new List<string>();
                if (_param.Def != null && _param.Def.Min.HasValue) lines.Add($"범위: {_param.Def.Min} ~ {_param.Def.Max}");
                if (_param.Def != null && !string.IsNullOrEmpty(_param.Def.Default)) lines.Add("기본값: " + _param.Def.Default);
                if (Options != null) lines.Add("선택 항목:\n" + string.Join("\n", Options.Select(o => "  " + o.Value + " = " + o.Label)));
                lines.Add("현재 값: " + _param.Arg.Text + (IsChanged ? "   (원래 값: " + _originalText + ")" : ""));
                if (IsLocked)
                {
                    lines.Add("다른 셀 참조/수식입니다. 왼쪽 칸 = 식(참조) 자체 수정, 오른쪽 칸 = 참조한 셀의 값 수정 (연결 유지).");
                    if (LinkedValue != null) lines.Add("참조 셀 값: " + LinkedValue);
                }
                return string.Join("\n", lines);
            }
        }

        // 툴팁은 짧게 (자세한 내용은 설명 패널)
        public string Description => EasyTip ?? HelpKorean ?? HelpEnglish;

        /// <summary>참조($C$49)나 수식 인자. 잠금이 아니라 "식 칸 + 참조 셀 값 칸"으로 수정</summary>
        public bool IsLocked => !_param.IsEditable;

        /// <summary>0번 Image 인자 (입력 이미지 연결) - 옵션으로 숨김</summary>
        public bool IsImageInput => _param.Index == 0 && _param.Def != null && _param.Def.Name == "Image";

        /// <summary>참조 셀의 현재 값. 셀 1개 참조가 아니면 null</summary>
        public string LinkedValue
        {
            get => IsLocked ? _tool.ResolveCellText(_param.Arg) : null;
            set
            {
                if (value == null || !HasLinkedValue) return;
                string text = value.Trim();
                if (text == LinkedValue) return;
                ApplyLinked(text);
            }
        }

        public bool HasLinkedValue => LinkedValue != null;

        private async void ApplyLinked(string text)
        {
            await _tool.ApplyLinkedValueAsync(this, _param.Arg, text);
        }

        // ---- 사용자/개발자 모드 ----
        internal ToolItemViewModel Tool => _tool;

        /// <summary>★ 주요 파라미터 (사용자 모드에 표시). 함수 단위로 PC에 저장</summary>
        public bool IsKey
        {
            get => KeyParamStore.Default.IsKey(_tool.Cell.FunctionName, FullName);
            set { KeyParamStore.Default.Set(_tool.Cell.FunctionName, FullName, value); OnPropertyChanged(); }
        }

        /// <summary>공용 파라미터 슬롯 이름 (공용 셀 참조가 아니면 null)</summary>
        public string SharedName => _tool.Owner.SharedNameOf(_param.Arg);
        public bool IsShared => SharedName != null;
        public string DisplayLabel => IsShared ? "🔗 " + Label : Label;
        public string LabelToolTip => IsShared ? $"공용 파라미터 '{SharedName}' ({_param.Arg.Text}) - 값을 바꾸면 연결된 툴 전부 반영"
            : HasEasyTip ? EasyTip + "\n(" + FullName + ")" : FullName;

        /// <summary>식(참조) 칸 + 참조 셀 값 칸 (셀 1개 참조가 아니면 식 칸만)</summary>
        public bool ShowExpressionBox => IsLocked;

        public bool CanLink => IsShared || !IsLocked;
        public bool CanUnlink => IsShared;

        internal void NotifyMode()
        {
            OnPropertyChanged(nameof(ShowExpressionBox));
            OnPropertyChanged(nameof(CanLink));
            OnPropertyChanged(nameof(CanUnlink));
            OnPropertyChanged(nameof(IsButtonRef));
            OnPropertyChanged(nameof(ButtonCaption));
            OnPropertyChanged(nameof(ShowLinkedValueBox));
            OnPropertyChanged(nameof(CanCreateButton));
        }

        // ---- 버튼 셀 참조 (Train Event 등) ----
        private string RefExpression => _tool.Owner.RefExpressionOf(_param.Arg);

        /// <summary>참조 셀이 Button(...) 이면 값 칸 대신 누르는 버튼 표시</summary>
        public bool IsButtonRef
        {
            get
            {
                string e = RefExpression;
                return e != null && System.Text.RegularExpressions.Regex.IsMatch(e.Trim(), @"^Button\s*\(", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
        }

        /// <summary>버튼 글자 = Button("이름") 의 이름</summary>
        public string ButtonCaption
        {
            get
            {
                if (!IsButtonRef) return null;
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(RefExpression, "\"([^\"]*)\"");
                return "▶ " + (m.Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : "누르기");
            }
        }

        public bool ShowLinkedValueBox => HasLinkedValue && !IsButtonRef;

        /// <summary>트리거 인자(Event, Train Event 등)가 아무 셀도 참조하지 않으면(상수 0 등) [버튼 만들기]. A0 등 참조 중이면 건드리지 않음</summary>
        public bool CanCreateButton => _param.Arg.Kind != CellArgKind.Reference && FullName.IndexOf("Event", StringComparison.OrdinalIgnoreCase) >= 0;

        internal async void PressButton()
        {
            if (IsButtonRef) await _tool.Owner.PressButtonAsync(_tool, this, _param.Arg.Text.Replace("$", ""));
        }

        internal async void CreateButton()
        {
            await _tool.Owner.CreateButtonAsync(_tool, this);
        }

        internal async void Unlink()
        {
            await _tool.Owner.UnlinkParamAsync(_tool, this);
        }
        public bool IsEnum => !IsLocked && Options != null;
        public bool IsNumber => !IsLocked && !IsEnum && _param.Arg.Kind == CellArgKind.Number;
        public bool IsText => !IsLocked && !IsEnum && !IsNumber;
        public bool HasRange => IsNumber && _param.Def != null && _param.Def.Min.HasValue && _param.Def.Max.HasValue && _param.Def.Max > _param.Def.Min;
        public double Min => HasRange ? _param.Def.Min.Value : 0;
        public double Max => HasRange ? _param.Def.Max.Value : 0;
        public bool IsChanged => _param.Arg.Text != _originalText;

        // 범위가 정수(0~100 등)이고 현재 값도 정수면 슬라이더를 정수 단위로
        public bool IsInteger => HasRange && Min % 1 == 0 && Max % 1 == 0 && _param.Arg.Number % 1 == 0;

        /// <summary>잠긴 인자는 참조/수식 원문, 나머지는 리터럴 값</summary>
        public string Text
        {
            get => _param.Arg.Text;
            set
            {
                if (value == null || value == _param.Arg.Text) return;
                string text = value.Trim();
                // 숫자 칸에는 숫자만 (잘못 입력하면 원래 값 표시로 복귀)
                double d = 0;
                if (IsNumber && !double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d))
                {
                    OnPropertyChanged();
                    return;
                }
                if (IsNumber) text = CellExpression.FormatNumber(d);
                Apply(text);
            }
        }

        public double SliderValue
        {
            get => _param.Arg.Number;
            set
            {
                string text = CellExpression.FormatNumber(Math.Round(value, IsInteger ? 0 : 3));
                if (text != _param.Arg.Text) Apply(text);
            }
        }

        public OptionItem SelectedOption
        {
            get
            {
                if (Options == null) return null;
                string key = CellExpression.FormatNumber(_param.Arg.Number);
                return Options.FirstOrDefault(o => o.Value == key);
            }
            set
            {
                if (value != null) Apply(value.Value);
            }
        }

        private async void Apply(string text)
        {
            await _tool.ApplyAsync(this, text);
        }

        internal void Reload(ToolParam param)
        {
            _param = param;
            OnPropertyChanged(nameof(Text));
            OnPropertyChanged(nameof(SliderValue));
            OnPropertyChanged(nameof(SelectedOption));
            OnPropertyChanged(nameof(IsChanged));
            OnPropertyChanged(nameof(HelpFacts));
            OnPropertyChanged(nameof(LinkedValue));
            OnPropertyChanged(nameof(HasLinkedValue));
            OnPropertyChanged(nameof(IsLocked));
            OnPropertyChanged(nameof(IsNumber));
            OnPropertyChanged(nameof(IsEnum));
            OnPropertyChanged(nameof(IsText));
            OnPropertyChanged(nameof(HasRange));
            OnPropertyChanged(nameof(SharedName));
            OnPropertyChanged(nameof(IsShared));
            OnPropertyChanged(nameof(DisplayLabel));
            OnPropertyChanged(nameof(LabelToolTip));
            NotifyMode();
        }
    }
}
