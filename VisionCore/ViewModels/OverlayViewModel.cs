using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Cognex.InSight.Remoting.Serialization;
using Cognex.InSight.Web;
using Cognex.InSight.Web.Controls;
using Newtonsoft.Json.Linq;
using VisionCore.Models;
using VisionCore.Models.JobEditor;

namespace VisionCore.ViewModels
{
    /// <summary>
    /// ToolSetup "오버레이 / 레이아웃" 탭: 잡의 모든 EditPoint / EditRegion / EditLine / EditCircle / EditAnnulus / EditPolygon 셀을 모아
    /// 이미지 위에서 바로 편집. 그 셀을 참조하는 셀(PlotString 등)이 그린 그래픽은 같이 움직이는 미리보기로 표시.
    /// 이미지/드래그 화면은 ShapeEditorControl (WinForms, View 코드비하인드에서 연결).
    /// </summary>
    public class OverlayViewModel : ViewModelBase
    {
        private static readonly Regex _refRegex = new Regex(@"(?<![A-Za-z0-9_])\$?([A-Za-z])\$?(\d{1,3})(?![0-9(])", RegexOptions.Compiled);

        private readonly ToolSetupViewModel _owner;
        private readonly CvsInSight _sensor;

        public ObservableCollection<OverlayItemViewModel> Items { get; } = new ObservableCollection<OverlayItemViewModel>();
        public ICommand ReloadCommand { get; }

        /// <summary>센서 그래픽 (View가 편집 화면에 넘김)</summary>
        public CvsCogShape[] Graphics { get; private set; } = new CvsCogShape[0];

        /// <summary>그래픽을 새로 받았을 때 (View → ShapeEditorControl.SetContext)</summary>
        public event Action GraphicsChanged;

        /// <summary>이미지까지 다시 받아야 할 때 (View → ShapeEditorControl.ReloadAsync)</summary>
        public event Action ImageReloadRequested;

        private OverlayItemViewModel _selectedItem;
        public OverlayItemViewModel SelectedItem
        {
            get => _selectedItem;
            set { if (_selectedItem != value) { _selectedItem = value; OnPropertyChanged(); } }
        }

        private string _summary = "탭을 열면 잡의 Edit 그래픽 셀을 읽습니다.";
        public string Summary
        {
            get => _summary;
            private set { _summary = value; OnPropertyChanged(); }
        }

        private bool _loaded;

        public OverlayViewModel(ToolSetupViewModel owner, CvsInSight sensor)
        {
            _owner = owner;
            _sensor = sensor;
            ReloadCommand = new RelayCommand(async o => await LoadAsync(true));
        }

        /// <summary>처음 탭을 열 때 1번 (force = 새로고침 버튼)</summary>
        public async Task LoadAsync(bool force)
        {
            if (_loaded && !force) return;
            if (!_sensor.Connected) { Summary = "센서가 연결되어 있지 않습니다."; return; }
            _loaded = true;

            string keep = _selectedItem?.Location;
            Summary = "Edit 그래픽 셀 읽는 중...";
            await _owner.SensorLock.WaitAsync();
            try
            {
                await _sensor.GetLatestResult();
                Dictionary<string, string> map = JobToolScanner.ReadCellMap(_sensor);
                List<OverlayItemViewModel> items = ReadEditCells(map);
                Graphics = await _sensor.GetGraphicsAsync() ?? new CvsCogShape[0];
                await FindLinkedCellsAsync(items);

                Items.Clear();
                foreach (OverlayItemViewModel i in items) Items.Add(i);
                SelectedItem = Items.FirstOrDefault(i => i.Location == keep) ?? Items.FirstOrDefault();
                Summary = $"Edit 그래픽 셀 {Items.Count}개 · 연결된 그래픽이 있는 셀 {Items.Count(i => i.Linked.Count > 0)}개";
            }
            catch (Exception ex)
            {
                Summary = "읽기 실패: " + ex.Message;
                _owner.Log("오버레이 셀 읽기 실패: " + ex.Message, true);
            }
            finally
            {
                _owner.SensorLock.Release();
            }
            GraphicsChanged?.Invoke();
            ImageReloadRequested?.Invoke();
        }

        /// <summary>최신 결과에서 값이 도형(EditPoint/EditRegion 등)인 셀</summary>
        private List<OverlayItemViewModel> ReadEditCells(Dictionary<string, string> map)
        {
            var list = new List<OverlayItemViewModel>();
            JArray cells = _sensor.Results != null ? _sensor.Results["cells"] as JArray : null;
            if (cells == null) return list;

            foreach (JToken c in cells)
            {
                HmiCellResult cell;
                try { cell = CvsInSight.JsonSerializer.DeserializeObject(c.ToString()) as HmiCellResult; }
                catch { continue; }
                if (cell == null || string.IsNullOrEmpty(cell.Location) || !ShapeEditorControl.IsSupported(cell.Value)) continue;

                string loc = ShapeEditorControl.Normalize(cell.Location);
                list.Add(new OverlayItemViewModel(loc, (CvsCogShape)cell.Value, GroupOf(loc), CaptionOf(loc, map)));
            }
            return list.OrderBy(i => i.Row).ThenBy(i => i.Location).ToList();
        }

        private static bool TryParse(string loc, out int col, out int row)
        {
            col = row = -1;
            Match m = Regex.Match(loc ?? "", @"^([A-Z])(\d{1,3})$");
            if (!m.Success) return false;
            col = m.Groups[1].Value[0] - 'A';
            row = int.Parse(m.Groups[2].Value);
            return true;
        }

        /// <summary>템플릿 포인트 블록(44행부터 18행 간격) → "P03 이름", 그 외 "공통"</summary>
        private string GroupOf(string loc)
        {
            int col, row;
            if (!TryParse(loc, out col, out row) || row < JobTemplateLayout.BlockFirstRow) return "공통";
            int index = (row - JobTemplateLayout.BlockFirstRow) / JobTemplateLayout.BlockStride;
            if (index >= JobTemplateLayout.PointCount) return "공통";
            PointItemViewModel p = _owner.Points.FirstOrDefault(x => x.Info.Index == index);
            return "P" + (index + 1).ToString("00") + (p != null && !string.IsNullOrEmpty(p.Name) ? " " + p.Name : "");
        }

        /// <summary>왼쪽 칸에 글자가 있으면 이름표로 사용 (예: B50 "Mark 위치" | C50 EditPoint)</summary>
        private static string CaptionOf(string loc, Dictionary<string, string> map)
        {
            int col, row;
            if (!TryParse(loc, out col, out row) || col == 0) return null;
            string left;
            double d;
            if (!map.TryGetValue(((char)('A' + col - 1)).ToString() + row, out left)) return null;
            left = (left ?? "").Trim();
            if (left.Length == 0 || left.StartsWith("{") || left.StartsWith("[") || double.TryParse(left, out d)) return null;
            return left.Length > 30 ? left.Substring(0, 30) + "…" : left;
        }

        /// <summary>
        /// 그래픽을 그린 셀들의 식을 읽어서, Edit 셀을 (직접 또는 중간 셀 1단계를 거쳐) 참조하는 셀을 찾음
        /// </summary>
        private async Task FindLinkedCellsAsync(List<OverlayItemViewModel> items)
        {
            var graphicCells = Graphics.Select(g => ShapeEditorControl.Normalize(g?.CellLocation)).Where(l => l.Length > 0).Distinct().ToList();
            var expr = await ReadExpressionsAsync(graphicCells);

            // 중간 셀 1단계 (예: PlotString이 D50 을 참조하고 D50 = C50.X + 10)
            var middle = expr.Values.SelectMany(References).Distinct().Where(l => !expr.ContainsKey(l)).ToList();
            foreach (var kv in await ReadExpressionsAsync(middle)) expr[kv.Key] = kv.Value;

            var editSet = new HashSet<string>(items.Select(i => i.Location));
            foreach (OverlayItemViewModel item in items)
            {
                foreach (string g in graphicCells)
                {
                    if (g == item.Location || !expr.ContainsKey(g)) continue;
                    List<string> refs = References(expr[g]).ToList();
                    string via = refs.Contains(item.Location) ? null
                        : refs.FirstOrDefault(r => !editSet.Contains(r) && expr.ContainsKey(r) && References(expr[r]).Contains(item.Location));
                    if (refs.Contains(item.Location) || via != null)
                        item.Linked.Add(new LinkedCell { Location = g, Via = via, Owner = this, Expression = expr[g] });
                }
            }
        }

        private async Task<Dictionary<string, string>> ReadExpressionsAsync(List<string> locations)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var values = new string[locations.Count];
            using (var gate = new SemaphoreSlim(16))
            {
                await Task.WhenAll(locations.Select(async (loc, i) =>
                {
                    await gate.WaitAsync().ConfigureAwait(false);
                    try { values[i] = await _sensor.GetCellExpression(loc).ConfigureAwait(false); }
                    catch { values[i] = null; }
                    finally { gate.Release(); }
                }));
            }
            for (int i = 0; i < locations.Count; i++)
                if (!string.IsNullOrEmpty(values[i])) result[locations[i]] = values[i];
            return result;
        }

        /// <summary>식 안의 셀 참조 (문자열 리터럴 제외)</summary>
        private static IEnumerable<string> References(string expression)
        {
            if (string.IsNullOrEmpty(expression)) yield break;
            string code = Regex.Replace(expression, "\"[^\"]*\"", "\"\"");
            foreach (Match m in _refRegex.Matches(code))
                yield return (m.Groups[1].Value + m.Groups[2].Value).ToUpperInvariant();
        }

        /// <summary>그래픽 글자 표시 배율 (화면만, 잡 영향 없음). 메인 디스플레이도 같은 값 사용</summary>
        public double TextScale
        {
            get => ConfigModel.Instance.GraphicsTextScale;
            set
            {
                ConfigModel.Instance.GraphicsTextScale = value;
                ConfigModel.Instance.SaveDisplaySettings();
                OnPropertyChanged();
                OnPropertyChanged(nameof(TextScaleText));
                RedrawRequested?.Invoke();
            }
        }

        public string TextScaleText => "×" + TextScale.ToString("0.0");

        /// <summary>표시 설정이 바뀌어 다시 그려야 할 때</summary>
        public event Action RedrawRequested;

        /// <summary>연결된 PlotString 등의 Font Size 인자 변경 → 센서 적용 후 그래픽 다시 받기</summary>
        internal async Task SetFontSizeAsync(LinkedCell cell, string value)
        {
            double d;
            if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d) || d <= 0)
            {
                _owner.Log($"{cell.Location} 글자 크기는 0보다 큰 숫자로 입력하세요: {value}", true);
                cell.Expression = cell.Expression;   // 표시 되돌림
                return;
            }

            await _owner.SensorLock.WaitAsync();
            string old = cell.FontSize;
            try
            {
                string newExpr = cell.Tool.SetText(cell.FontSizeIndex, CellExpression.FormatNumber(d));
                await _sensor.SetCellExpression(cell.Location, newExpr);
                cell.Expression = newExpr;
                _owner.MarkUnsaved();
                _owner.Log($"{cell.Location} 글자 크기: {old} → {CellExpression.FormatNumber(d)}", false);
                Logger.Info($"[ToolSetup] {cell.Location} Font Size {old} -> {d}");

                await _sensor.GetLatestResult();
                Graphics = await _sensor.GetGraphicsAsync() ?? new CvsCogShape[0];
            }
            catch (Exception ex)
            {
                cell.Expression = cell.Expression;
                _owner.Log($"{cell.Location} 글자 크기 적용 실패: {ex.Message}", true);
            }
            finally
            {
                _owner.SensorLock.Release();
            }
            GraphicsChanged?.Invoke();
        }

        /// <summary>
        /// 편집 화면에서 놓을 때마다: 센서에 값 적용 → 다시 계산된 그래픽을 받아 연결 그래픽 실제 위치로
        /// </summary>
        public async Task CommitAsync(OverlayItemViewModel item, CvsCogShape shape)
        {
            if (item == null || shape == null) return;
            await _owner.SensorLock.WaitAsync();
            try
            {
                CvsCogShape editable = ShapeEditorControl.ToEditable(shape);
                await _sensor.SetCellValue(item.Location, editable);
                item.Update(shape);
                _owner.MarkUnsaved();
                _owner.Log($"{item.Location} {item.Kind} ({item.Group}) → {item.ValueText}", false);
                Logger.Info($"[ToolSetup] overlay {item.Location} -> {item.ValueText}");

                await _sensor.GetLatestResult();
                Graphics = await _sensor.GetGraphicsAsync() ?? new CvsCogShape[0];
            }
            catch (Exception ex)
            {
                _owner.Log($"{item.Location} 적용 실패: {ex.Message}", true);
                Logger.Error($"[ToolSetup] overlay {item.Location} 적용 실패: {ex.Message}");
            }
            finally
            {
                _owner.SensorLock.Release();
            }
            GraphicsChanged?.Invoke();
        }
    }

    public class LinkedCell : ViewModelBase
    {
        public string Location { get; set; }
        public string Via { get; set; }   // 중간 셀을 거쳐 참조하면 그 셀
        internal OverlayViewModel Owner { get; set; }
        internal ToolCell Tool { get; private set; }

        private string _expression;
        public string Expression
        {
            get => _expression;
            set
            {
                _expression = value;
                Tool = ToolCell.TryCreate(Location, value, ToolCatalog.Default);
                OnPropertyChanged();
                OnPropertyChanged(nameof(Text));
                OnPropertyChanged(nameof(FontSize));
                OnPropertyChanged(nameof(HasFontSize));
            }
        }

        public string Text => Location + (Via != null ? "  (" + Via + " 경유)" : "") + "   " + Expression;

        /// <summary>PlotString 등 "Font Size" 인자 위치 (없으면 -1)</summary>
        internal int FontSizeIndex
        {
            get
            {
                if (Tool == null) return -1;
                ToolParam p = Tool.Params.FirstOrDefault(x => x.Def != null && x.Def.Name == "Font Size");
                return p != null ? p.Index : -1;
            }
        }

        public bool HasFontSize => FontSizeIndex >= 0;

        /// <summary>잡의 실제 글자 크기 (Enter = 센서에 적용)</summary>
        public string FontSize
        {
            get => HasFontSize ? Tool.Params[FontSizeIndex].Arg.Text : null;
            set
            {
                if (!HasFontSize || value == null || value.Trim() == FontSize) return;
                ApplyFontSize(value.Trim());
            }
        }

        private async void ApplyFontSize(string value)
        {
            if (Owner != null) await Owner.SetFontSizeAsync(this, value);
        }
    }

    public class OverlayItemViewModel : ViewModelBase
    {
        public string Location { get; }
        public string Group { get; }
        public string Caption { get; }
        public int Row { get; }
        public CvsCogShape Shape { get; private set; }
        public List<LinkedCell> Linked { get; } = new List<LinkedCell>();

        public OverlayItemViewModel(string location, CvsCogShape shape, string group, string caption)
        {
            Location = location;
            Shape = shape;
            Group = group;
            Caption = caption;
            int row;
            Row = int.TryParse(location.Substring(1), out row) ? row : 0;
        }

        public string Kind => Shape.GetType().Name.Replace("CvsCog", "");
        public string Title => Location + "  Edit" + Kind;
        public string LinkedSummary => Linked.Count == 0 ? "연결된 그래픽 없음" : "연결된 그래픽 " + Linked.Count + "개";

        public string ValueText
        {
            get
            {
                Func<double, string> f = v => Math.Round(v, 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (Shape is CvsCogPoint p) return $"X {f(p.X)}, Y {f(p.Y)}";
                if (Shape is CvsCogRegion r) return $"X {f(r.X)}, Y {f(r.Y)}, W {f(r.Width)}, H {f(r.Height)}, A {f(r.Angle)}";
                if (Shape is CvsCogLine l) return $"({f(l.X0)}, {f(l.Y0)}) → ({f(l.X1)}, {f(l.Y1)})";
                if (Shape is CvsCogCircle c) return $"X {f(c.X)}, Y {f(c.Y)}, R {f(c.Radius)}";
                if (Shape is CvsCogAnnulus a) return $"X {f(a.X)}, Y {f(a.Y)}, R {f(a.InnerRadius)}~{f(a.OuterRadius)}";
                if (Shape is CvsCogPolygon pg) return $"꼭짓점 {(pg.Points?.Length ?? 0) / 2}개";
                return "";
            }
        }

        internal void Update(CvsCogShape shape)
        {
            Shape = shape;
            OnPropertyChanged(nameof(ValueText));
        }
    }
}
