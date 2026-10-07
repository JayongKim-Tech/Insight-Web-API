using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using VisionCore.Models;

namespace VisionCore.ViewModels
{
    /// <summary>
    /// 이미지 저장 드라이브 사용량 표시 + 자동 삭제
    /// - 1분마다 사용률 확인 (드라이브 여유 공간 조회 1회라 부담 없음)
    /// - 설정한 % 이상이면 가장 오래된 날짜 폴더부터 하나씩 삭제, (설정값 - 10%)까지 내려가면 멈춤
    /// - 오늘 폴더, 결과 CSV(Results 폴더)는 삭제하지 않음
    /// </summary>
    public class DiskStatusViewModel : ViewModelBase
    {
        // 삭제를 시작하면 설정값보다 이만큼 아래까지 비움 (매 검사마다 삭제가 반복되지 않도록)
        private const double CleanupMarginPercent = 10;

        private readonly DispatcherTimer _timer;
        private int _isCleaning;
        private bool _noMoreWarned;

        public DiskStatusViewModel()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _timer.Tick += (s, e) => Refresh();
            _timer.Start();

            // 설정(저장 경로/기준 %)을 바꾸면 바로 다시 계산
            ConfigModel.Instance.ConfigChanged += (s, e) => _timer.Dispatcher.BeginInvoke(new Action(Refresh));
            Refresh();
        }

        private string _driveName;
        public string DriveName { get => _driveName; private set { _driveName = value; OnPropertyChanged(); } }

        private double _usedPercent;
        public double UsedPercent { get => _usedPercent; private set { _usedPercent = value; OnPropertyChanged(); } }

        private string _usageText;
        public string UsageText { get => _usageText; private set { _usageText = value; OnPropertyChanged(); } }

        // 0 = 정상, 1 = 기준 근접(5% 이내), 2 = 기준 초과 (막대 색상용)
        private int _level;
        public int Level { get => _level; private set { _level = value; OnPropertyChanged(); } }

        private string _toolTipText;
        public string ToolTipText { get => _toolTipText; private set { _toolTipText = value; OnPropertyChanged(); } }

        private static int LimitPercent => ConfigModel.Instance.DiskLimitPercent;

        public void Refresh()
        {
            DriveInfo drive = GetDrive();
            if (drive == null || !drive.IsReady)
            {
                UsageText = "드라이브 없음";
                return;
            }

            double total = drive.TotalSize;
            double used = total - drive.AvailableFreeSpace;
            double percent = used * 100.0 / total;

            DriveName = drive.Name.TrimEnd('\\');
            UsedPercent = percent;
            UsageText = $"{percent:0}%  {used / GB:0} / {total / GB:0} GB";

            int limit = LimitPercent;
            Level = limit <= 0 ? (percent >= 90 ? 1 : 0)
                  : percent >= limit ? 2
                  : percent >= limit - 5 ? 1 : 0;

            ToolTipText = limit <= 0
                ? $"이미지 저장 드라이브 {DriveName}  (자동 삭제 사용 안 함)"
                : $"이미지 저장 드라이브 {DriveName}  ({limit}% 이상이면 오래된 날짜 이미지부터 자동 삭제)";

            if (limit > 0 && percent >= limit)
            {
                if (Interlocked.Exchange(ref _isCleaning, 1) == 0)
                    Task.Run(() => Cleanup(limit - CleanupMarginPercent));
            }
            else
            {
                _noMoreWarned = false;
            }
        }

        private const double GB = 1024.0 * 1024 * 1024;

        private static DriveInfo GetDrive()
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(ConfigModel.Instance.SaveRootPath));
                return string.IsNullOrEmpty(root) ? null : new DriveInfo(root);
            }
            catch
            {
                return null;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 자동 삭제 안전 규칙 (하나라도 어긋나면 지우지 않고 건너뜀)
        //  1. 저장 경로가 드라이브 루트(C:\ 등)·Windows·Program Files·사용자 기본 폴더 자체면 자동 삭제 전체 중단
        //  2. 저장경로\yyyy\MM\dd 이름 규칙에 맞는 폴더만 들어가 봄 (다른 이름의 폴더는 열어보지도 않음)
        //  3. 날짜 폴더 안이 이 프로그램이 만든 구조(센서\OK|NG\*.bmp|*.jpg)와 정확히 같을 때만 삭제
        //     -> 다른 파일/폴더가 하나라도 섞여 있으면 그 날짜 폴더는 통째로 건너뜀
        //  4. 폴더 통째 삭제(재귀 삭제)를 쓰지 않고, 검증된 이미지 파일만 하나씩 지운 뒤 빈 폴더만 정리
        //  5. 바로가기 폴더(정션/심볼릭 링크)는 건너뜀 (링크 너머 다른 경로로 넘어가지 않도록)
        //  6. 삭제 직전 모든 경로가 저장 경로 안쪽인지 다시 확인
        // ─────────────────────────────────────────────────────────────────────

        private static readonly string[] ImageExtensions = { ".bmp", ".jpg" };
        private static readonly string[] ResultFolders = { "OK", "NG" };

        // 백그라운드: 가장 오래된 날짜 폴더부터 하나씩 삭제하며 목표 사용률까지 내려가면 멈춤
        private void Cleanup(double targetPercent)
        {
            try
            {
                string root = NormalizeRoot(ConfigModel.Instance.SaveRootPath);
                if (root == null) return;

                string reason = GetUnsafeRootReason(root);
                if (reason != null)
                {
                    if (!_noMoreWarned)
                    {
                        _noMoreWarned = true;
                        Logger.Warning($"디스크 정리 중단: 저장 경로({root})가 {reason}라서 자동 삭제를 하지 않습니다. 전용 폴더로 저장 경로를 바꿔주세요.");
                    }
                    return;
                }

                var drive = GetDrive();
                if (drive == null) return;

                foreach (var day in FindDayFolders(root))
                {
                    if (UsedPercentOf(drive) <= targetPercent) break;

                    var files = CollectDeletableFiles(root, day.Path);
                    if (files == null)
                    {
                        Logger.Warning($"디스크 정리: {day.Date:yyyy-MM-dd} 폴더에 이 프로그램이 만들지 않은 파일/폴더가 있어 건너뜁니다. ({day.Path})");
                        continue;
                    }

                    long freed = 0;
                    int failed = 0;
                    foreach (var file in files)
                    {
                        try
                        {
                            long len = file.Length;
                            file.Delete();
                            freed += len;
                        }
                        catch
                        {
                            failed++; // 사용 중인 파일 등은 남겨둠
                        }
                    }

                    // 비어 있는 폴더만 아래에서 위로 정리 (OK/NG -> 센서 -> 일 -> 월 -> 연). 내용이 남아 있으면 그대로 둠
                    foreach (var sensorDir in Directory.GetDirectories(day.Path))
                    {
                        foreach (var resultDir in Directory.GetDirectories(sensorDir)) DeleteIfEmpty(root, resultDir);
                        DeleteIfEmpty(root, sensorDir);
                    }
                    DeleteIfEmpty(root, day.Path);
                    DeleteIfEmpty(root, Path.GetDirectoryName(day.Path));
                    DeleteIfEmpty(root, Path.GetDirectoryName(Path.GetDirectoryName(day.Path)));

                    Logger.Info($"디스크 정리: {day.Date:yyyy-MM-dd} 이미지 {files.Count - failed}개 삭제 ({freed / GB:0.0} GB 확보)"
                                + (failed > 0 ? $", 사용 중인 파일 {failed}개는 남김" : ""));

                    Thread.Sleep(200); // 검사 중 디스크 저장에 영향이 적도록 천천히
                }

                if (UsedPercentOf(drive) > targetPercent && !_noMoreWarned)
                {
                    _noMoreWarned = true;
                    Logger.Warning("디스크 정리: 더 지울 이전 날짜 이미지가 없습니다. 다른 파일이 디스크를 차지하고 있는지 확인하세요.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"디스크 정리 실패: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _isCleaning, 0);
            }
        }

        private static double UsedPercentOf(DriveInfo drive)
        {
            var d = new DriveInfo(drive.Name); // 최신 값으로 다시 조회
            return (d.TotalSize - d.AvailableFreeSpace) * 100.0 / d.TotalSize;
        }

        private static string NormalizeRoot(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return null;
                string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
                return Directory.Exists(full) ? full : null;
            }
            catch
            {
                return null;
            }
        }

        // 저장 경로가 자동 삭제 대상으로 쓰기에 위험한 위치면 이유를 반환 (안전하면 null)
        private static string GetUnsafeRootReason(string root)
        {
            string driveRoot = Path.GetPathRoot(root)?.TrimEnd(Path.DirectorySeparatorChar);
            if (string.Equals(root, driveRoot, StringComparison.OrdinalIgnoreCase)) return "드라이브 최상위";

            if (IsReparsePoint(root)) return "바로가기(링크) 폴더";

            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(windows) && IsSameOrUnder(root, windows)) return "Windows 시스템 폴더";

            string[] forbiddenExact =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), // C:\Users
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppDomain.CurrentDomain.BaseDirectory, // 프로그램 실행 폴더 자체
            };
            foreach (var f in forbiddenExact)
            {
                if (!string.IsNullOrEmpty(f) && string.Equals(root, f.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    return "시스템/사용자 기본 폴더";
            }

            return null;
        }

        // 날짜 폴더 안이 "센서\OK|NG\이미지파일" 구조로만 되어 있으면 지울 파일 목록 반환, 하나라도 다르면 null (= 건너뜀)
        private static System.Collections.Generic.List<FileInfo> CollectDeletableFiles(string root, string dayPath)
        {
            var result = new System.Collections.Generic.List<FileInfo>();
            var day = new DirectoryInfo(dayPath);

            if (IsReparsePoint(dayPath) || day.EnumerateFiles().Any()) return null; // 날짜 폴더 바로 아래엔 파일이 없어야 정상

            foreach (var sensorDir in day.EnumerateDirectories())
            {
                if ((sensorDir.Attributes & FileAttributes.ReparsePoint) != 0) return null;
                if (sensorDir.EnumerateFiles().Any()) return null; // 센서 폴더 바로 아래엔 파일이 없어야 정상

                foreach (var resultDir in sensorDir.EnumerateDirectories())
                {
                    if ((resultDir.Attributes & FileAttributes.ReparsePoint) != 0) return null;
                    if (!ResultFolders.Contains(resultDir.Name, StringComparer.OrdinalIgnoreCase)) return null;
                    if (resultDir.EnumerateDirectories().Any()) return null; // OK/NG 아래엔 하위 폴더가 없어야 정상

                    foreach (var file in resultDir.EnumerateFiles())
                    {
                        if (!ImageExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase)) return null;
                        if ((file.Attributes & FileAttributes.ReparsePoint) != 0) return null;
                        if (!IsUnder(file.FullName, root)) return null;
                        result.Add(file);
                    }
                }
            }

            return result;
        }

        // 저장경로\yyyy\MM\dd 이름 규칙의 폴더만 찾음 (오늘 제외, 오래된 순). 규칙에 안 맞는 이름의 폴더는 열어보지 않음
        private static System.Collections.Generic.IEnumerable<DayFolder> FindDayFolders(string root)
        {
            var list = new System.Collections.Generic.List<DayFolder>();

            foreach (var y in Directory.GetDirectories(root).Where(p => IsDigits(Path.GetFileName(p), 4) && !IsReparsePoint(p)))
            foreach (var m in Directory.GetDirectories(y).Where(p => IsDigits(Path.GetFileName(p), 2) && !IsReparsePoint(p)))
            foreach (var d in Directory.GetDirectories(m).Where(p => IsDigits(Path.GetFileName(p), 2) && !IsReparsePoint(p)))
            {
                string key = $"{Path.GetFileName(y)}-{Path.GetFileName(m)}-{Path.GetFileName(d)}";
                DateTime date;
                if (DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                    && date < DateTime.Today && IsUnder(d, root))
                {
                    list.Add(new DayFolder { Date = date, Path = d });
                }
            }

            return list.OrderBy(f => f.Date);
        }

        // 저장 경로 안쪽의 빈 폴더만 삭제 (재귀 삭제 아님 - 내용이 있으면 Windows가 거부)
        private static void DeleteIfEmpty(string root, string dir)
        {
            try
            {
                if (!IsUnder(dir, root) || !Directory.Exists(dir) || IsReparsePoint(dir)) return;
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir, false);
            }
            catch { /* 비어있지 않거나 사용 중이면 그대로 둠 */ }
        }

        // path가 root "안쪽"인지 (root 자체는 false)
        private static bool IsUnder(string path, string root)
        {
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSameOrUnder(string path, string parent)
        {
            string p = parent.TrimEnd(Path.DirectorySeparatorChar);
            return string.Equals(path, p, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsReparsePoint(string path)
        {
            try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
            catch { return true; } // 확인 불가 = 위험으로 간주
        }

        private static bool IsDigits(string s, int length) => s != null && s.Length == length && s.All(char.IsDigit);

        private class DayFolder
        {
            public DateTime Date;
            public string Path;
        }
    }
}
