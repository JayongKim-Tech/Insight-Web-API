using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using VisionCore.Models.JobEditor;

namespace VisionCore.ViewModels
{
    /// <summary>
    /// 툴 1개의 사각 검사 영역(Region X, Y, Width, Height, Angle) + 기준 위치(Fixture X, Y, Theta).
    /// 좌표: 이미지 픽셀, Y 아래로 +, 각도는 시계 방향 + (In-Sight 도움말 기준).
    /// 이미지 좌표 = Fixture 원점 + R(Theta) * (Region 원점 + R(Angle) * (u, v))
    /// </summary>
    public class RoiViewModel : ViewModelBase
    {
        private readonly Func<CellArg, double?> _resolve;
        private int _ix = -1, _iy = -1, _iw = -1, _ih = -1, _ia = -1;
        private int _ifx = -1, _ify = -1, _ift = -1;
        private int _ir1 = -1, _ir2 = -1;   // 원/링 영역 반지름 (안쪽, 바깥쪽)

        public ToolItemViewModel Tool { get; }

        /// <summary>원/링 영역 (FindCircle 등 Annulus 그룹, Circle 그룹). X, Y = 중심</summary>
        public bool IsCircular { get; private set; }
        public bool HasInnerRadius => _ir1 >= 0;
        public double R1 { get; private set; }   // 안쪽 반지름 (Circle 이면 0)
        public double R2 { get; private set; }   // 바깥 반지름

        // Region (Fixture 좌표계)
        public double X { get; private set; }
        public double Y { get; private set; }
        public double W { get; private set; }
        public double H { get; private set; }
        public double A { get; private set; }

        // Fixture (이미지 좌표계)
        public double FX { get; private set; }
        public double FY { get; private set; }
        public double FT { get; private set; }

        /// <summary>Fixture가 수식이라 값을 못 구했으면 false (0으로 가정해서 그림 → 위치가 틀릴 수 있음)</summary>
        public bool FixtureResolved { get; private set; }

        public bool CanMove { get; private set; }
        public bool CanResize { get; private set; }
        public bool CanRotate { get; private set; }
        public bool IsEditing { get; set; }

        public string Label => Tool.Cell.Location + " " + Tool.Cell.FunctionName;

        private RoiViewModel(ToolItemViewModel tool, Func<CellArg, double?> resolve)
        {
            Tool = tool;
            _resolve = resolve;
        }

        /// <summary>
        /// Region 그룹(X/Y/Width/Height) = 사각 ROI, Annulus/Circle 그룹(X/Y/반지름) = 원/링 ROI
        /// </summary>
        public static RoiViewModel TryCreate(ToolItemViewModel tool, Func<CellArg, double?> resolve)
        {
            var roi = new RoiViewModel(tool, resolve);
            foreach (ToolParam p in tool.Cell.Params)
            {
                if (p.Def == null) continue;
                // "Region" (FindLine 등) / "Find Region" (FindPatMaxRedLine 등)
                if (p.Group == "Region" || (p.Group != null && p.Group.EndsWith(" Region")))
                {
                    switch (p.Def.Name)
                    {
                        case "X": roi._ix = p.Index; break;
                        case "Y": roi._iy = p.Index; break;
                        case "Width": roi._iw = p.Index; break;
                        case "Height": roi._ih = p.Index; break;
                        case "Angle": roi._ia = p.Index; break;
                    }
                }
                else if (p.Group == "Annulus" || p.Group == "Circle")
                {
                    switch (p.Def.Name)
                    {
                        case "X": roi._ix = p.Index; break;
                        case "Y": roi._iy = p.Index; break;
                        case "Inner Radius": roi._ir1 = p.Index; break;
                        case "Outer Radius":
                        case "Radius": roi._ir2 = p.Index; break;
                    }
                }
                else if (p.Group == "Fixture")
                {
                    switch (p.Def.Name)
                    {
                        case "X": roi._ifx = p.Index; break;
                        case "Y": roi._ify = p.Index; break;
                        case "Theta": roi._ift = p.Index; break;
                    }
                }
            }
            roi.IsCircular = roi._ir2 >= 0 && roi._iw < 0;
            if (roi._ix < 0 || roi._iy < 0 || (!roi.IsCircular && (roi._iw < 0 || roi._ih < 0))) return null;
            roi.Reload();
            return roi;
        }

        /// <summary>식(ToolCell)의 현재 값으로 다시 읽음</summary>
        public void Reload()
        {
            List<ToolParam> ps = Tool.Cell.Params;
            bool ok = true;
            X = Value(ps, _ix, ref ok);
            Y = Value(ps, _iy, ref ok);
            if (IsCircular)
            {
                R1 = _ir1 >= 0 ? Value(ps, _ir1, ref ok) : 0;
                R2 = Value(ps, _ir2, ref ok);
            }
            else
            {
                W = Value(ps, _iw, ref ok);
                H = Value(ps, _ih, ref ok);
            }
            A = _ia >= 0 ? Value(ps, _ia, ref ok) : 0;

            bool fixtureOk = true;
            FX = _ifx >= 0 ? Value(ps, _ifx, ref fixtureOk) : 0;
            FY = _ify >= 0 ? Value(ps, _ify, ref fixtureOk) : 0;
            FT = _ift >= 0 ? Value(ps, _ift, ref fixtureOk) : 0;
            FixtureResolved = fixtureOk;

            CanMove = Editable(ps, _ix) && Editable(ps, _iy);
            CanResize = IsCircular ? Editable(ps, _ir2) || Editable(ps, _ir1) : Editable(ps, _iw) && Editable(ps, _ih);
            CanRotate = _ia >= 0 && Editable(ps, _ia);
            OnPropertyChanged("Geometry");
        }

        private double Value(List<ToolParam> ps, int index, ref bool ok)
        {
            if (index < 0 || index >= ps.Count) { ok = false; return 0; }
            double? v = _resolve(ps[index].Arg);
            if (!v.HasValue) { ok = false; return 0; }
            return v.Value;
        }

        private static bool Editable(List<ToolParam> ps, int index)
        {
            return index >= 0 && index < ps.Count && ps[index].IsEditable && ps[index].Arg.Kind == CellArgKind.Number;
        }

        // ---- 좌표 변환 ----

        private static Vector Rotate(Vector v, double deg)
        {
            double r = deg * Math.PI / 180.0, c = Math.Cos(r), s = Math.Sin(r);
            return new Vector(v.X * c - v.Y * s, v.X * s + v.Y * c);
        }

        /// <summary>영역 로컬 (u, v) → 이미지 좌표</summary>
        public Point ToImage(double u, double v)
        {
            Vector f = new Vector(X, Y) + Rotate(new Vector(u, v), A);
            Vector img = new Vector(FX, FY) + Rotate(f, FT);
            return new Point(img.X, img.Y);
        }

        /// <summary>이미지 좌표 → Fixture 좌표계</summary>
        private Vector ToFixture(Point p)
        {
            return Rotate(new Vector(p.X - FX, p.Y - FY), -FT);
        }

        public Point[] Corners => new[] { ToImage(0, 0), ToImage(W, 0), ToImage(W, H), ToImage(0, H) };

        // ---- 드래그 (미리보기: 값만 바꾸고 센서 전송은 Commit) ----

        public void MoveBy(Point fromImage, Point toImage)
        {
            Vector d = ToFixture(toImage) - ToFixture(fromImage);
            X += d.X;
            Y += d.Y;
            OnPropertyChanged("Geometry");
        }

        /// <summary>원/링: 핸들을 끌어서 반지름 변경 (outer = 바깥, 아니면 안쪽). 안쪽 &lt; 바깥 유지</summary>
        public void ResizeRadius(Point image, bool outer)
        {
            const double MinGap = 2;
            double r = (ToFixture(image) - new Vector(X, Y)).Length;
            if (outer) R2 = Math.Max(r, HasInnerRadius ? R1 + MinGap : MinGap);
            else R1 = Math.Max(0, Math.Min(r, R2 - MinGap));
            OnPropertyChanged("Geometry");
        }

        public bool CanResizeOuter => IsCircular && Editable(Tool.Cell.Params, _ir2);
        public bool CanResizeInner => IsCircular && HasInnerRadius && Editable(Tool.Cell.Params, _ir1);

        /// <summary>원점 반대쪽 꼭짓점(W, H)을 끌어서 크기 변경</summary>
        public void ResizeTo(Point image)
        {
            ResizeHandle(image, 1, 1);
        }

        /// <summary>
        /// 핸들(hu, hv: 0 / 0.5 / 1 = 영역 로컬 비율 위치)을 끌어서 크기 변경. 반대쪽 변은 고정.
        /// 0.5 인 축은 변하지 않음 (변 가운데 핸들 = 한 방향만)
        /// </summary>
        public void ResizeHandle(Point image, double hu, double hv)
        {
            const double MinSize = 4;
            Vector local = Rotate(ToFixture(image) - new Vector(X, Y), -A);
            Vector shift = new Vector();

            if (hu == 1) W = Math.Max(MinSize, local.X);
            else if (hu == 0)
            {
                double right = W;
                double left = Math.Min(local.X, right - MinSize);
                W = right - left;
                shift.X = left;
            }

            if (hv == 1) H = Math.Max(MinSize, local.Y);
            else if (hv == 0)
            {
                double bottom = H;
                double top = Math.Min(local.Y, bottom - MinSize);
                H = bottom - top;
                shift.Y = top;
            }

            // 왼쪽/위쪽 변을 끌었으면 원점이 따라 이동 (영역 회전 방향으로)
            Vector o = Rotate(shift, A);
            X += o.X;
            Y += o.Y;
            OnPropertyChanged("Geometry");
        }

        /// <summary>
        /// 엣지 툴(Polarity 인자 있음) - 화면에 스캔 방향 화살표 표시 (영역 로컬 +X = Width 방향)
        /// </summary>
        public bool IsEdgeTool => Tool.Cell.Params.Any(p => p.Def != null && p.Def.Name == "Polarity");

        /// <summary>중심 기준 회전 (핸들은 영역 +X 방향에 있음)</summary>
        public void RotateTo(Point image)
        {
            Vector center = new Vector(X, Y) + Rotate(new Vector(W / 2, H / 2), A);
            Vector d = ToFixture(image) - center;
            if (d.Length < 1) return;
            A = Math.Round(Math.Atan2(d.Y, d.X) * 180.0 / Math.PI, 1);
            Vector origin = center - Rotate(new Vector(W / 2, H / 2), A);
            X = origin.X;
            Y = origin.Y;
            OnPropertyChanged("Geometry");
        }

        /// <summary>
        /// 드래그로 바뀐 값만 모아 한 번에 센서로 전송
        /// </summary>
        public async Task CommitAsync()
        {
            var changes = new Dictionary<int, string>();
            Collect(changes, _ix, X);
            Collect(changes, _iy, Y);
            if (IsCircular)
            {
                if (_ir1 >= 0) Collect(changes, _ir1, R1);
                Collect(changes, _ir2, R2);
            }
            else
            {
                Collect(changes, _iw, W);
                Collect(changes, _ih, H);
            }
            if (_ia >= 0) Collect(changes, _ia, A);

            if (changes.Count > 0) await Tool.ApplyManyAsync(changes);
            else Reload();
        }

        private void Collect(Dictionary<int, string> changes, int index, double value)
        {
            CellArg arg = Tool.Cell.Params[index].Arg;
            if (arg.Kind != CellArgKind.Number) return;
            string text = CellExpression.FormatNumber(Math.Round(value, 3));
            if (text != CellExpression.FormatNumber(arg.Number)) changes[index] = text;
        }
    }
}
