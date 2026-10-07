using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace VisionCore.Models
{
    /// <summary>
    /// 결과 리스트 1행 (CSV 1줄). UI는 Values[i]를 헤더 순서대로 컬럼에 바인딩
    /// </summary>
    public class ResultRow
    {
        public string[] Values { get; }
        public string Sensor { get; }
        public string ImagePath { get; }
        public bool IsNg { get; }

        public ResultRow(string[] values, string sensor, string imagePath, bool isNg)
        {
            Values = values;
            Sensor = sensor;
            ImagePath = imagePath;
            IsNg = isNg;
        }
    }

    /// <summary>
    /// 하루치 CSV를 읽은 결과 (화면에 올릴 최근 N행 + 하루 전체 집계)
    /// </summary>
    public class ResultFileData
    {
        public string[] Headers;
        public List<ResultRow> Rows;      // 최신 행이 앞
        public int TotalCount;
        public int NgCount;
        public int BadRowCount;
        public HashSet<string> Sensors;
    }

    /// <summary>
    /// 검사 결과를 날짜별 CSV(SaveRootPath\Results\yyyy-MM-dd.csv) 하나에 모든 센서 결과를 기록/조회
    /// - 쓰기는 전용 백그라운드 스레드 1개가 큐로 받아 배치 처리 (센서 여러 대 동시 기록 충돌 방지, 검사 흐름 비차단)
    /// - 엑셀 등이 파일을 잠그고 있으면 메모리에 보관했다가 재시도
    /// </summary>
    public sealed class ResultCsvService
    {
        public static ResultCsvService Instance { get; } = new ResultCsvService();

        public const string TimeHeader = "Time";
        public const string SensorHeader = "Sensor";
        public const string PointHeader = "Point";
        public const string ResultHeader = "Result";
        public const string ImagePathHeader = "ImagePath";

        private static readonly Encoding Utf8Bom = new UTF8Encoding(true); // 엑셀 한글 깨짐 방지
        private static readonly char[] EscapeChars = { ',', '"', '\r', '\n' };

        // (파일 날짜, 헤더, 값) - 결과가 기록될 때마다 발생 (실시간 리스트 갱신용)
        public event Action<DateTime, string[], string[]> RowLogged;

        private readonly BlockingCollection<PendingLine> _queue = new BlockingCollection<PendingLine>();
        private readonly Thread _writer;

        private class PendingLine
        {
            public string Path;
            public string[] Headers;
            public string[] Values;
        }

        private ResultCsvService()
        {
            _writer = new Thread(WriteLoop) { IsBackground = true, Name = "ResultCsvWriter" };
            _writer.Start();
        }

        public string ResultFolder => Path.Combine(ConfigModel.Instance.SaveRootPath, "Results");

        public string GetFilePath(DateTime date) => Path.Combine(ResultFolder, date.ToString("yyyy-MM-dd") + ".csv");

        #region 쓰기

        public void Append(InspectionResultItem item)
        {
            if (item == null) return;

            var headers = new List<string> { TimeHeader, SensorHeader, PointHeader, ResultHeader };
            var values = new List<string> { item.Timestamp.ToString("HH:mm:ss.fff"), item.SensorName, item.PointName, item.Result };

            foreach (var cv in item.CustomValues)
            {
                headers.Add(cv.Name);
                values.Add(cv.Value);
            }

            headers.Add(ImagePathHeader);
            values.Add(item.ImagePath ?? "");

            var line = new PendingLine { Path = GetFilePath(item.Timestamp), Headers = headers.ToArray(), Values = values.ToArray() };

            try
            {
                _queue.Add(line);
            }
            catch (InvalidOperationException)
            {
                return; // 종료 중
            }

            RowLogged?.Invoke(item.Timestamp.Date, line.Headers, line.Values);
        }

        /// <summary>
        /// 프로그램 종료 시 남은 큐를 파일에 마저 기록 (최대 2초 대기)
        /// </summary>
        public void Shutdown()
        {
            _queue.CompleteAdding();
            _writer.Join(2000);
        }

        private void WriteLoop()
        {
            var batch = new List<PendingLine>();
            bool lockWarned = false;

            foreach (var first in _queue.GetConsumingEnumerable())
            {
                batch.Add(first);
                PendingLine next;
                while (batch.Count < 500 && _queue.TryTake(out next)) batch.Add(next);

                while (batch.Count > 0)
                {
                    try
                    {
                        WriteBatch(batch);
                        if (lockWarned) Logger.Info("결과 CSV 기록이 재개되었습니다.");
                        lockWarned = false;
                    }
                    catch (IOException ex) when (IsFileLocked(ex))
                    {
                        // 엑셀로 열어둔 경우 등: 버리지 않고 1초 후 재시도
                        if (!lockWarned) Logger.Warning("결과 CSV 파일이 다른 프로그램에서 사용 중입니다. 닫히면 자동으로 기록합니다.");
                        lockWarned = true;
                        if (_queue.IsAddingCompleted) return;
                        Thread.Sleep(1000);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"결과 CSV 기록 실패 ({batch.Count}행 누락): {ex.Message}");
                        batch.Clear();
                    }
                }
            }
        }

        // 파일 단위로 기록하고, 성공한 파일의 행은 배치에서 제거 (재시도 시 중복 기록 방지)
        private static void WriteBatch(List<PendingLine> batch)
        {
            foreach (var path in batch.Select(p => p.Path).Distinct().ToList())
            {
                var lines = batch.Where(p => p.Path == path).ToList();

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                bool isNew = !File.Exists(path) || new FileInfo(path).Length == 0;

                using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (var sw = new StreamWriter(fs, Utf8Bom))
                {
                    if (isNew) sw.WriteLine(ToCsvLine(lines[0].Headers));
                    foreach (var line in lines) sw.WriteLine(ToCsvLine(line.Values));
                }

                batch.RemoveAll(p => p.Path == path);
            }
        }

        private static bool IsFileLocked(IOException ex)
        {
            int code = ex.HResult & 0xFFFF;
            return code == 32 || code == 33; // ERROR_SHARING_VIOLATION / ERROR_LOCK_VIOLATION
        }

        private static string ToCsvLine(string[] values)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append(',');
                string v = values[i];
                if (string.IsNullOrEmpty(v)) continue;

                if (v.IndexOfAny(EscapeChars) < 0) sb.Append(v);
                else sb.Append('"').Append(v.Replace("\"", "\"\"")).Append('"');
            }
            return sb.ToString();
        }

        #endregion

        #region 읽기

        /// <summary>
        /// 해당 날짜 CSV를 스트리밍으로 읽어 최근 maxRows행만 보관 (파일이 커도 메모리 일정). 파일이 없으면 null
        /// </summary>
        public ResultFileData ReadFile(DateTime date, int maxRows)
        {
            string path = GetFilePath(date);
            if (!File.Exists(path)) return null;

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs, Encoding.UTF8, true))
            {
                var sb = new StringBuilder();
                var fields = new List<string>();

                if (ReadRecord(reader, sb, fields) == null) return null;

                var data = new ResultFileData { Headers = fields.ToArray(), Sensors = new HashSet<string>() };
                int resultIdx = IndexOf(data.Headers, ResultHeader);
                int sensorIdx = IndexOf(data.Headers, SensorHeader);
                int imageIdx = IndexOf(data.Headers, ImagePathHeader);

                var recent = new Queue<ResultRow>(Math.Min(maxRows, 1024));

                while (ReadRecord(reader, sb, fields) != null)
                {
                    if (fields.Count == 1 && fields[0].Length == 0) continue; // 빈 줄

                    string[] values;
                    if (fields.Count == data.Headers.Length)
                    {
                        values = fields.ToArray();
                    }
                    else
                    {
                        // 컬럼 수가 안 맞는 깨진 행: 모자라면 빈칸, 넘치면 잘라서 사용
                        values = new string[data.Headers.Length];
                        for (int i = 0; i < values.Length; i++) values[i] = i < fields.Count ? fields[i] : "";
                        data.BadRowCount++;
                    }

                    var row = CreateRow(values, resultIdx, sensorIdx, imageIdx);
                    data.TotalCount++;
                    if (row.IsNg) data.NgCount++;
                    if (!string.IsNullOrEmpty(row.Sensor)) data.Sensors.Add(row.Sensor);

                    recent.Enqueue(row);
                    if (recent.Count > maxRows) recent.Dequeue();
                }

                data.Rows = recent.Reverse().ToList();
                return data;
            }
        }

        public static ResultRow CreateRow(string[] values, int resultIdx, int sensorIdx, int imageIdx)
        {
            string result = resultIdx >= 0 ? values[resultIdx] : null;
            bool isNg = !string.IsNullOrEmpty(result) && !string.Equals(result, "OK", StringComparison.OrdinalIgnoreCase);

            return new ResultRow(values,
                sensorIdx >= 0 ? values[sensorIdx] : null,
                imageIdx >= 0 ? values[imageIdx] : null,
                isNg);
        }

        public static int IndexOf(string[] headers, string name)
        {
            if (headers == null) return -1;
            for (int i = 0; i < headers.Length; i++)
            {
                if (string.Equals(headers[i], name, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        // CSV 1레코드 파싱 (따옴표 안의 쉼표/줄바꿈 지원). fields/sb는 재사용해서 할당 최소화
        private static List<string> ReadRecord(TextReader reader, StringBuilder sb, List<string> fields)
        {
            string line = reader.ReadLine();
            if (line == null) return null;

            fields.Clear();
            sb.Clear();
            bool inQuotes = false;

            while (true)
            {
                for (int i = 0; i < line.Length; i++)
                {
                    char c = line[i];
                    if (inQuotes)
                    {
                        if (c != '"') sb.Append(c);
                        else if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else if (c == '"') inQuotes = true;
                    else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(c);
                }

                if (!inQuotes) break;

                line = reader.ReadLine();
                if (line == null) break; // 따옴표가 안 닫힌 깨진 행: 있는 데까지 사용
                sb.Append('\n');
            }

            fields.Add(sb.ToString());
            return fields;
        }

        #endregion
    }
}
