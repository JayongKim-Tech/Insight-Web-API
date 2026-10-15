using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cognex.InSight.Web;
using Newtonsoft.Json.Linq;

namespace VisionCore.Models.JobEditor
{
    /// <summary>
    /// 공통 템플릿 셀 배치.
    /// 포인트 표: C22:G41 (Pos, Name, Pass, Judge, Result) / 포인트 블록: 18행 간격, 툴은 블록 +0~+17행.
    /// 블록 시작 행은 잡마다 다름 (002 = 44, 003 = 55) → 포인트 표 D22 식($B$55)에서 읽음
    /// </summary>
    public static class JobTemplateLayout
    {
        public const int PointCount = 20;
        public const int TableFirstRow = 22;
        public const int DefaultBlockFirstRow = 44;
        public static int BlockFirstRow { get; set; } = DefaultBlockFirstRow;
        public const int BlockStride = 18;
        // 블록 첫 행부터 검색 (TrainPatMaxRedLine 이 블록 +1 행(A45)에 있는 잡이 있음. 함수 셀만 툴로 잡히므로 이름 행이 섞여도 무방)
        public const int ToolFirstOffset = 0;
        public const int ToolLastOffset = 17;
        public const string Columns = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        public static int BlockRow(int pointIndex)
        {
            return BlockFirstRow + BlockStride * pointIndex;
        }
    }

    /// <summary>
    /// 포인트 표의 1행 (P01 ~ P20)
    /// </summary>
    public class PointInfo
    {
        public int Index { get; set; }
        public string Code { get; set; }        // P01
        public string Name { get; set; }        // 블록 B열 이름
        public string Position { get; set; }    // PLC Position 번호
        public bool Pass { get; set; }          // Bypass
        public string Result { get; set; }      // OK / NG
        public int BlockRow { get; set; }
    }

    /// <summary>
    /// 센서에 로드된 잡을 Web API로 읽어 포인트/툴 셀을 찾음
    /// </summary>
    public static class JobToolScanner
    {
        /// <summary>
        /// 최신 결과의 location -> data 문자열 (구독 범위 A0:Z599 안의 값이 있는 셀)
        /// </summary>
        public static Dictionary<string, string> ReadCellMap(CvsInSight sensor)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            JArray cells = sensor.Results != null ? sensor.Results["cells"] as JArray : null;
            if (cells == null) return map;

            foreach (JToken c in cells)
            {
                string loc = (string)c["location"];
                if (loc == null || map.ContainsKey(loc)) continue;
                JToken data = c["data"];
                map[loc] = data == null || data.Type == JTokenType.Null ? "" : data.ToString();
            }
            return map;
        }

        /// <summary>포인트 표 첫 행의 Name 셀(D22 = $B$55) → 1번 블록 시작 행. 못 읽으면 기존 값 유지</summary>
        public static async Task DetectBlockFirstRowAsync(CvsInSight sensor)
        {
            try
            {
                string expr = await sensor.GetCellExpression("D" + JobTemplateLayout.TableFirstRow);
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match((expr ?? "").Trim(), @"^=?\$?B\$?(\d{1,3})$");
                if (m.Success) JobTemplateLayout.BlockFirstRow = int.Parse(m.Groups[1].Value);
            }
            catch { /* 기존 값 유지 */ }
        }

        /// <param name="count">표시할 포인트 수 (Config의 Point 개수, 템플릿 최대 20)</param>
        public static async Task<List<PointInfo>> LoadPointsAsync(CvsInSight sensor, int count)
        {
            await sensor.GetLatestResult();
            Dictionary<string, string> map = ReadCellMap(sensor);
            await DetectBlockFirstRowAsync(sensor);

            var points = new List<PointInfo>();
            int n = Math.Max(1, Math.Min(count, JobTemplateLayout.PointCount));

            // 포인트마다 Name 셀(D22 = $B$55 등)이 가리키는 행 = 그 포인트 블록의 이름 행 (블록 간격이 잡마다 달라도 맞게)
            var nameCells = Enumerable.Range(0, n).Select(i => "D" + (JobTemplateLayout.TableFirstRow + i)).ToList();
            var nameExpr = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await ReadExpressionsAsync(sensor, nameCells, nameExpr);

            for (int i = 0; i < n; i++)
            {
                int row = JobTemplateLayout.TableFirstRow + i;
                int blockRow = JobTemplateLayout.BlockRow(i);
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match((nameExpr["D" + row] ?? "").Trim(), @"^=?\$?B\$?(\d{1,3})$");
                if (m.Success) blockRow = int.Parse(m.Groups[1].Value);
                points.Add(new PointInfo
                {
                    Index = i,
                    Code = "P" + (i + 1).ToString("00"),
                    Name = Get(map, "B" + blockRow),
                    Position = Get(map, "C" + row),
                    Pass = Get(map, "E" + row) == "1" || string.Equals(Get(map, "E" + row), "True", StringComparison.OrdinalIgnoreCase),
                    Result = Get(map, "G" + row),
                    BlockRow = blockRow
                });
            }
            return points;
        }

        private static readonly System.Text.RegularExpressions.Regex RangeRef =
            new System.Text.RegularExpressions.Regex(@"\$?([A-Za-z])\$?(\d{1,3})\s*:\s*\$?([A-Za-z])\$?(\d{1,3})");
        private static readonly System.Text.RegularExpressions.Regex CellRef =
            new System.Text.RegularExpressions.Regex(@"(?<![A-Za-z0-9_.])\$?([A-Za-z])\$?(\d{1,3})(?![0-9(A-Za-z_])");

        private const int TraceMaxCells = 400;     // 참조 추적으로 읽을 최대 셀 수
        private const int MaxBlockSpan = 40;       // 툴 영역으로 볼 최대 행 수

        /// <summary>
        /// 포인트의 툴 찾기 (행 위치를 가정하지 않음 - 잡마다 블록이 조금씩 밀려도 맞게):
        ///  1) 포인트 표의 판정(F)/결과(G) 셀에서 참조를 거꾸로 따라가며 만나는 함수 셀 = 결과에 영향을 주는 툴 (AffectsResult)
        ///  2) 그 툴들이 있는 행 범위(+ 블록 이름 행) 안의 나머지 함수 셀 = 보조 툴 (PlotString 등, 결과에는 영향 없음)
        ///  3) 참조를 하나도 못 찾으면 예전 방식 (블록 시작 행 +0 ~ +17)
        /// </summary>
        public static async Task<List<ToolCell>> LoadToolsAsync(CvsInSight sensor, PointInfo point, ToolCatalog catalog)
        {
            int tableRow = JobTemplateLayout.TableFirstRow + point.Index;
            var expressions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 1) 참조 추적 (BFS, 단계별로 여러 셀을 동시에 조회)
            var traced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var frontier = new List<string> { "F" + tableRow, "G" + tableRow };
            while (frontier.Count > 0 && expressions.Count < TraceMaxCells)
            {
                await ReadExpressionsAsync(sensor, frontier, expressions);
                var next = new List<string>();
                foreach (string loc in frontier)
                {
                    traced.Add(loc);
                    string expr;
                    if (!expressions.TryGetValue(loc, out expr)) continue;
                    foreach (string r in References(expr))
                    {
                        if (traced.Contains(r) || next.Contains(r) || !ShouldTrace(r, tableRow)) continue;
                        next.Add(r);
                    }
                }
                frontier = next.Take(TraceMaxCells - expressions.Count).ToList();
            }

            var tools = new List<ToolCell>();
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string loc in traced)
            {
                string expr;
                if (!expressions.TryGetValue(loc, out expr)) continue;
                ToolCell tool = ToolCell.TryCreate(loc, expr, catalog, true);
                if (tool == null) continue;
                tool.AffectsResult = true;
                tools.Add(tool);
                found.Add(loc);
            }

            // 2) 행 범위: 추적한 툴 + 블록 이름 행. 못 찾았으면 3) 예전 고정 범위
            var rows = tools.Select(t => RowOf(t.Location)).Where(r => r > JobTemplateLayout.TableFirstRow + JobTemplateLayout.PointCount).ToList();
            int first, last;
            if (rows.Count > 0)
            {
                first = Math.Min(rows.Min(), point.BlockRow);
                last = rows.Max();
                if (last - first + 1 > MaxBlockSpan) first = Math.Max(rows.Min(), last - MaxBlockSpan + 1);   // 블록 이름 행이 너무 멀면 툴 범위만
            }
            else
            {
                first = point.BlockRow + JobTemplateLayout.ToolFirstOffset;
                last = point.BlockRow + JobTemplateLayout.ToolLastOffset;
            }

            var area = new List<string>();
            for (int r = first; r <= last; r++)
                foreach (char col in JobTemplateLayout.Columns)
                {
                    string loc = col.ToString() + r;
                    if (!expressions.ContainsKey(loc)) area.Add(loc);
                }
            await ReadExpressionsAsync(sensor, area, expressions);

            foreach (string loc in area)
            {
                string expr;
                if (found.Contains(loc) || !expressions.TryGetValue(loc, out expr)) continue;
                ToolCell tool = ToolCell.TryCreate(loc, expr, catalog, true);
                if (tool != null) tools.Add(tool);
            }

            return tools.OrderBy(t => RowOf(t.Location)).ThenBy(t => t.Location[0]).ToList();
        }

        /// <summary>A0(촬상)와 다른 포인트의 표 행은 따라가지 않음</summary>
        private static bool ShouldTrace(string loc, int ownTableRow)
        {
            int row = RowOf(loc);
            if (row < 0 || string.Equals(loc, "A0", StringComparison.OrdinalIgnoreCase)) return false;
            bool inTable = row >= JobTemplateLayout.TableFirstRow && row < JobTemplateLayout.TableFirstRow + JobTemplateLayout.PointCount;
            return !inTable || row == ownTableRow;
        }

        private static int RowOf(string loc)
        {
            int row;
            return loc != null && loc.Length > 1 && int.TryParse(loc.Substring(1), out row) ? row : -1;
        }

        /// <summary>식 안의 셀 참조 (문자열 리터럴 제외, A5:B9 범위는 펼침)</summary>
        public static IEnumerable<string> References(string expression)
        {
            if (string.IsNullOrEmpty(expression)) yield break;
            string code = System.Text.RegularExpressions.Regex.Replace(expression, "\"[^\"]*\"", "\"\"");

            foreach (System.Text.RegularExpressions.Match m in RangeRef.Matches(code))
            {
                char c1 = char.ToUpperInvariant(m.Groups[1].Value[0]), c2 = char.ToUpperInvariant(m.Groups[3].Value[0]);
                int r1 = int.Parse(m.Groups[2].Value), r2 = int.Parse(m.Groups[4].Value);
                int count = 0;
                for (char c = (char)Math.Min(c1, c2); c <= Math.Max(c1, c2); c++)
                    for (int r = Math.Min(r1, r2); r <= Math.Max(r1, r2) && count < 200; r++, count++)
                        yield return c.ToString() + r;
            }
            code = RangeRef.Replace(code, " ");

            foreach (System.Text.RegularExpressions.Match m in CellRef.Matches(code))
                yield return char.ToUpperInvariant(m.Groups[1].Value[0]) + m.Groups[2].Value;
        }

        /// <summary>요청은 id로 응답이 매칭되므로 여러 개를 동시에 보내 시간 단축</summary>
        internal static async Task ReadExpressionsAsync(CvsInSight sensor, List<string> locations, Dictionary<string, string> into)
        {
            var todo = locations.Where(l => !into.ContainsKey(l)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var values = new string[todo.Count];
            using (var gate = new SemaphoreSlim(16))
            {
                await Task.WhenAll(todo.Select(async (loc, i) =>
                {
                    await gate.WaitAsync().ConfigureAwait(false);
                    try { values[i] = await sensor.GetCellExpression(loc).ConfigureAwait(false); }
                    catch { values[i] = null; }
                    finally { gate.Release(); }
                }));
            }
            for (int i = 0; i < todo.Count; i++)
                into[todo[i]] = values[i] ?? "";
        }

        private static string Get(Dictionary<string, string> map, string loc)
        {
            string v;
            return map.TryGetValue(loc, out v) ? v : "";
        }
    }
}
