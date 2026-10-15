using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using VisionCore.ViewModels;

namespace VisionCore.Views
{
    /// <summary>
    /// 센서 이미지 위에 툴 ROI를 그리고 마우스로 이동/크기/회전 편집.
    /// 드래그 중에는 화면만 갱신, 마우스를 놓을 때 RoiViewModel.CommitAsync()로 센서에 1번 전송.
    /// 배율/화면 이동은 표시용일 뿐 ROI 값(이미지 픽셀 좌표)에는 영향 없음.
    ///   왼쪽 드래그: ROI 이동/크기/회전 (클릭 즉시 선택+드래그)
    ///   오른쪽(또는 휠 버튼) 드래그: 화면 이동 / 휠: 확대 / 더블클릭 빈 곳: 화면 맞춤
    ///   방향키: 선택 ROI 1px 이동 (Shift = 10px)
    /// </summary>
    public class RoiCanvas : Canvas
    {
        private enum DragMode { None, Move, Resize, Rotate, Pan }

        private class HitTag
        {
            public RoiViewModel Roi;
            public DragMode Mode;
            public double U, V;   // 크기 핸들 위치 (0 / 0.5 / 1)
        }

        private const double HandleSize = 11;
        private const double HandleHit = 18;          // 핸들 클릭 범위 (보이는 것보다 넓게)
        private const double RotateHandleGap = 30;
        private const double DragThreshold = 3;       // 이 거리 이하로 움직이면 클릭으로 간주 (실수로 미는 것 방지)

        private static readonly Brush NormalStroke = Freeze(new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)));
        private static readonly Brush SelectedStroke = Brushes.Orange;

        private readonly Image _image = new Image { IsHitTestVisible = false };
        private readonly DispatcherTimer _nudgeCommit;

        private double _fitScale = 1, _zoom = 1, _offX, _offY;
        private Vector _pan;
        private DragMode _mode;
        private HitTag _dragTag;
        private Point _downScreen, _lastImage, _lastScreen;
        private bool _dragStarted;
        private RoiViewModel _nudgeRoi;

        public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
            "Source", typeof(ImageSource), typeof(RoiCanvas), new PropertyMetadata(null, (d, e) => ((RoiCanvas)d).Redraw()));

        public static readonly DependencyProperty RoisProperty = DependencyProperty.Register(
            "Rois", typeof(IEnumerable), typeof(RoiCanvas), new PropertyMetadata(null, (d, e) => ((RoiCanvas)d).OnRoisChanged(e)));

        public static readonly DependencyProperty SelectedRoiProperty = DependencyProperty.Register(
            "SelectedRoi", typeof(RoiViewModel), typeof(RoiCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((RoiCanvas)d).Redraw()));

        public ImageSource Source
        {
            get => (ImageSource)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        public IEnumerable Rois
        {
            get => (IEnumerable)GetValue(RoisProperty);
            set => SetValue(RoisProperty, value);
        }

        public RoiViewModel SelectedRoi
        {
            get => (RoiViewModel)GetValue(SelectedRoiProperty);
            set => SetValue(SelectedRoiProperty, value);
        }

        public RoiCanvas()
        {
            Background = new SolidColorBrush(Color.FromRgb(0x20, 0x22, 0x26));
            ClipToBounds = true;
            Focusable = true;
            FocusVisualStyle = null;
            SizeChanged += (s, e) => Redraw();

            _nudgeCommit = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _nudgeCommit.Tick += async (s, e) =>
            {
                _nudgeCommit.Stop();
                RoiViewModel roi = _nudgeRoi;
                _nudgeRoi = null;
                if (roi != null) await roi.CommitAsync();
            };
        }

        private static Brush Freeze(Brush b) { b.Freeze(); return b; }

        private double ImageWidth => Source is System.Windows.Media.Imaging.BitmapSource b ? b.PixelWidth : (Source?.Width ?? 0);
        private double ImageHeight => Source is System.Windows.Media.Imaging.BitmapSource b ? b.PixelHeight : (Source?.Height ?? 0);
        private double Scale => _fitScale * _zoom;

        /// <summary>화면 맞춤 (배율 1, 이동 초기화)</summary>
        public void FitToView()
        {
            _zoom = 1;
            _pan = new Vector();
            Redraw();
        }

        private void OnRoisChanged(DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is INotifyCollectionChanged oldCol) oldCol.CollectionChanged -= Rois_CollectionChanged;
            if (e.NewValue is INotifyCollectionChanged newCol) newCol.CollectionChanged += Rois_CollectionChanged;
            Resubscribe(e.OldValue as IEnumerable, e.NewValue as IEnumerable);
            Redraw();
        }

        private void Rois_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            Resubscribe(e.OldItems, e.NewItems);
            if (e.Action == NotifyCollectionChangedAction.Reset && Rois != null)
                Resubscribe(null, Rois);
            Redraw();
        }

        private void Resubscribe(IEnumerable removed, IEnumerable added)
        {
            if (removed != null) foreach (RoiViewModel r in removed.OfType<RoiViewModel>()) r.PropertyChanged -= Roi_PropertyChanged;
            if (added != null) foreach (RoiViewModel r in added.OfType<RoiViewModel>()) { r.PropertyChanged -= Roi_PropertyChanged; r.PropertyChanged += Roi_PropertyChanged; }
        }

        private void Roi_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            Redraw();
        }

        // ---- 좌표 ----

        private Point ToScreen(Point p) => new Point(_offX + p.X * Scale, _offY + p.Y * Scale);
        private Point ToImage(Point s) => new Point((s.X - _offX) / Scale, (s.Y - _offY) / Scale);

        private void Layout()
        {
            double iw = ImageWidth, ih = ImageHeight;
            if (iw <= 0 || ih <= 0 || ActualWidth <= 0 || ActualHeight <= 0) return;
            _fitScale = Math.Min(ActualWidth / iw, ActualHeight / ih);
            _offX = (ActualWidth - iw * Scale) / 2 + _pan.X;
            _offY = (ActualHeight - ih * Scale) / 2 + _pan.Y;
        }

        // ---- 그리기 (이미지 요소는 재사용, 오버레이만 다시 그림) ----

        private void Redraw()
        {
            Children.Clear();
            if (Source == null)
            {
                Children.Add(new TextBlock { Text = "이미지 없음 - [이미지 갱신] 또는 [촬영]", Foreground = Brushes.Gray, Margin = new Thickness(12) });
                return;
            }

            Layout();
            if (_image.Source != Source) _image.Source = Source;
            _image.Width = ImageWidth * Scale;
            _image.Height = ImageHeight * Scale;
            SetLeft(_image, _offX);
            SetTop(_image, _offY);
            Children.Add(_image);

            if (Rois == null) return;
            RoiViewModel selected = SelectedRoi;
            List<RoiViewModel> rois = Rois.OfType<RoiViewModel>().ToList();
            foreach (RoiViewModel roi in rois)
                if (roi != selected) DrawRoi(roi, false);
            if (selected != null && rois.Contains(selected))
                DrawRoi(selected, true);
        }

        /// <summary>원/링 ROI: 링(안쪽 구멍 제외) 채우기 + 바깥/안쪽 원. 핸들 = 오른쪽(이미지 +X)의 반지름 점</summary>
        private void DrawCircular(RoiViewModel roi, bool selected)
        {
            Brush stroke = selected ? SelectedStroke : NormalStroke;
            Point c = ToScreen(roi.ToImage(0, 0));
            double r2 = roi.R2 * Scale, r1 = roi.R1 * Scale;

            var ring = new GeometryGroup { FillRule = FillRule.EvenOdd };
            ring.Children.Add(new EllipseGeometry(c, r2, r2));
            if (r1 > 0) ring.Children.Add(new EllipseGeometry(c, r1, r1));
            Children.Add(new Path
            {
                Data = ring,
                Stroke = stroke,
                StrokeThickness = selected ? 2.5 : 1.5,
                StrokeDashArray = roi.FixtureResolved ? null : new DoubleCollection { 4, 3 },
                Fill = new SolidColorBrush(Color.FromArgb(selected ? (byte)45 : (byte)18, 0xFF, 0xA5, 0x00)),
                Tag = new HitTag { Roi = roi, Mode = DragMode.Move },
                Cursor = roi.CanMove ? Cursors.SizeAll : Cursors.Hand,
                ToolTip = selected ? null : roi.Label + " (클릭해서 선택)"
            });

            // 중심 십자
            Children.Add(new Line { X1 = c.X - 6, Y1 = c.Y, X2 = c.X + 6, Y2 = c.Y, Stroke = stroke, StrokeThickness = 1, IsHitTestVisible = false });
            Children.Add(new Line { X1 = c.X, Y1 = c.Y - 6, X2 = c.X, Y2 = c.Y + 6, Stroke = stroke, StrokeThickness = 1, IsHitTestVisible = false });

            var label = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                Padding = new Thickness(4, 1, 4, 1),
                IsHitTestVisible = false,
                Child = new TextBlock { Text = roi.Label + (roi.FixtureResolved ? "" : " (기준 위치 계산 불가)"), Foreground = stroke, FontSize = 11 }
            };
            SetLeft(label, c.X - r2 + 4);
            SetTop(label, c.Y - r2 - 20);
            Children.Add(label);

            if (!selected) return;
            if (roi.CanResizeOuter)
                AddHandle(new Point(c.X + r2, c.Y), new HitTag { Roi = roi, Mode = DragMode.Resize, U = 1 }, Cursors.SizeWE, false);
            if (roi.CanResizeInner)
                AddHandle(new Point(c.X + r1, c.Y), new HitTag { Roi = roi, Mode = DragMode.Resize, U = 0 }, Cursors.SizeWE, true);
        }

        private void DrawRoi(RoiViewModel roi, bool selected)
        {
            if (roi.IsCircular) { DrawCircular(roi, selected); return; }
            Brush stroke = selected ? SelectedStroke : NormalStroke;
            Point[] corners = roi.Corners.Select(ToScreen).ToArray();

            var poly = new Polygon
            {
                Points = new PointCollection(corners),
                Stroke = stroke,
                StrokeThickness = selected ? 2.5 : 1.5,
                StrokeDashArray = roi.FixtureResolved ? null : new DoubleCollection { 4, 3 },
                Fill = new SolidColorBrush(Color.FromArgb(selected ? (byte)45 : (byte)18, 0xFF, 0xA5, 0x00)),
                Tag = new HitTag { Roi = roi, Mode = DragMode.Move },
                Cursor = roi.CanMove ? Cursors.SizeAll : Cursors.Hand,
                ToolTip = selected ? null : roi.Label + " (클릭해서 선택)"
            };
            Children.Add(poly);

            DrawDirection(roi, selected);

            // 라벨
            Point o = corners[0];
            var label = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                Padding = new Thickness(4, 1, 4, 1),
                IsHitTestVisible = false,
                Child = new TextBlock { Text = roi.Label + (roi.FixtureResolved ? "" : " (기준 위치 계산 불가)"), Foreground = stroke, FontSize = 11 }
            };
            SetLeft(label, o.X + 4);
            SetTop(label, o.Y - 20);
            Children.Add(label);

            if (!selected) return;

            // 크기 핸들 8개 (꼭짓점 4 + 변 가운데 4)
            if (roi.CanResize)
            {
                double[] steps = { 0, 0.5, 1 };
                foreach (double u in steps)
                    foreach (double v in steps)
                    {
                        if (u == 0.5 && v == 0.5) continue;
                        Point c = ToScreen(roi.ToImage(roi.W * u, roi.H * v));
                        AddHandle(c, new HitTag { Roi = roi, Mode = DragMode.Resize, U = u, V = v }, ResizeCursor(roi, u, v), false);
                    }
            }

            // 회전 핸들: 위쪽 변 가운데에서 바깥(-Y 방향)으로
            if (roi.CanRotate)
            {
                Point edge = ToScreen(roi.ToImage(roi.W / 2, 0));
                Point inner = ToScreen(roi.ToImage(roi.W / 2, roi.H / 2));
                Vector v = edge - inner;
                if (v.Length < 0.01) v = new Vector(0, -1);
                v.Normalize();
                Point r = edge + v * RotateHandleGap;
                Children.Add(new Line { X1 = edge.X, Y1 = edge.Y, X2 = r.X, Y2 = r.Y, Stroke = SelectedStroke, StrokeThickness = 1.5, IsHitTestVisible = false });
                AddHandle(r, new HitTag { Roi = roi, Mode = DragMode.Rotate }, Cursors.Hand, true);
            }
        }

        /// <summary>
        /// 엣지 툴만: 영역 가운데를 가로지르는 스캔 방향 화살표 1개 (ROI와 같은 색).
        /// 픽셀은 로컬 Y 방향으로 합산(투영)하고, 엣지는 로컬 +X(Width) 방향으로 탐색 → 찾는 선은 로컬 Y축과 평행
        /// (실제 센서 화면 확인: Angle 90 영역 → 세로 스캔, 수평 엣지)
        /// </summary>
        private void DrawDirection(RoiViewModel roi, bool selected)
        {
            if (!roi.IsEdgeTool) return;
            Point a = ToScreen(roi.ToImage(roi.W * 0.15, roi.H / 2));
            Point b = ToScreen(roi.ToImage(roi.W * 0.85, roi.H / 2));
            DrawArrow(a, b, selected ? SelectedStroke : NormalStroke, selected ? 2.5 : 1.5, selected ? 1.0 : 0.7);
        }

        private void DrawArrow(Point a, Point b, Brush brush, double thickness, double opacity)
        {
            Vector v = b - a;
            if (v.Length < 4) return;
            Vector n = v; n.Normalize();
            Vector side = new Vector(-n.Y, n.X);
            double head = Math.Min(12, v.Length * 0.35);

            // 머리 부분은 선을 짧게 끊어서 끝이 뾰족하게
            Point shaftEnd = b - n * head * 0.8;
            Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = shaftEnd.X, Y2 = shaftEnd.Y, Stroke = brush, StrokeThickness = thickness, Opacity = opacity, IsHitTestVisible = false });
            Children.Add(new Polygon
            {
                Points = new PointCollection { b, b - n * head + side * head * 0.55, b - n * head - side * head * 0.55 },
                Fill = brush, Opacity = opacity, IsHitTestVisible = false
            });
        }

        private void AddHandle(Point c, HitTag tag, Cursor cursor, bool round)
        {
            // 넓은 투명 히트 영역 + 보이는 작은 핸들
            var hit = new Rectangle { Width = HandleHit, Height = HandleHit, Fill = Brushes.Transparent, Tag = tag, Cursor = cursor };
            SetLeft(hit, c.X - HandleHit / 2);
            SetTop(hit, c.Y - HandleHit / 2);

            Shape visual = round
                ? (Shape)new Ellipse { Width = HandleSize + 2, Height = HandleSize + 2, Fill = SelectedStroke, Stroke = Brushes.White, StrokeThickness = 2 }
                : new Rectangle { Width = HandleSize, Height = HandleSize, Fill = Brushes.White, Stroke = SelectedStroke, StrokeThickness = 2 };
            visual.IsHitTestVisible = false;
            SetLeft(visual, c.X - visual.Width / 2);
            SetTop(visual, c.Y - visual.Height / 2);

            Children.Add(visual);
            Children.Add(hit);
        }

        /// <summary>핸들이 화면에서 놓인 방향에 맞는 커서 (회전된 영역도 자연스럽게)</summary>
        private Cursor ResizeCursor(RoiViewModel roi, double u, double v)
        {
            Point c = ToScreen(roi.ToImage(roi.W / 2, roi.H / 2));
            Point h = ToScreen(roi.ToImage(roi.W * u, roi.H * v));
            double deg = Math.Atan2(h.Y - c.Y, h.X - c.X) * 180 / Math.PI;
            deg = (deg + 360) % 180;   // 0~180
            if (deg < 22.5 || deg >= 157.5) return Cursors.SizeWE;
            if (deg < 67.5) return Cursors.SizeNWSE;
            if (deg < 112.5) return Cursors.SizeNS;
            return Cursors.SizeNESW;
        }

        // ---- 마우스 ----

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            if (Source == null) return;
            Focus();

            Point s = e.GetPosition(this);
            _downScreen = _lastScreen = s;
            _lastImage = ToImage(s);
            _dragStarted = false;

            // 오른쪽/휠 버튼 = 화면 이동
            if (e.ChangedButton == MouseButton.Right || e.ChangedButton == MouseButton.Middle)
            {
                _mode = DragMode.Pan;
                CaptureMouse();
                e.Handled = true;
                return;
            }
            if (e.ChangedButton != MouseButton.Left) return;

            var tag = (e.OriginalSource as FrameworkElement)?.Tag as HitTag;
            if (tag == null)
            {
                if (e.ClickCount == 2) FitToView();
                return;
            }

            // 클릭 즉시 선택 + 드래그 시작
            if (SelectedRoi != tag.Roi) SelectedRoi = tag.Roi;
            if (tag.Mode == DragMode.Move && !tag.Roi.CanMove) return;

            _mode = tag.Mode;
            _dragTag = tag;
            tag.Roi.IsEditing = true;
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_mode == DragMode.None || !IsMouseCaptured) return;

            Point s = e.GetPosition(this);
            if (!_dragStarted)
            {
                if ((s - _downScreen).Length < DragThreshold) return;
                _dragStarted = true;
            }

            Point p = ToImage(s);
            switch (_mode)
            {
                case DragMode.Pan:
                    _pan += s - _lastScreen;
                    Redraw();
                    break;
                case DragMode.Move:
                    _dragTag.Roi.MoveBy(_lastImage, p);
                    break;
                case DragMode.Resize:
                    if (_dragTag.Roi.IsCircular) _dragTag.Roi.ResizeRadius(p, _dragTag.U == 1);
                    else _dragTag.Roi.ResizeHandle(p, _dragTag.U, _dragTag.V);
                    break;
                case DragMode.Rotate:
                    // 핸들이 -Y 방향에 있으므로 +90도 보정해서 영역 X축 각도로
                    _dragTag.Roi.RotateTo(RotatePoint(_dragTag.Roi, p));
                    break;
            }
            _lastScreen = s;
            _lastImage = p;
        }

        /// <summary>
        /// RotateTo는 "중심 → 마우스 = 영역 +X 방향" 기준. 회전 핸들은 -Y(위쪽)에 있으므로 마우스를 중심 기준 +90도 돌려서 넘김
        /// </summary>
        private static Point RotatePoint(RoiViewModel roi, Point mouse)
        {
            Point c = roi.ToImage(roi.W / 2, roi.H / 2);
            Vector d = mouse - c;
            // 화면(Y 아래) 기준 +90도 = (x, y) → (-y, x)
            return c + new Vector(-d.Y, d.X);
        }

        protected override async void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (!IsMouseCaptured) return;
            ReleaseMouseCapture();

            DragMode mode = _mode;
            HitTag tag = _dragTag;
            bool moved = _dragStarted;
            _mode = DragMode.None;
            _dragTag = null;

            if (tag != null && mode != DragMode.Pan)
            {
                tag.Roi.IsEditing = false;
                if (moved) await tag.Roi.CommitAsync();
            }
            e.Handled = true;
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            // 오른쪽 드래그로 화면 이동할 때 컨텍스트 메뉴 등 방지
            base.OnMouseRightButtonUp(e);
            e.Handled = true;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Source == null) return;

            // 마우스 위치 기준 확대/축소 (표시만 바뀜)
            Point s = e.GetPosition(this);
            Point before = ToImage(s);
            _zoom = Math.Max(1, Math.Min(20, _zoom * (e.Delta > 0 ? 1.2 : 1 / 1.2)));
            if (_zoom == 1) _pan = new Vector();
            Layout();
            Point after = ToScreen(before);
            if (_zoom > 1) _pan += s - after;
            Redraw();
            e.Handled = true;
        }

        // ---- 방향키 미세 이동 ----

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            RoiViewModel roi = SelectedRoi;
            if (roi == null || !roi.CanMove) return;

            double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
            Vector d;
            switch (e.Key)
            {
                case Key.Left: d = new Vector(-step, 0); break;
                case Key.Right: d = new Vector(step, 0); break;
                case Key.Up: d = new Vector(0, -step); break;
                case Key.Down: d = new Vector(0, step); break;
                default: return;
            }

            Point from = roi.ToImage(0, 0);
            roi.MoveBy(from, from + d);

            // 연속으로 누르는 동안은 화면만, 멈추고 0.5초 뒤 1번 전송
            _nudgeRoi = roi;
            _nudgeCommit.Stop();
            _nudgeCommit.Start();
            e.Handled = true;
        }
    }
}
