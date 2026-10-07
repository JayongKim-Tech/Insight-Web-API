using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using VisionCore.Models;

namespace VisionCore.ViewModels
{
    /// <summary>
    /// 센서 1대의 최신 판정 (결과 패널 하단 큰 OK/NG 카드)
    /// </summary>
    public class LatestResult
    {
        public string Sensor { get; }
        public string Result { get; }
        public bool IsNg { get; }
        public string Point { get; }
        public string Time { get; }

        public LatestResult(string sensor, string result, bool isNg, string point, string time)
        {
            Sensor = sensor;
            Result = result;
            IsNg = isNg;
            Point = point;
            Time = time;
        }
    }

    /// <summary>
    /// 결과 리스트 컬럼 1개 (헤더 표시 + 필터 적용 여부)
    /// </summary>
    public class ResultColumn : ViewModelBase
    {
        public string Name { get; }
        public int Index { get; }

        private bool _isFiltered;
        public bool IsFiltered { get => _isFiltered; set { _isFiltered = value; OnPropertyChanged(); } }

        public ResultColumn(string name, int index)
        {
            Name = name;
            Index = index;
        }
    }

    /// <summary>
    /// 헤더 필터 팝업의 값 1개 (체크된 값만 표시)
    /// </summary>
    public class FilterValueItem : ViewModelBase
    {
        public string Value { get; }
        public int Count { get; }
        public string Display => string.IsNullOrEmpty(Value) ? "(빈 값)" : Value;

        private bool _isChecked;
        public bool IsChecked { get => _isChecked; set { _isChecked = value; OnPropertyChanged(); } }

        public FilterValueItem(string value, int count, bool isChecked)
        {
            Value = value;
            Count = count;
            _isChecked = isChecked;
        }
    }

    /// <summary>
    /// 하단 Results 탭: 날짜별 CSV 결과 리스트 (오늘 = 실시간 추가, 과거 날짜 = 읽기 전용)
    /// </summary>
    public class ResultHistoryViewModel : ViewModelBase
    {
        // 화면에 유지하는 최대 행 수 (집계는 하루 전체 기준)
        private const int MaxRows = 5000;
        private const string All = "전체";

        private readonly ResultCsvService _csv = ResultCsvService.Instance;
        private readonly Dispatcher _dispatcher = Application.Current.Dispatcher;

        // 센서 스레드에서 들어온 결과를 모아뒀다가 UI 스레드에서 한 번에 반영 (결과가 몰려도 UI 부하 최소화)
        private readonly ConcurrentQueue<Tuple<DateTime, string[], string[]>> _pending = new ConcurrentQueue<Tuple<DateTime, string[], string[]>>();
        private int _flushScheduled;
        private int _loadVersion;
        private bool _isLoading;
        private bool _followToday = true; // 오늘 화면을 보는 중 (자정이 지나면 새 날짜로 따라감)
        private string _newestLoadedTime;

        private int _resultIdx = -1, _sensorIdx = -1, _imageIdx = -1, _timeIdx = -1;

        public ResultHistoryViewModel()
        {
            TodayCommand = new RelayCommand(o => SelectedDate = DateTime.Today);
            OpenFolderCommand = new RelayCommand(o => OpenFolder(SelectedRow));
            OpenImageCommand = new RelayCommand(o => OpenImage(SelectedRow));
            CopyRowCommand = new RelayCommand(o => CopyRow(SelectedRow));
            OpenCsvFolderCommand = new RelayCommand(o => OpenCsvFolder());

            OpenColumnFilterCommand = new RelayCommand(o => { if (o is int index) OpenColumnFilter(index); });
            ApplyColumnFilterCommand = new RelayCommand(o => ApplyColumnFilter());
            ClearColumnFilterCommand = new RelayCommand(o => SetColumnFilter(_filterColumnIndex, null));
            CheckAllFilterValuesCommand = new RelayCommand(o => CheckAllFilterValues(Equals(o, "True")));
            ClearAllColumnFiltersCommand = new RelayCommand(o => ClearAllColumnFilters());

            _selectedDate = DateTime.Today;
            _csv.RowLogged += OnRowLogged;
            LoadDate(_selectedDate);
        }

        #region 바인딩 속성

        private ObservableCollection<ResultRow> _rows = new ObservableCollection<ResultRow>();
        public ObservableCollection<ResultRow> Rows
        {
            get => _rows;
            private set
            {
                _rows = value;
                RowsView = CollectionViewSource.GetDefaultView(_rows);
                ApplyFilter();
                OnPropertyChanged();
            }
        }

        private ICollectionView _rowsView;
        public ICollectionView RowsView
        {
            get => _rowsView;
            private set { _rowsView = value; OnPropertyChanged(); }
        }

        // CSV 헤더 = DataGrid 컬럼 (ResultGridColumns 첨부 속성이 동적으로 생성)
        private string[] _headers;
        public string[] Headers
        {
            get => _headers;
            private set
            {
                _headers = value;
                _timeIdx = ResultCsvService.IndexOf(value, ResultCsvService.TimeHeader);
                _resultIdx = ResultCsvService.IndexOf(value, ResultCsvService.ResultHeader);
                _sensorIdx = ResultCsvService.IndexOf(value, ResultCsvService.SensorHeader);
                _imageIdx = ResultCsvService.IndexOf(value, ResultCsvService.ImagePathHeader);
                OnPropertyChanged();

                // 컬럼 구성이 같으면 컬럼/헤더 필터 유지 (날짜만 바꾼 경우), 다르면 새로 만들고 필터 초기화
                bool same = value != null && _headerColumns != null && _headerColumns.Select(c => c.Name).SequenceEqual(value);
                if (same) return;

                _columnFilters.Clear();
                OnPropertyChanged(nameof(HasColumnFilters));
                HeaderColumns = value?.Select((name, i) => new ResultColumn(name, i)).ToArray();
            }
        }

        // DataGrid 컬럼 정의 (ResultGridColumns 첨부 속성이 동적으로 생성, 헤더의 ▼ 버튼으로 값 필터)
        private ResultColumn[] _headerColumns;
        public ResultColumn[] HeaderColumns
        {
            get => _headerColumns;
            private set { _headerColumns = value; OnPropertyChanged(); }
        }

        private DateTime _selectedDate;
        public DateTime? SelectedDate
        {
            get => _selectedDate;
            set
            {
                if (value == null || value.Value.Date == _selectedDate) return;
                _selectedDate = value.Value.Date;
                _followToday = _selectedDate == DateTime.Today;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPastDate));
                OnPropertyChanged(nameof(PastDateText));
                LoadDate(_selectedDate);
            }
        }

        public bool IsPastDate => _selectedDate != DateTime.Today;
        public string PastDateText => $"{_selectedDate:yyyy-MM-dd} 기록 (읽기 전용) - 실시간 결과는 [오늘]에서 확인";

        // 행을 선택하면 해당 센서 디스플레이에 그 결과 이미지를 띄움 (MainViewModel이 구독)
        public event Action<ResultRow> RowSelected;

        private ResultRow _selectedRow;
        public ResultRow SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (ReferenceEquals(_selectedRow, value)) return;
                _selectedRow = value;
                OnPropertyChanged();
                if (value != null) RowSelected?.Invoke(value);
            }
        }

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            private set { _statusMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasStatusMessage)); }
        }
        public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);

        private int _totalCount;
        public int TotalCount { get => _totalCount; private set { _totalCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(OkCount)); OnPropertyChanged(nameof(NgRateText)); } }

        private int _ngCount;
        public int NgCount { get => _ngCount; private set { _ngCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(OkCount)); OnPropertyChanged(nameof(NgRateText)); } }

        public int OkCount => _totalCount - _ngCount;
        public string NgRateText => _totalCount == 0 ? "0.0%" : $"{_ngCount * 100.0 / _totalCount:0.0}%";

        // 필터
        public ObservableCollection<string> SensorFilters { get; } = new ObservableCollection<string> { All };
        public string[] ResultFilters { get; } = { All, "OK", "NG" };

        private string _selectedSensor = All;
        public string SelectedSensor
        {
            get => _selectedSensor;
            set { _selectedSensor = value ?? All; OnPropertyChanged(); ApplyFilter(); }
        }

        private string _selectedResult = All;
        public string SelectedResult
        {
            get => _selectedResult;
            set { _selectedResult = value ?? All; OnPropertyChanged(); ApplyFilter(); }
        }

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set { _searchText = value; OnPropertyChanged(); ApplyFilter(); }
        }

        public ICommand TodayCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand OpenImageCommand { get; }
        public ICommand CopyRowCommand { get; }
        public ICommand OpenCsvFolderCommand { get; }

        // 센서별 최신 판정 (결과 패널 하단에 크게 표시)
        public ObservableCollection<LatestResult> LatestResults { get; } = new ObservableCollection<LatestResult>();

        #endregion

        #region 날짜별 CSV 로드

        private async void LoadDate(DateTime date)
        {
            int version = Interlocked.Increment(ref _loadVersion);
            _isLoading = true;
            StatusMessage = "불러오는 중...";

            ResultFileData data = null;
            string error = null;
            try
            {
                data = await Task.Run(() => _csv.ReadFile(date, MaxRows));
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            // 로드 중 날짜를 또 바꿨으면 이전 결과는 버림
            if (version != _loadVersion) return;

            if (error != null)
            {
                Logger.Error($"결과 CSV 읽기 실패 ({date:yyyy-MM-dd}): {error}");
                ApplyData(null);
                StatusMessage = $"{date:yyyy-MM-dd} 결과 파일을 읽을 수 없습니다.";
            }
            else
            {
                ApplyData(data);
                StatusMessage = data == null || data.TotalCount == 0 ? $"{date:yyyy-MM-dd} 검사 기록 없음" : null;
                if (data != null && data.BadRowCount > 0)
                    Logger.Warning($"결과 CSV({date:yyyy-MM-dd})에서 형식이 맞지 않는 {data.BadRowCount}행을 보정해서 표시했습니다.");
            }

            _isLoading = false;
            FlushPending(); // 로드 중에 쌓인 실시간 결과 반영
        }

        private void ApplyData(ResultFileData data)
        {
            // 컬럼 재생성 시 이전 행이 새 컬럼에 바인딩되지 않도록 먼저 비움
            if (_rows.Count > 0) Rows = new ObservableCollection<ResultRow>();
            Headers = data?.Headers;
            TotalCount = data?.TotalCount ?? 0;
            NgCount = data?.NgCount ?? 0;

            var rows = data?.Rows ?? new List<ResultRow>();
            _newestLoadedTime = rows.Count > 0 && _timeIdx >= 0 ? rows[0].Values[_timeIdx] : null;

            // 컬렉션을 통째로 교체 (행마다 Add 하면 수천 번 CollectionChanged가 발생)
            Rows = new ObservableCollection<ResultRow>(rows);

            SensorFilters.Clear();
            SensorFilters.Add(All);
            if (data != null)
            {
                foreach (var s in data.Sensors.OrderBy(s => s)) SensorFilters.Add(s);
            }
            if (!SensorFilters.Contains(_selectedSensor)) SelectedSensor = All;
        }

        #endregion

        #region 실시간 추가

        private void OnRowLogged(DateTime date, string[] headers, string[] values)
        {
            _pending.Enqueue(Tuple.Create(date, headers, values));

            if (Interlocked.Exchange(ref _flushScheduled, 1) == 0)
                _dispatcher.BeginInvoke(new Action(FlushPending), DispatcherPriority.Background);
        }

        private void UpdateLatest(string[] headers, string[] values)
        {
            string sensor = ValueOf(headers, values, ResultCsvService.SensorHeader) ?? "";
            string result = ValueOf(headers, values, ResultCsvService.ResultHeader);

            var latest = new LatestResult(
                sensor,
                string.IsNullOrEmpty(result) ? "-" : result.ToUpperInvariant(),
                !string.Equals(result, "OK", StringComparison.OrdinalIgnoreCase),
                ValueOf(headers, values, ResultCsvService.PointHeader),
                ValueOf(headers, values, ResultCsvService.TimeHeader));

            // 불변 객체를 교체하는 방식 (센서 수만큼만 유지, 바인딩 갱신 최소)
            for (int i = 0; i < LatestResults.Count; i++)
            {
                if (LatestResults[i].Sensor == sensor)
                {
                    LatestResults[i] = latest;
                    return;
                }
            }
            LatestResults.Add(latest);
        }

        // 센서 연결 해제 시 해당 센서 카드 제거
        public void RemoveLatest(string sensor)
        {
            var card = LatestResults.FirstOrDefault(r => r.Sensor == sensor);
            if (card != null) LatestResults.Remove(card);
        }

        private static string ValueOf(string[] headers, string[] values, string name)
        {
            int idx = ResultCsvService.IndexOf(headers, name);
            return idx >= 0 && idx < values.Length ? values[idx] : null;
        }

        private void FlushPending()
        {
            Interlocked.Exchange(ref _flushScheduled, 0);
            if (_isLoading) return; // 로드 완료 후 다시 호출됨

            Tuple<DateTime, string[], string[]> item;
            while (_pending.TryDequeue(out item))
            {
                UpdateLatest(item.Item2, item.Item3); // 보고 있는 날짜와 상관없이 최신 판정은 항상 갱신

                if (item.Item1 != _selectedDate)
                {
                    // 과거 날짜를 보는 중이면 화면에는 안 올림 (CSV에는 기록됨)
                    if (!_followToday || item.Item1 < _selectedDate) continue;
                    StartNewDay(item.Item1);
                }

                AddLiveRow(item.Item2, item.Item3);
            }
        }

        // 오늘 화면을 보던 중 자정이 넘어가면 새 날짜로 자동 전환
        private void StartNewDay(DateTime date)
        {
            _selectedDate = date;
            OnPropertyChanged(nameof(SelectedDate));
            OnPropertyChanged(nameof(IsPastDate));
            OnPropertyChanged(nameof(PastDateText));
            ApplyData(null);
        }

        private void AddLiveRow(string[] headers, string[] values)
        {
            if (_headers == null) Headers = headers;

            // 로드 직후, 이미 파일에서 읽어온 행이면 중복 추가하지 않음
            if (_newestLoadedTime != null && _timeIdx >= 0)
            {
                int t = Array.IndexOf(headers, ResultCsvService.TimeHeader);
                if (t >= 0 && string.CompareOrdinal(values[t], _newestLoadedTime) <= 0) return;
                _newestLoadedTime = null;
            }

            // 화면 컬럼 순서에 맞춰 값 정렬 (헤더가 같으면 그대로 사용)
            string[] mapped = values;
            if (!ReferenceEquals(headers, _headers) && !headers.SequenceEqual(_headers))
            {
                mapped = new string[_headers.Length];
                for (int i = 0; i < _headers.Length; i++)
                {
                    int src = Array.IndexOf(headers, _headers[i]);
                    mapped[i] = src >= 0 ? values[src] : "";
                }
            }

            var row = ResultCsvService.CreateRow(mapped, _resultIdx, _sensorIdx, _imageIdx);

            _rows.Insert(0, row);
            if (_rows.Count > MaxRows) _rows.RemoveAt(_rows.Count - 1);

            TotalCount++;
            if (row.IsNg) NgCount++;
            if (!string.IsNullOrEmpty(row.Sensor) && !SensorFilters.Contains(row.Sensor)) SensorFilters.Add(row.Sensor);
            if (StatusMessage != null) StatusMessage = null;
        }

        #endregion

        #region 필터

        private void ApplyFilter()
        {
            if (_rowsView == null) return;

            bool active = _selectedSensor != All || _selectedResult != All || !string.IsNullOrWhiteSpace(_searchText) || _columnFilters.Count > 0;
            // 필터가 없으면 null로 두어 행 추가 시 필터 평가 비용 제거
            _rowsView.Filter = active ? FilterRow : (Predicate<object>)null;
        }

        private bool FilterRow(object o)
        {
            var row = (ResultRow)o;

            foreach (var kv in _columnFilters)
            {
                if (!kv.Value.Contains(row.Values[kv.Key] ?? "")) return false;
            }

            if (_selectedSensor != All && row.Sensor != _selectedSensor) return false;
            if (_selectedResult == "OK" && row.IsNg) return false;
            if (_selectedResult == "NG" && !row.IsNg) return false;

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                string keyword = _searchText.Trim();
                foreach (var v in row.Values)
                {
                    if (v != null && v.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
                return false;
            }
            return true;
        }

        #endregion

        #region 헤더 필터 (엑셀 자동 필터 방식: 컬럼별 허용 값 목록)

        // 컬럼 인덱스 -> 표시할 값 집합
        private readonly Dictionary<int, HashSet<string>> _columnFilters = new Dictionary<int, HashSet<string>>();
        private int _filterColumnIndex = -1;

        public bool HasColumnFilters => _columnFilters.Count > 0;

        public ICommand OpenColumnFilterCommand { get; }
        public ICommand ApplyColumnFilterCommand { get; }
        public ICommand ClearColumnFilterCommand { get; }
        public ICommand CheckAllFilterValuesCommand { get; }
        public ICommand ClearAllColumnFiltersCommand { get; }

        private bool _isColumnFilterOpen;
        public bool IsColumnFilterOpen
        {
            get => _isColumnFilterOpen;
            set { _isColumnFilterOpen = value; OnPropertyChanged(); }
        }

        private string _filterColumnName;
        public string FilterColumnName
        {
            get => _filterColumnName;
            private set { _filterColumnName = value; OnPropertyChanged(); }
        }

        private List<FilterValueItem> _filterValues = new List<FilterValueItem>();
        private ICollectionView _filterValuesView;
        public ICollectionView FilterValuesView
        {
            get => _filterValuesView;
            private set { _filterValuesView = value; OnPropertyChanged(); }
        }

        // 팝업 안의 값 검색
        private string _filterSearchText;
        public string FilterSearchText
        {
            get => _filterSearchText;
            set
            {
                _filterSearchText = value;
                OnPropertyChanged();
                if (_filterValuesView == null) return;
                _filterValuesView.Filter = string.IsNullOrWhiteSpace(value)
                    ? (Predicate<object>)null
                    : o => ((FilterValueItem)o).Display.IndexOf(value.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        // 현재 화면에 올라온 행 기준으로 컬럼의 고유 값 목록(개수 포함)을 만들어 팝업 표시
        private void OpenColumnFilter(int index)
        {
            if (_headers == null || index < 0 || index >= _headers.Length) return;

            var counts = new Dictionary<string, int>();
            foreach (var row in _rows)
            {
                string v = row.Values[index] ?? "";
                int c;
                counts[v] = counts.TryGetValue(v, out c) ? c + 1 : 1;
            }

            HashSet<string> current;
            _columnFilters.TryGetValue(index, out current);

            // 숫자 컬럼이면 숫자 순, 아니면 문자열 순
            double tmp;
            bool numeric = counts.Keys.All(k => k.Length == 0 || double.TryParse(k, out tmp));
            IEnumerable<string> keys = numeric
                ? counts.Keys.OrderBy(k => k.Length == 0 ? double.MinValue : double.Parse(k))
                : counts.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

            _filterValues = keys.Select(k => new FilterValueItem(k, counts[k], current == null || current.Contains(k))).ToList();

            _filterColumnIndex = index;
            FilterColumnName = _headers[index];
            FilterValuesView = CollectionViewSource.GetDefaultView(_filterValues);
            FilterSearchText = null;
            IsColumnFilterOpen = true;
        }

        private void CheckAllFilterValues(bool isChecked)
        {
            if (_filterValuesView == null) return;
            foreach (FilterValueItem item in _filterValuesView) item.IsChecked = isChecked; // 검색으로 보이는 값만
        }

        private void ApplyColumnFilter()
        {
            var allowed = new HashSet<string>(_filterValues.Where(i => i.IsChecked).Select(i => i.Value));
            SetColumnFilter(_filterColumnIndex, allowed.Count == _filterValues.Count ? null : allowed);
        }

        // allowed == null 이면 해당 컬럼 필터 해제
        private void SetColumnFilter(int index, HashSet<string> allowed)
        {
            IsColumnFilterOpen = false;
            if (index < 0) return;

            if (allowed == null) _columnFilters.Remove(index);
            else _columnFilters[index] = allowed;

            var column = _headerColumns?.FirstOrDefault(c => c.Index == index);
            if (column != null) column.IsFiltered = allowed != null;

            OnPropertyChanged(nameof(HasColumnFilters));
            ApplyFilter();
        }

        private void ClearAllColumnFilters()
        {
            if (_columnFilters.Count == 0) return;

            _columnFilters.Clear();
            if (_headerColumns != null)
            {
                foreach (var c in _headerColumns) c.IsFiltered = false;
            }

            OnPropertyChanged(nameof(HasColumnFilters));
            ApplyFilter();
        }

        #endregion

        #region 우클릭 / 더블클릭 동작

        // 이미지 파일을 선택한 상태로 탐색기 열기 (파일이 없으면 폴더만, 폴더도 없으면 안내)
        private void OpenFolder(ResultRow row)
        {
            if (row == null) return;
            string path = row.ImagePath;

            if (string.IsNullOrEmpty(path))
            {
                Logger.Warning("이 결과에는 저장된 이미지 경로가 없습니다.");
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                    return;
                }

                string dir = Path.GetDirectoryName(path);
                if (Directory.Exists(dir)) Process.Start("explorer.exe", $"\"{dir}\"");
                else Logger.Warning($"경로가 존재하지 않습니다: {dir}");
            }
            catch (Exception ex)
            {
                Logger.Error($"폴더 열기 실패: {ex.Message}");
            }
        }

        private void OpenImage(ResultRow row)
        {
            if (row == null || string.IsNullOrEmpty(row.ImagePath)) return;

            try
            {
                if (File.Exists(row.ImagePath)) Process.Start(row.ImagePath);
                else Logger.Warning($"이미지 파일이 없습니다: {row.ImagePath}");
            }
            catch (Exception ex)
            {
                Logger.Error($"이미지 열기 실패: {ex.Message}");
            }
        }

        private void CopyRow(ResultRow row)
        {
            if (row == null) return;

            try
            {
                Clipboard.SetText(string.Join("\t", row.Values));
            }
            catch (Exception ex)
            {
                Logger.Error($"복사 실패: {ex.Message}");
            }
        }

        private void OpenCsvFolder()
        {
            try
            {
                string file = _csv.GetFilePath(_selectedDate);
                if (File.Exists(file))
                {
                    Process.Start("explorer.exe", $"/select,\"{file}\"");
                    return;
                }

                Directory.CreateDirectory(_csv.ResultFolder);
                Process.Start("explorer.exe", $"\"{_csv.ResultFolder}\"");
            }
            catch (Exception ex)
            {
                Logger.Error($"결과 폴더 열기 실패: {ex.Message}");
            }
        }

        #endregion
    }
}
