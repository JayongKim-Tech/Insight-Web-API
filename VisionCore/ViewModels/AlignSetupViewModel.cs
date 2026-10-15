using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using Cognex.InSight.Web;
using VisionCore.Models;
using VisionCore.Models.JobEditor;

namespace VisionCore.ViewModels
{
    /// <summary>
    /// ToolSetup "캘리브레이션" / "원점 등록" / "데이터 맵" 탭.
    /// 공통 템플릿의 고정 셀(AlignTemplate)을 읽고, 버튼 셀 누르기(SetCellValue 1) · EditInt 값 · 식 수정으로 센서에 반영.
    /// </summary>
    public class AlignSetupViewModel : ViewModelBase
    {
        private readonly ToolSetupViewModel _owner;
        private readonly CvsInSight _sensor;
        private Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private AlignLayout _layout = new AlignLayout();
        private bool _loaded;

        public ObservableCollection<CalStepViewModel> CalSteps { get; } = new ObservableCollection<CalStepViewModel>();
        public ObservableCollection<AlignValueRow> Coefs { get; } = new ObservableCollection<AlignValueRow>();
        public ObservableCollection<AlignValueRow> LiveRows { get; } = new ObservableCollection<AlignValueRow>();
        public ObservableCollection<OriginRowViewModel> Origins { get; } = new ObservableCollection<OriginRowViewModel>();
        public ObservableCollection<DataSlotViewModel> DataSlots { get; } = new ObservableCollection<DataSlotViewModel>();

        public ICommand ReloadCommand { get; }
        public ICommand TriggerCommand { get; }

        private string _summary = "탭을 열면 잡의 얼라인 / 데이터 맵 셀을 읽습니다.";
        public string Summary { get => _summary; private set { _summary = value; OnPropertyChanged(); } }

        private string _calCheck = "";
        /// <summary>계수 점검 결과 (det, 축별 px/mm, 축 직각도)</summary>
        public string CalCheck { get => _calCheck; private set { _calCheck = value; OnPropertyChanged(); } }

        private bool _calCheckBad;
        public bool CalCheckBad { get => _calCheckBad; private set { _calCheckBad = value; OnPropertyChanged(); } }

        private bool _hasCalibration, _hasOriginTable, _hasDataMap;
        public bool HasCalibration { get => _hasCalibration; private set { _hasCalibration = value; OnPropertyChanged(); OnPropertyChanged(nameof(NoCalibration)); } }
        public bool NoCalibration => !_hasCalibration;
        public bool HasOriginTable { get => _hasOriginTable; private set { _hasOriginTable = value; OnPropertyChanged(); OnPropertyChanged(nameof(NoOriginTable)); } }
        public bool NoOriginTable => !_hasOriginTable;
        public bool HasDataMap { get => _hasDataMap; private set { _hasDataMap = value; OnPropertyChanged(); OnPropertyChanged(nameof(NoDataMap)); } }
        public bool NoDataMap => !_hasDataMap;

        private string _outputInfo = "";
        /// <summary>Q2 출력 버퍼 요약 (항목 수, 형식)</summary>
        public string OutputInfo { get => _outputInfo; private set { _outputInfo = value; OnPropertyChanged(); } }

        public AlignSetupViewModel(ToolSetupViewModel owner, CvsInSight sensor)
        {
            _owner = owner;
            _sensor = sensor;
            ReloadCommand = new RelayCommand(async o => await LoadAsync(true));
            TriggerCommand = new RelayCommand(async o => await TriggerAsync());
        }

        /// <summary>처음 탭을 열 때 1번 (force = 새로고침 버튼)</summary>
        public async Task LoadAsync(bool force)
        {
            if (_loaded && !force) return;
            if (!_sensor.Connected) { Summary = "센서가 연결되어 있지 않습니다."; return; }
            _loaded = true;

            Summary = "셀 읽는 중...";
            await _owner.SensorLock.WaitAsync();
            try
            {
                // 포인트별 블록 이름 행 (포인트 표 Name 식 기준, 블록 간격이 달라도 맞게)
                List<PointInfo> points = await JobToolScanner.LoadPointsAsync(_sensor, ConfigModel.Instance.PointCount);
                _map = JobToolScanner.ReadCellMap(_sensor);
                _layout = AlignLayout.Detect(_map);

                BuildCalibration();
                BuildOrigins(points);
                await BuildDataMapAsync();
                UpdateValues();

                Summary = (HasCalibration ? $"캘리브레이션 {_layout.CalSteps[0].Row}~{_layout.CalSteps.Last().Row}행" : "캘리브레이션 없음")
                          + (HasOriginTable ? $" · 원점 표 {AlignLayout.Loc(_layout.OrgXCol, _layout.TableFirstRow)}부터" : " · 원점 표 없음")
                          + (HasDataMap ? $" · 데이터 맵 {DataSlots.Count}슬롯 · 출력 버퍼 {_layout.OutputBufferCell}" : " · 데이터 맵 없음");
            }
            catch (Exception ex)
            {
                Summary = "읽기 실패: " + ex.Message;
                _owner.Log("얼라인 셀 읽기 실패: " + ex.Message, true);
            }
            finally
            {
                _owner.SensorLock.Release();
            }
        }

        private async Task TriggerAsync()
        {
            if (!_sensor.Connected) return;
            try
            {
                await _sensor.ManualAcquire();
                await Task.Delay(500); // 검사 완료 대기
                await RefreshValuesAsync();
            }
            catch (Exception ex)
            {
                _owner.Log("촬영 실패: " + ex.Message, true);
            }
        }

        /// <summary>최신 결과만 다시 읽어 표시값 갱신 (식은 그대로)</summary>
        public async Task RefreshValuesAsync()
        {
            await _owner.SensorLock.WaitAsync();
            try
            {
                await _sensor.GetLatestResult();
                _map = JobToolScanner.ReadCellMap(_sensor);
            }
            finally
            {
                _owner.SensorLock.Release();
            }
            UpdateValues();
        }

        // ───────────── 구성 ─────────────

        private void BuildCalibration()
        {
            CalSteps.Clear();
            Coefs.Clear();
            LiveRows.Clear();
            HasCalibration = _layout.HasCalibration;
            if (!HasCalibration) return;

            foreach (CalStepDef d in _layout.CalSteps)
                CalSteps.Add(new CalStepViewModel(this, d));
            foreach (string[] c in _layout.Coefs)
                Coefs.Add(new AlignValueRow(Text(c[0]), c[1]));
            foreach (string[] c in _layout.LiveRows)
                LiveRows.Add(new AlignValueRow(Text(c[0]), c[1], c[2], c[3]));
        }

        private void BuildOrigins(List<PointInfo> points)
        {
            Origins.Clear();
            HasOriginTable = _layout.HasOriginTable;
            if (!HasOriginTable) return;

            foreach (PointInfo p in points)
                Origins.Add(new OriginRowViewModel(this, _layout, p.Index, _layout.TableFirstRow + p.Index, AlignLayout.PointCoordCells(_map, p.BlockRow)));
        }

        private async Task BuildDataMapAsync()
        {
            DataSlots.Clear();
            OutputInfo = "";
            HasDataMap = _layout.HasDataMap;
            if (!HasDataMap) return;
            string bufferCell = _layout.OutputBufferCell;

            // 출력 버퍼에 들어간 순서 (Q2 식의 값 인자) → 슬롯별 PLC 항목 번호
            List<string> outputs = new List<string>();
            bool allWords = false;
            try
            {
                string q2 = await _sensor.GetCellExpression(bufferCell) ?? "";
                Match m = Regex.Match(q2, "FormatOutputBuffer\\(\\s*[^,]*,\\s*\"([^\"]*)\"\\s*,(.*)\\)\\s*$", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    string[] types = m.Groups[1].Value.Split(':');
                    allWords = types.All(t => t.Trim().Equals("il", StringComparison.OrdinalIgnoreCase));
                    outputs = m.Groups[2].Value.Split(',').Select(a => a.Replace("$", "").Trim().ToUpperInvariant()).ToList();
                    OutputInfo = $"출력 버퍼 {bufferCell}: 항목 {outputs.Count}개" + (allWords ? " (모두 16-bit 정수, 항목 n = Word n)" : $" (형식 {m.Groups[1].Value})");
                }
                else OutputInfo = $"출력 버퍼 {bufferCell} 식을 해석하지 못했습니다.";
            }
            catch (Exception ex) { OutputInfo = "출력 버퍼 읽기 실패: " + ex.Message; }

            AlignLayout L = _layout;
            foreach (int r in L.DataRows)
            {
                string source = "";
                try { source = await _sensor.GetCellExpression(AlignLayout.Loc(L.DataSourceCol, r)) ?? ""; } catch { }
                string outCell = AlignLayout.Loc(L.DataOutCol, r);
                int index = outputs.IndexOf(outCell);
                string plc = index < 0 ? "미출력" : (allWords ? "Word " : "항목 ") + index;
                DataSlots.Add(new DataSlotViewModel(this, L, r, source, plc));
            }
        }

        private void UpdateValues()
        {
            foreach (CalStepViewModel s in CalSteps) s.Update();
            foreach (AlignValueRow r in Coefs) r.Update(this);
            foreach (AlignValueRow r in LiveRows) r.Update(this);
            foreach (OriginRowViewModel o in Origins) o.Update();
            foreach (DataSlotViewModel d in DataSlots) d.Update();
            UpdateCalCheck();
        }

        /// <summary>
        /// 계수 점검: a,c = 로봇 X 1mm당 이미지 이동, b,d = 로봇 Y 1mm당 이미지 이동.
        /// det ≈ 0 이면 캡처 오류, 두 축 벡터 사이 각도가 90°에서 멀면 캡처나 로봇 축 확인.
        /// </summary>
        private void UpdateCalCheck()
        {
            if (!HasCalibration) { CalCheck = ""; return; }
            double a = 0, c = 0, b = 0, d = 0, det = 0;
            List<string[]> k = _layout.Coefs;
            if (k.Count < 5 || !TryNumber(k[0][1], out a) || !TryNumber(k[1][1], out c) || !TryNumber(k[2][1], out b) || !TryNumber(k[3][1], out d) || !TryNumber(k[4][1], out det))
            {
                CalCheck = "계수가 아직 없습니다. 원점 → X1 → X2 → Y1 → Y2 순서로 캡처하세요.";
                CalCheckBad = true;
                return;
            }
            double sx = Math.Sqrt(a * a + c * c), sy = Math.Sqrt(b * b + d * d);
            double angle = sx > 0 && sy > 0 ? Math.Acos(Math.Max(-1, Math.Min(1, (a * b + c * d) / (sx * sy)))) * 180 / Math.PI : 0;
            bool bad = Math.Abs(det) < 1 || Math.Abs(angle - 90) > 3;
            CalCheck = $"로봇 X: {sx:0.000} px/mm ({(sx > 0 ? 1 / sx : 0):0.00000} mm/px)   로봇 Y: {sy:0.000} px/mm ({(sy > 0 ? 1 / sy : 0):0.00000} mm/px)\n"
                     + $"두 축 사이 각도 {angle:0.00}° (90°가 정상)   det {det:0.0}"
                     + (Math.Abs(det) < 1 ? "\n⚠ det가 0에 가까움: 버튼을 안 눌렀거나 로봇이 안 움직인 단계가 있습니다." : "")
                     + (Math.Abs(angle - 90) > 3 ? "\n⚠ 축이 직각이 아님: X 단계에 Y가 섞였거나 캡처가 잘못됐습니다." : "");
            CalCheckBad = bad;
        }

        // ───────────── 센서 반영 ─────────────

        /// <summary>버튼 셀 누르기 (캡처 / 원점 등록). 누른 뒤 결과 다시 읽음</summary>
        internal async Task PressAsync(string location, string what)
        {
            if (!_sensor.Connected) return;
            await _owner.SensorLock.WaitAsync();
            try
            {
                await _sensor.SetCellValue(location, "1");
                await Task.Delay(300);
            }
            catch (Exception ex)
            {
                _owner.Log($"{what} 실패 ({location}): " + ex.Message, true);
                return;
            }
            finally
            {
                _owner.SensorLock.Release();
            }
            await RefreshValuesAsync();
            _owner.MarkUnsaved();
            _owner.Log($"{what} ({location})", false);
        }

        /// <summary>EditInt / EditFloat 값 셀</summary>
        internal async Task<bool> SetValueAsync(string location, string value, string what)
        {
            return await ApplyAsync(location, what, value, () => _sensor.SetCellValue(location, value));
        }

        /// <summary>식 셀 (상수 / 참조 / 문자열)</summary>
        internal async Task<bool> SetExpressionAsync(string location, string expr, string what)
        {
            return await ApplyAsync(location, what, expr, () => _sensor.SetCellExpression(location, expr));
        }

        private async Task<bool> ApplyAsync(string location, string what, string newText, Func<Task> action)
        {
            if (!_sensor.Connected) return false;
            await _owner.SensorLock.WaitAsync();
            try { await action(); }
            catch (Exception ex)
            {
                _owner.Log($"{what} 변경 실패 ({location}): " + ex.Message, true);
                return false;
            }
            finally { _owner.SensorLock.Release(); }

            await RefreshValuesAsync();
            _owner.MarkUnsaved();
            _owner.Log($"{what} ({location}) → {newText}", false);
            Logger.Info($"[AlignSetup] {location} = {newText}");
            return true;
        }

        // ───────────── 셀 값 ─────────────

        internal string Text(string location)
        {
            string v;
            return _map.TryGetValue(location, out v) ? (v ?? "").Trim() : "";
        }

        internal bool TryNumber(string location, out double value)
        {
            return double.TryParse(Text(location), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>숫자면 소수 3자리, 아니면(#ERR, 빈 셀) 그대로</summary>
        internal string Num(string location)
        {
            double v;
            if (TryNumber(location, out v)) return v.ToString("0.###", CultureInfo.InvariantCulture);
            string t = Text(location);
            return t.StartsWith("{") ? "#ERR" : t;
        }
    }

    /// <summary>이름 + 값 1~3개 (계수, 원점→보정값 표)</summary>
    public class AlignValueRow : ViewModelBase
    {
        private readonly string[] _cells;
        public string Name { get; }
        public string Cells => string.Join(" ", _cells);

        private string _x = "", _y = "", _t = "";
        public string X { get => _x; private set { _x = value; OnPropertyChanged(); } }
        public string Y { get => _y; private set { _y = value; OnPropertyChanged(); } }
        public string T { get => _t; private set { _t = value; OnPropertyChanged(); } }

        public AlignValueRow(string name, params string[] cells)
        {
            Name = name;
            _cells = cells;
        }

        public void Update(AlignSetupViewModel vm)
        {
            X = _cells.Length > 0 ? vm.Num(_cells[0]) : "";
            Y = _cells.Length > 1 ? vm.Num(_cells[1]) : "";
            T = _cells.Length > 2 ? vm.Num(_cells[2]) : "";
        }
    }

    /// <summary>캘리브레이션 1단계: 로봇 이동량 입력 + 캡처 버튼 + 캡처된 이미지 좌표</summary>
    public class CalStepViewModel : ViewModelBase
    {
        private readonly AlignSetupViewModel _vm;
        private readonly CalStepDef _def;

        public string Name => _def.Name;
        public string ButtonCell => _def.ButtonCell;
        public bool HasJogXY => _def.JogX != null;
        public bool HasJogT => _def.JogT != null;
        public ICommand CaptureCommand { get; }

        private string _jogX = "", _jogY = "", _jogT = "", _capX = "", _capY = "", _capT = "";
        public string JogX { get => _jogX; set { Commit(_def.JogX, ref _jogX, value, nameof(JogX), "로봇 X 이동량"); } }
        public string JogY { get => _jogY; set { Commit(_def.JogY, ref _jogY, value, nameof(JogY), "로봇 Y 이동량"); } }
        public string JogT { get => _jogT; set { Commit(_def.JogT, ref _jogT, value, nameof(JogT), "로봇 θ 이동량"); } }
        public string CapX { get => _capX; private set { _capX = value; OnPropertyChanged(); } }
        public string CapY { get => _capY; private set { _capY = value; OnPropertyChanged(); } }
        public string CapT { get => _capT; private set { _capT = value; OnPropertyChanged(); } }

        public CalStepViewModel(AlignSetupViewModel vm, CalStepDef def)
        {
            _vm = vm;
            _def = def;
            CaptureCommand = new RelayCommand(async o => await _vm.PressAsync(ButtonCell, $"캘리브레이션 캡처 [{Name}]"));
        }

        public void Update()
        {
            Set(ref _jogX, _def.JogX, nameof(JogX));
            Set(ref _jogY, _def.JogY, nameof(JogY));
            Set(ref _jogT, _def.JogT, nameof(JogT));
            string cols = _def.CapturedCols;
            CapX = _vm.Num(cols[0].ToString() + _def.Row);
            CapY = _vm.Num(cols[1].ToString() + _def.Row);
            CapT = _vm.Num(cols[2].ToString() + _def.Row);
        }

        private void Set(ref string field, string cell, string prop)
        {
            field = cell != null ? _vm.Num(cell) : "";
            OnPropertyChanged(prop);
        }

        /// <summary>템플릿 EditInt(-30,30) 범위 밖이나 숫자가 아니면 원래 값으로 되돌림</summary>
        private void Commit(string cell, ref string field, string value, string prop, string what)
        {
            if (cell == null || value == field) return;
            int n;
            if (!int.TryParse((value ?? "").Trim(), out n) || n < -30 || n > 30)
            {
                OnPropertyChanged(prop);
                return;
            }
            field = n.ToString();
            _ = _vm.SetValueAsync(cell, field, $"캘리브레이션 [{Name}] {what}");
        }
    }

    /// <summary>포인트별 원점 1행: 현재 포인트 좌표 / 저장된 원점 / 등록 버튼</summary>
    public class OriginRowViewModel : ViewModelBase
    {
        private readonly AlignSetupViewModel _vm;
        private readonly AlignLayout _layout;
        private readonly int _tableRow;
        private readonly string[] _coordCells;   // 포인트 블록 X/Y/θ 값 셀 (못 찾으면 null)

        public string Code { get; }
        public string ButtonCell => AlignLayout.Loc(_layout.OriginButtonCol, _tableRow);
        public ICommand RegisterCommand { get; }

        private string _name = "", _pos = "", _curX = "", _curY = "", _curT = "", _orgX = "", _orgY = "", _orgT = "";
        private bool _isNow;
        public string Name { get => _name; private set { _name = value; OnPropertyChanged(); } }
        public string Pos { get => _pos; private set { _pos = value; OnPropertyChanged(); } }
        public bool IsNow { get => _isNow; private set { _isNow = value; OnPropertyChanged(); } }
        public string CurX { get => _curX; private set { _curX = value; OnPropertyChanged(); } }
        public string CurY { get => _curY; private set { _curY = value; OnPropertyChanged(); } }
        public string CurT { get => _curT; private set { _curT = value; OnPropertyChanged(); } }
        public string OrgX { get => _orgX; private set { _orgX = value; OnPropertyChanged(); } }
        public string OrgY { get => _orgY; private set { _orgY = value; OnPropertyChanged(); } }
        public string OrgT { get => _orgT; private set { _orgT = value; OnPropertyChanged(); } }

        public OriginRowViewModel(AlignSetupViewModel vm, AlignLayout layout, int index, int tableRow, string[] coordCells)
        {
            _vm = vm;
            _layout = layout;
            _tableRow = tableRow;
            _coordCells = coordCells;
            Code = "P" + (index + 1).ToString("00");
            RegisterCommand = new RelayCommand(async o => await _vm.PressAsync(ButtonCell, $"{Code} 원점 등록"));
        }

        public void Update()
        {
            Name = _layout.NameCol >= 0 ? _vm.Text(AlignLayout.Loc(_layout.NameCol, _tableRow)) : "";
            Pos = _layout.PosCol >= 0 ? _vm.Text(AlignLayout.Loc(_layout.PosCol, _tableRow)) : "";
            IsNow = _layout.IsNowCol >= 0 && _vm.Text(AlignLayout.Loc(_layout.IsNowCol, _tableRow)) == "1";
            CurX = _coordCells != null ? _vm.Num(_coordCells[0]) : "-";
            CurY = _coordCells != null ? _vm.Num(_coordCells[1]) : "-";
            CurT = _coordCells != null ? _vm.Num(_coordCells[2]) : "-";
            int c = _layout.OrgXCol;
            OrgX = _vm.Num(AlignLayout.Loc(c, _tableRow));
            OrgY = _vm.Num(AlignLayout.Loc(c + 1, _tableRow));
            OrgT = _vm.Num(AlignLayout.Loc(c + 2, _tableRow));
        }
    }

    /// <summary>데이터 맵 1슬롯: 이름 / 소스 식 / 배율 → 출력값, PLC 항목 번호</summary>
    public class DataSlotViewModel : ViewModelBase
    {
        private readonly AlignSetupViewModel _vm;
        private readonly AlignLayout _layout;
        private readonly int _row;

        public string No { get; }
        public string Plc { get; }
        public bool CanEditName => _layout.DataNameCol >= 0;
        public bool CanEditScale => _layout.DataScaleCol >= 0;

        private string Cell(int col) => AlignLayout.Loc(col, _row);

        private string _name, _source, _scale, _out = "", _valid = "";
        public string Name
        {
            get => _name;
            set
            {
                string v = (value ?? "").Replace("\"", "").Trim();
                if (v == _name || !CanEditName) return;
                _name = v;
                _ = _vm.SetExpressionAsync(Cell(_layout.DataNameCol), "\"" + v + "\"", $"데이터 맵 {No} 이름");
            }
        }
        public string Source
        {
            get => _source;
            set
            {
                string v = (value ?? "").Trim().TrimStart('=');
                if (v.Length == 0) v = "0";
                if (v == _source) return;
                _source = v;
                _ = _vm.SetExpressionAsync(Cell(_layout.DataSourceCol), v, $"데이터 맵 {No} 소스");
            }
        }
        public string Scale
        {
            get => _scale;
            set
            {
                double d;
                string v = (value ?? "").Trim();
                if (v == _scale || !CanEditScale || !double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) { OnPropertyChanged(); return; }
                _scale = v;
                _ = _vm.SetExpressionAsync(Cell(_layout.DataScaleCol), v, $"데이터 맵 {No} 배율");
            }
        }
        public string Out { get => _out; private set { _out = value; OnPropertyChanged(); } }
        public string Valid { get => _valid; private set { _valid = value; OnPropertyChanged(); } }

        public DataSlotViewModel(AlignSetupViewModel vm, AlignLayout layout, int row, string source, string plc)
        {
            _vm = vm;
            _layout = layout;
            _row = row;
            No = vm.Text(Cell(layout.DataNoCol));
            _name = CanEditName ? vm.Text(Cell(layout.DataNameCol)) : "";
            _source = source.Trim().TrimStart('=');
            _scale = CanEditScale ? vm.Text(Cell(layout.DataScaleCol)) : "";
            Plc = plc;
        }

        public void Update()
        {
            Out = _vm.Num(Cell(_layout.DataOutCol));
            Valid = _layout.DataValidCol < 0 ? "" : _vm.Text(Cell(_layout.DataValidCol)) == "1" ? "OK" : "-";
        }
    }
}
