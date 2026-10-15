using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VisionCore.Models.JobEditor
{
    /// <summary>
    /// 공통 템플릿의 얼라인 / 데이터 맵 셀 위치를 라벨 셀로 찾음 (행/열 고정 없음 - 잡마다 조금 밀려도 맞게).
    /// 기준 라벨:
    ///  - 포인트 표: 'Pos No' / 'Name' / 'IsNow' (헤더 행 다음 행부터 포인트 1, 2, ...)
    ///  - 포인트별 원점: 'OrgX' / 'OrgY' / 'OrgT' (헤더 행 = 포인트 표 헤더 행, 등록 버튼 = OrgX 왼쪽 열)
    ///  - 캘리브레이션: 'PosX' / 'PosY' 헤더 행. 그 왼쪽 3열 = 캡처 X/Y/θ, 4번째 왼쪽 = 캡처 버튼.
    ///    아래 행들이 단계 (원점, X1, X2, Y1, Y2), PosX 열에 'PosT' 가 있는 행 = θ 단계 (이동량은 PosY 열)
    ///  - 계수: 'Coef' 아래 행들 (라벨 열 + 오른쪽 값 열, 순서 = a, c, b, d, det, kθ)
    ///  - 보정값: 'Origin' 바로 아래가 'Cur' 인 셀부터 라벨 5행 (Origin, Cur, dPix, Robot, Result), 값 = 오른쪽 3열
    ///  - 데이터 맵: 'DATA MAP' 헤더 행의 'Name' / 'Source' / 'Scale' / 'Out' / 'Valid', 아래 행들이 슬롯
    ///  - 출력 버퍼: 값이 FORMAT_OUT_DATA 인 셀 (FormatOutputBuffer)
    ///  - 포인트 좌표: 각 포인트 블록 이름 행의 'X' / 'Y' / 'Theta' 라벨 → 바로 아래 행 값
    /// </summary>
    public class AlignLayout
    {
        public const string Cols = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        // 포인트 표
        public int TableFirstRow = -1;
        public int PosCol = -1, NameCol = -1, IsNowCol = -1;

        // 포인트별 원점
        public int OriginButtonCol = -1, OrgXCol = -1;
        public bool HasOriginTable => OrgXCol >= 0 && TableFirstRow >= 0;

        // 캘리브레이션
        public List<CalStepDef> CalSteps = new List<CalStepDef>();
        public bool HasCalibration => CalSteps.Count > 0;
        public List<string[]> Coefs = new List<string[]>();          // { 라벨 셀, 값 셀 }
        public List<string[]> LiveRows = new List<string[]>();       // { 라벨 셀, X, Y, θ }

        // 데이터 맵
        public int DataNoCol = -1, DataNameCol = -1, DataSourceCol = -1, DataScaleCol = -1, DataOutCol = -1, DataValidCol = -1;
        public List<int> DataRows = new List<int>();
        public bool HasDataMap => DataRows.Count > 0 && DataSourceCol >= 0 && DataOutCol >= 0;
        public string OutputBufferCell;

        public static string Loc(int col, int row) => Cols[col].ToString() + row;

        public static bool TryParse(string loc, out int col, out int row)
        {
            col = row = -1;
            Match m = Regex.Match(loc ?? "", @"^([A-Za-z])(\d{1,3})$");
            if (!m.Success) return false;
            col = char.ToUpperInvariant(m.Groups[1].Value[0]) - 'A';
            row = int.Parse(m.Groups[2].Value);
            return true;
        }

        public static AlignLayout Detect(Dictionary<string, string> map)
        {
            var labels = new Dictionary<string, List<int[]>>(StringComparer.OrdinalIgnoreCase);   // 텍스트 → (col,row) 목록 (행 순)
            foreach (KeyValuePair<string, string> kv in map)
            {
                int c, r;
                string t = (kv.Value ?? "").Trim();
                if (t.Length == 0 || t.Length > 40 || !TryParse(kv.Key, out c, out r)) continue;
                List<int[]> list;
                if (!labels.TryGetValue(t, out list)) labels[t] = list = new List<int[]>();
                list.Add(new[] { c, r });
            }
            foreach (List<int[]> l in labels.Values) l.Sort((a, b) => a[1] != b[1] ? a[1].CompareTo(b[1]) : a[0].CompareTo(b[0]));

            Func<string, List<int[]>> find = t => { List<int[]> l; return labels.TryGetValue(t, out l) ? l : new List<int[]>(); };
            Func<int, int, string> text = (c, r) => { string v; return c >= 0 && c < 26 && map.TryGetValue(Loc(c, r), out v) ? (v ?? "").Trim() : ""; };
            Func<int, string, int> colInRow = (r, t) => { int[] p = find(t).FirstOrDefault(x => x[1] == r); return p != null ? p[0] : -1; };
            Func<int, int, bool> has = (c, r) => c >= 0 && c < 26 && map.ContainsKey(Loc(c, r));
            // 라벨 = 숫자가 아닌 글자 (빈 셀이 0 으로 오는 경우 제외)
            Func<int, int, bool> isLabel = (c, r) => { string t = text(c, r); double d; return t.Length > 0 && !double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d); };

            var L = new AlignLayout();

            // 포인트 표
            int[] pos = find("Pos No").FirstOrDefault();
            if (pos != null)
            {
                L.TableFirstRow = pos[1] + 1;
                L.PosCol = pos[0];
                L.NameCol = colInRow(pos[1], "Name");
                L.IsNowCol = colInRow(pos[1], "IsNow");

                int orgX = colInRow(pos[1], "OrgX");
                if (orgX > 0) { L.OrgXCol = orgX; L.OriginButtonCol = orgX - 1; }
            }

            // 캘리브레이션 단계
            int[] posX = find("PosX").FirstOrDefault();
            if (posX != null && posX[0] >= 4)
            {
                int hr = posX[1], gx = posX[0], gy = colInRow(hr, "PosY");
                if (gy < 0) gy = gx + 1;
                int btn = gx - 4;
                int xyCount = 0;
                for (int r = hr + 1; r <= hr + 10 && has(btn, r); r++)
                {
                    string cap = Cols[gx - 3].ToString() + Cols[gx - 2] + Cols[gx - 1];
                    if (text(gx, r).Equals("PosT", StringComparison.OrdinalIgnoreCase))
                        L.CalSteps.Add(new CalStepDef(r, "θ", Loc(btn, r), cap, null, null, Loc(gy, r)));
                    else
                    {
                        string[] names = { "원점", "X 1", "X 2", "Y 1", "Y 2" };
                        string name = xyCount < names.Length ? names[xyCount] : "추가 " + xyCount;
                        L.CalSteps.Add(new CalStepDef(r, name, Loc(btn, r), cap, Loc(gx, r), Loc(gy, r), null));
                        xyCount++;
                    }
                }
            }

            // 계수
            int[] coef = find("Coef").FirstOrDefault();
            if (coef != null)
                for (int r = coef[1] + 1; r <= coef[1] + 10 && isLabel(coef[0], r); r++)
                    L.Coefs.Add(new[] { Loc(coef[0], r), Loc(coef[0] + 1, r) });

            // 보정값 (Origin → Cur → dPix → Robot → Result)
            int[] org = find("Origin").FirstOrDefault(p => text(p[0], p[1] + 1).Equals("Cur", StringComparison.OrdinalIgnoreCase));
            if (org != null && org[0] <= 22)
                for (int r = org[1]; r <= org[1] + 6 && isLabel(org[0], r); r++)
                    L.LiveRows.Add(new[] { Loc(org[0], r), Loc(org[0] + 1, r), Loc(org[0] + 2, r), Loc(org[0] + 3, r) });

            // 데이터 맵
            int[] dm = find("DATA MAP").FirstOrDefault();
            if (dm != null)
            {
                int hr = dm[1];
                L.DataNoCol = dm[0];
                L.DataNameCol = colInRow(hr, "Name");
                L.DataSourceCol = colInRow(hr, "Source");
                L.DataScaleCol = colInRow(hr, "Scale");
                L.DataOutCol = colInRow(hr, "Out");
                L.DataValidCol = colInRow(hr, "Valid");
                for (int r = hr + 1; r <= hr + 64 && text(L.DataNoCol, r).Length > 0 && has(L.DataOutCol, r); r++)
                    L.DataRows.Add(r);
            }

            L.OutputBufferCell = map.Where(kv => (kv.Value ?? "").Contains("FORMAT_OUT_DATA")).Select(kv => kv.Key).FirstOrDefault() ?? "Q2";
            return L;
        }

        /// <summary>포인트 블록 이름 행의 'X' / 'Y' / 'Theta' 라벨 → 아래 행 값 셀 3개 (없으면 null)</summary>
        public static string[] PointCoordCells(Dictionary<string, string> map, int blockRow)
        {
            int x = -1, y = -1, t = -1;
            for (int c = 0; c < 26; c++)
            {
                string v;
                if (!map.TryGetValue(Loc(c, blockRow), out v)) continue;
                v = (v ?? "").Trim();
                if (x < 0 && v.Equals("X", StringComparison.OrdinalIgnoreCase)) x = c;
                else if (y < 0 && v.Equals("Y", StringComparison.OrdinalIgnoreCase)) y = c;
                else if (t < 0 && v.Equals("Theta", StringComparison.OrdinalIgnoreCase)) t = c;
            }
            if (x < 0 || y < 0 || t < 0) return null;
            return new[] { Loc(x, blockRow + 1), Loc(y, blockRow + 1), Loc(t, blockRow + 1) };
        }
    }

    public class CalStepDef
    {
        public int Row { get; }
        public string Name { get; }
        public string ButtonCell { get; }
        public string CapturedCols { get; }     // 캡처 X/Y/θ 열 3글자
        public string JogX { get; }
        public string JogY { get; }
        public string JogT { get; }

        public CalStepDef(int row, string name, string buttonCell, string capturedCols, string jogX, string jogY, string jogT)
        {
            Row = row; Name = name; ButtonCell = buttonCell; CapturedCols = capturedCols; JogX = jogX; JogY = jogY; JogT = jogT;
        }
    }
}
