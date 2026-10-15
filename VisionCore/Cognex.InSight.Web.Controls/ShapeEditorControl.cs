using Cognex.InSight.Remoting.Serialization;
using Cognex.InSight.Web;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Cognex.InSight.Web.Controls
{
  /// <summary>
  /// In-Sight Suite처럼 이미지 위에서 EditPoint / EditLine / EditCircle / EditAnnulus / EditRegion / EditPolygon 값을 편집하는 화면.
  /// 팝업(ShapeEditorForm)과 ToolSetup의 오버레이 탭(WindowsFormsHost)에서 같이 사용.
  ///  - 왼쪽 드래그: 도형 이동 / 핸들(작은 네모)로 크기·끝점·꼭짓점, 노란 원으로 회전
  ///  - 휠: 확대/축소, 오른쪽/가운데 드래그: 화면 이동, 빈 곳 더블클릭: 화면 맞춤
  ///  - 방향키 1px (Shift 10px), Ctrl+방향키 크기, Q/E 회전, Tab 핸들 선택
  ///
  /// LiveCommit = true 면 마우스를 놓을 때 / 방향키를 멈출 때 Committed 이벤트 (호스트가 센서에 바로 적용).
  /// LinkedLocations 의 셀이 그린 그래픽(편집 셀을 참조하는 PlotString 등)은 드래그 중 도형과 같이 움직이는 미리보기로 표시하고,
  /// 적용 후 SetContext로 센서가 다시 그린 그래픽을 받으면 실제 위치로 맞춰짐.
  ///
  /// 편집은 "화면 방향" 좌표(가로 u, 세로 v = 이미지 픽셀)로 하고, 저장할 때 센서 좌표로 변환.
  /// 센서 좌표: XY 모드는 (X=가로, Y=세로), 레거시 In-Sight 모드는 (X=행=세로, Y=열=가로) - DisplayContext와 같은 규칙
  /// </summary>
  public class ShapeEditorControl : UserControl
  {
    private static readonly HttpClient _http = new HttpClient();

    private const float HandleSize = 8f;
    private const float RotateHandleGap = 28f;   // 화면 픽셀

    private CvsInSight _inSight;
    private CvsCogShape _shape;
    private string _location;
    private bool _xy = true;

    // 참고 그래픽: 고정(배경 버퍼) / 연결(편집 셀을 참조 → 드래그 중 같이 움직임)
    private CvsCogShape[] _context = new CvsCogShape[0];
    private CvsCogShape[] _staticContext = new CvsCogShape[0];
    private CvsCogShape[] _linkedContext = new CvsCogShape[0];
    private HashSet<string> _linked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private PointF _baselineAnchor;          // 현재 참고 그래픽이 그려졌을 때의 도형 기준점
    private PointF? _pendingBaseline;        // 적용(Committed) 시점의 기준점 → 다음 SetContext에서 사용

    // 이미지 + 고정 참고 그래픽은 화면 크기 비트맵에 한 번만 그려두고(확대/이동/크기 변경 때만 다시), 드래그 중엔 도형만 다시 그림
    private Bitmap _backBuffer;
    private bool _backDirty = true;
    private int _activeHandle = -1;   // 키보드 방향키로 움직일 핸들 (-1 = 도형 전체)

    private readonly ImagePanel _view;
    private readonly Label _status;
    private readonly FlowLayoutPanel _fields;
    private readonly Timer _nudgeTimer;
    private readonly List<KeyValuePair<string, NumericUpDown>> _numbers = new List<KeyValuePair<string, NumericUpDown>>();
    private bool _syncingFields;

    private Bitmap _image;
    private float _zoom = 1f;
    private PointF _pan;           // 화면 = pan + 이미지좌표 * zoom
    private bool _fitted;

    // 드래그 상태
    private enum DragKind { None, Move, Handle, Rotate, Pan }
    private DragKind _drag;
    private int _dragHandle;
    private PointF _dragLastImage;
    private Point _panStartMouse;
    private PointF _panStart;

    // Region (화면 방향): 원점 O, 1축 각도 Phi(도), 길이 L1/L2, 2축 = Phi + Sign*90
    private PointF _rO;
    private double _rPhi, _rL1, _rL2;
    private int _rSign = 1;

    /// <summary>LiveCommit 모드에서 편집이 끝날 때마다 (도형은 센서 값으로 정리된 상태)</summary>
    public event Action<CvsCogShape> Committed;

    /// <summary>true = 마우스 놓을 때/방향키 멈출 때 Committed 발생 (오버레이 탭). false = 팝업 (적용 버튼)</summary>
    public bool LiveCommit { get; set; }

    public CvsCogShape Shape
    {
      get { SaveRegion(); return _shape; }
    }

    public string Location
    {
      get { return _location; }
    }

    public ShapeEditorControl()
    {
      Font = new Font("Malgun Gothic", 9F);

      _view = new ImagePanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(40, 40, 40) };
      _view.Paint += View_Paint;
      _view.MouseDown += View_MouseDown;
      _view.MouseMove += View_MouseMove;
      _view.MouseUp += View_MouseUp;
      _view.MouseWheel += View_MouseWheel;
      _view.MouseDoubleClick += (s, e) => { if (HitTest(ToImage(e.Location)) == DragKind.None) FitToView(); };
      _view.Resize += (s, e) => { if (!_fitted && _view.ClientSize.Width > 1) FitToView(); else InvalidateView(true); };

      _status = new Label { Dock = DockStyle.Top, Height = 24, Padding = new Padding(6, 4, 6, 0), BackColor = Color.FromArgb(245, 245, 240), AutoEllipsis = true };
      _status.Text = HelpText;

      var bottom = new Panel { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(4) };
      var fit = new Button { Text = "화면 맞춤", Dock = DockStyle.Right, Width = 80 };
      fit.Click += (s, e) => FitToView();
      _fields = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
      bottom.Controls.Add(_fields);
      bottom.Controls.Add(fit);

      Controls.Add(_view);
      Controls.Add(_status);
      Controls.Add(bottom);

      _nudgeTimer = new Timer { Interval = 400 };
      _nudgeTimer.Tick += (s, e) => { _nudgeTimer.Stop(); RaiseCommitted(); };

      Disposed += (s, e) => { _image?.Dispose(); _backBuffer?.Dispose(); _nudgeTimer.Dispose(); };
    }

    private const string HelpText = "드래그 = 이동 · 핸들 = 크기/끝점 · 노란 원 = 회전 · 방향키 = 1px (Shift 10px) · Ctrl+방향키 = 크기 · Q/E = 회전 · Tab = 핸들 선택 · 휠 = 확대 · 오른쪽 드래그 = 화면 이동";

    #region 외부 API

    public void SetSensor(CvsInSight inSight)
    {
      _inSight = inSight;
      _xy = inSight == null || inSight.UsesXYCoordinates;
    }

    /// <summary>편집할 도형 (null = 선택 없음). 도형 객체는 직접 수정됨</summary>
    public void SetShape(string location, CvsCogShape shape)
    {
      _nudgeTimer.Stop();
      _location = location;
      _shape = shape;
      _activeHandle = -1;
      _drag = DragKind.None;
      _pendingBaseline = null;
      LoadRegion();
      _baselineAnchor = Anchor();
      BuildFields();
      SplitContext();
      InvalidateView(true);
    }

    /// <summary>편집 셀을 참조하는 셀들 (그 셀이 그린 그래픽은 도형과 같이 움직이는 미리보기)</summary>
    public void SetLinkedLocations(IEnumerable<string> locations)
    {
      _linked = new HashSet<string>((locations ?? Enumerable.Empty<string>()).Select(Normalize), StringComparer.OrdinalIgnoreCase);
      SplitContext();
      InvalidateView(true);
    }

    /// <summary>센서가 그린 참고 그래픽 교체 (적용 후 다시 받은 것이면 연결 그래픽이 실제 위치로)</summary>
    public void SetContext(CvsCogShape[] context)
    {
      _context = context ?? new CvsCogShape[0];
      _baselineAnchor = _pendingBaseline ?? Anchor();
      _pendingBaseline = null;
      SplitContext();
      InvalidateView(true);
    }

    public void SetImage(Bitmap image)
    {
      _image?.Dispose();
      _image = image;
      if (!_fitted) FitToView();
      else InvalidateView(true);
    }

    /// <summary>센서의 현재 이미지 + 그래픽을 받아옴</summary>
    public async Task ReloadAsync()
    {
      if (_inSight == null) return;
      Task<CvsCogShape[]> graphicsTask = _inSight.GetGraphicsAsync();
      try
      {
        CvsCogViewPort vp = _inSight.ViewPort;
        string url = _inSight.GetMainImageUrl(vp != null && vp.Width > 0 ? vp.Width : -1, vp != null && vp.Height > 0 ? vp.Height : -1);
        byte[] bytes = await _http.GetByteArrayAsync(url);
        using (var ms = new MemoryStream(bytes))
        using (var tmp = new Bitmap(ms))
          SetImage(new Bitmap(tmp));
      }
      catch (Exception ex)
      {
        Debug.WriteLine("ShapeEditorControl image: " + ex.Message);
        _status.Text = "이미지를 받지 못했습니다 (" + ex.Message + ") - 좌표만 편집 가능";
      }
      try { SetContext(await graphicsTask); }
      catch (Exception ex) { Debug.WriteLine("ShapeEditorControl graphics: " + ex.Message); }
    }

    /// <summary>표시 설정(글자 배율 등)이 바뀌었을 때 배경까지 다시 그림</summary>
    public void RedrawAll()
    {
      InvalidateView(true);
    }

    /// <summary>숫자 칸에 입력만 하고 바로 적용한 경우 대비: Value를 읽어야 입력이 반영됨</summary>
    public void FlushFields()
    {
      foreach (var kv in _numbers) { decimal unused = kv.Value.Value; }
      SaveRegion();
    }

    /// <summary>편집 가능한 도형인지</summary>
    public static bool IsSupported(object value)
    {
      return value is CvsCogRegion || value is CvsCogPoint || value is CvsCogLine || value is CvsCogCircle
          || value is CvsCogAnnulus || (value is CvsCogPolygon && ((CvsCogPolygon)value).Points != null && ((CvsCogPolygon)value).Points.Length >= 6);
    }

    /// <summary>
    /// 결과 셀 값(도형) → SetCellValue로 보낼 수 있는 편집용 복사본 (색/라벨 등 편집 불가 필드 제거)
    /// </summary>
    public static CvsCogShape ToEditable(object value)
    {
      JToken token = JToken.FromObject(value);
      string[] nonEditableFields = new string[] { "color", "font", "fontSize", "graphicId", "label", "lineThickness", "name", "runtimeEditable", "source",
          "showAxesLabels", "showScanLine", "showXArrow", "showYArrow" };
      SerializationHelper.RemoveFields(token, nonEditableFields);
      return CvsInSight.JsonSerializer.DeserializeObject(token.ToString()) as CvsCogShape;
    }

    public static string Normalize(string location)
    {
      return (location ?? "").Replace("$", "").Trim().ToUpperInvariant();
    }

    #endregion

    private void SplitContext()
    {
      string self = Normalize(_location);
      var stat = new List<CvsCogShape>();
      var linked = new List<CvsCogShape>();
      foreach (CvsCogShape g in _context)
      {
        if (g == null) continue;
        string loc = Normalize(g.CellLocation);
        if (loc.Length > 0 && loc == self) continue;   // 편집 중인 도형은 직접 그림
        if (loc.Length > 0 && _linked.Contains(loc)) linked.Add(g);
        else stat.Add(g);
      }
      _staticContext = stat.ToArray();
      _linkedContext = linked.ToArray();
    }

    private void RaiseCommitted()
    {
      if (!LiveCommit || _shape == null) return;
      SaveRegion();
      // 다음에 받는 참고 그래픽은 "지금 도형 상태로 센서가 다시 그린 것" → 지금 기준점을 기억
      _pendingBaseline = Anchor();
      Committed?.Invoke(_shape);
    }

    /// <param name="background">true = 확대/이동/이미지 변경 → 배경까지 다시 그림</param>
    private void InvalidateView(bool background)
    {
      if (background) _backDirty = true;
      _view.Invalidate();
    }

    #region 좌표 변환

    /// <summary>센서 좌표 → 화면 방향 이미지 좌표</summary>
    private PointF S2D(double x, double y)
    {
      return _xy ? new PointF((float)x, (float)y) : new PointF((float)y, (float)x);
    }

    /// <summary>화면 방향 이미지 좌표 → 센서 좌표 (x, y)</summary>
    private void D2S(PointF p, out double x, out double y)
    {
      if (_xy) { x = p.X; y = p.Y; }
      else { x = p.Y; y = p.X; }
      x = Math.Round(x, 2);
      y = Math.Round(y, 2);
    }

    private PointF ToScreen(PointF img)
    {
      return new PointF(_pan.X + img.X * _zoom, _pan.Y + img.Y * _zoom);
    }

    private PointF ToImage(Point screen)
    {
      return new PointF((screen.X - _pan.X) / _zoom, (screen.Y - _pan.Y) / _zoom);
    }

    public void FitToView()
    {
      if (_view.ClientSize.Width < 2 || _view.ClientSize.Height < 2) return;
      SizeF size = _image != null ? (SizeF)_image.Size : ShapeBounds().Size;
      if (size.Width < 1 || size.Height < 1) size = new SizeF(640, 480);
      float w = Math.Max(1, _view.ClientSize.Width - 20), h = Math.Max(1, _view.ClientSize.Height - 20);
      _zoom = Math.Min(w / size.Width, h / size.Height);
      _pan = new PointF((_view.ClientSize.Width - size.Width * _zoom) / 2, (_view.ClientSize.Height - size.Height * _zoom) / 2);
      _fitted = _image != null;
      InvalidateView(true);
    }

    private RectangleF ShapeBounds()
    {
      // 이미지를 못 받았을 때: 원점 ~ 도형 끝까지 보이게
      PointF[] pts = Outline().Concat(Handles()).ToArray();
      if (pts.Length == 0) return new RectangleF(0, 0, 640, 480);
      return new RectangleF(0, 0, Math.Max(640, pts.Max(p => p.X) + 50), Math.Max(480, pts.Max(p => p.Y) + 50));
    }

    /// <summary>도형 기준점 (연결 그래픽 미리보기 이동량 계산용)</summary>
    private PointF Anchor()
    {
      if (_shape is CvsCogRegion) return _rO;
      if (_shape is CvsCogPoint) { var p = (CvsCogPoint)_shape; return S2D(p.X, p.Y); }
      if (_shape is CvsCogLine) { var l = (CvsCogLine)_shape; return S2D(l.X0, l.Y0); }
      if (_shape is CvsCogCircle) { var c = (CvsCogCircle)_shape; return S2D(c.X, c.Y); }
      if (_shape is CvsCogAnnulus) { var a = (CvsCogAnnulus)_shape; return S2D(a.X, a.Y); }
      if (_shape is CvsCogPolygon) { PointF[] p = PolygonPoints(); return p.Length > 0 ? p[0] : PointF.Empty; }
      return PointF.Empty;
    }

    #endregion

    #region Region (회전 사각형) 변환

    // XY:     O=(X,Y),  Phi=Angle,      L1=Width,  L2=Height, 2축=+90
    // 레거시: O=(Y,X),  Phi=90-Angle,   L1=Height, L2=Width,  2축=-90   (RegionDimensions.calculateRotatedRectangle 과 같은 결과)
    private void LoadRegion()
    {
      var r = _shape as CvsCogRegion;
      if (r == null) return;
      _rO = S2D(r.X, r.Y);
      if (_xy) { _rPhi = r.Angle; _rL1 = r.Width; _rL2 = r.Height; _rSign = 1; }
      else { _rPhi = 90 - r.Angle; _rL1 = r.Height; _rL2 = r.Width; _rSign = -1; }
    }

    private void SaveRegion()
    {
      var r = _shape as CvsCogRegion;
      if (r == null) return;
      double x, y;
      D2S(_rO, out x, out y);
      r.X = x;
      r.Y = y;
      double angle = _xy ? _rPhi : 90 - _rPhi;
      angle = Math.Round(NormalizeAngle(angle), 2);
      r.Angle = angle;
      if (_xy) { r.Width = Math.Round(_rL1, 2); r.Height = Math.Round(_rL2, 2); }
      else { r.Height = Math.Round(_rL1, 2); r.Width = Math.Round(_rL2, 2); }
    }

    private static double NormalizeAngle(double a)
    {
      a %= 360;
      if (a > 180) a -= 360;
      if (a <= -180) a += 360;
      return a;
    }

    private PointF E1 { get { double t = _rPhi * Math.PI / 180; return new PointF((float)Math.Cos(t), (float)Math.Sin(t)); } }
    private PointF E2 { get { PointF e = E1; return new PointF(-e.Y * _rSign, e.X * _rSign); } }

    private PointF RegionPoint(double u, double v)
    {
      PointF e1 = E1, e2 = E2;
      return new PointF((float)(_rO.X + u * e1.X + v * e2.X), (float)(_rO.Y + u * e1.Y + v * e2.Y));
    }

    private void RegionLocal(PointF p, out double u, out double v)
    {
      PointF e1 = E1, e2 = E2;
      double dx = p.X - _rO.X, dy = p.Y - _rO.Y;
      u = dx * e1.X + dy * e1.Y;
      v = dx * e2.X + dy * e2.Y;
    }

    // 핸들 비율 위치 (u, v): 꼭짓점 4 + 변 가운데 4
    private static readonly double[,] RegionHandles =
    {
      { 0, 0 }, { 0.5, 0 }, { 1, 0 }, { 1, 0.5 }, { 1, 1 }, { 0.5, 1 }, { 0, 1 }, { 0, 0.5 }
    };

    private PointF RotateHandlePoint()
    {
      PointF tip = RegionPoint(_rL1, _rL2 / 2);
      PointF e1 = E1;
      float gap = RotateHandleGap / _zoom;
      return new PointF(tip.X + e1.X * gap, tip.Y + e1.Y * gap);
    }

    private void ResizeRegion(PointF image, double hu, double hv)
    {
      const double MinSize = 2;
      double u, v;
      RegionLocal(image, out u, out v);
      double shiftU = 0, shiftV = 0;

      if (hu == 1) _rL1 = Math.Max(MinSize, u);
      else if (hu == 0) { double left = Math.Min(u, _rL1 - MinSize); _rL1 -= left; shiftU = left; }

      if (hv == 1) _rL2 = Math.Max(MinSize, v);
      else if (hv == 0) { double top = Math.Min(v, _rL2 - MinSize); _rL2 -= top; shiftV = top; }

      // 왼쪽/위쪽 변을 끌었으면 원점이 따라 이동
      _rO = RegionPoint(shiftU, shiftV);
    }

    private void RotateRegion(PointF image)
    {
      PointF c = RegionPoint(_rL1 / 2, _rL2 / 2);
      double dx = image.X - c.X, dy = image.Y - c.Y;
      if (dx * dx + dy * dy < 1) return;
      SetRegionAngleAroundCenter(Math.Round(Math.Atan2(dy, dx) * 180 / Math.PI, 1));
    }

    private void SetRegionAngleAroundCenter(double phi)
    {
      PointF c = RegionPoint(_rL1 / 2, _rL2 / 2);
      _rPhi = phi;
      PointF e1 = E1, e2 = E2;
      _rO = new PointF((float)(c.X - _rL1 / 2 * e1.X - _rL2 / 2 * e2.X), (float)(c.Y - _rL1 / 2 * e1.Y - _rL2 / 2 * e2.Y));
    }

    #endregion

    #region 도형별 핸들 / 이동

    /// <summary>도형 외곽선 (화면 방향 이미지 좌표)</summary>
    private PointF[] Outline()
    {
      if (_shape is CvsCogRegion)
        return new[] { RegionPoint(0, 0), RegionPoint(_rL1, 0), RegionPoint(_rL1, _rL2), RegionPoint(0, _rL2) };
      if (_shape is CvsCogPolygon)
        return PolygonPoints();
      if (_shape is CvsCogLine)
      {
        var l = (CvsCogLine)_shape;
        return new[] { S2D(l.X0, l.Y0), S2D(l.X1, l.Y1) };
      }
      return new PointF[0];
    }

    private PointF[] PolygonPoints()
    {
      double[] p = ((CvsCogPolygon)_shape).Points ?? new double[0];
      var list = new PointF[p.Length / 2];
      for (int i = 0; i < list.Length; i++) list[i] = S2D(p[i * 2], p[i * 2 + 1]);
      return list;
    }

    /// <summary>크기/끝점 핸들 위치</summary>
    private PointF[] Handles()
    {
      if (_shape is CvsCogRegion)
      {
        var list = new PointF[8];
        for (int i = 0; i < 8; i++) list[i] = RegionPoint(RegionHandles[i, 0] * _rL1, RegionHandles[i, 1] * _rL2);
        return list;
      }
      if (_shape is CvsCogLine || _shape is CvsCogPolygon)
        return Outline();
      if (_shape is CvsCogCircle)
      {
        var c = (CvsCogCircle)_shape;
        PointF ctr = S2D(c.X, c.Y);
        return new[] { new PointF(ctr.X + (float)c.Radius, ctr.Y) };
      }
      if (_shape is CvsCogAnnulus)
      {
        var a = (CvsCogAnnulus)_shape;
        PointF ctr = S2D(a.X, a.Y);
        return new[] { new PointF(ctr.X + (float)a.InnerRadius, ctr.Y), new PointF(ctr.X + (float)a.OuterRadius, ctr.Y) };
      }
      return new PointF[0];
    }

    private bool NearScreen(PointF img, PointF target, float px)
    {
      PointF a = ToScreen(img), b = ToScreen(target);
      return Math.Abs(a.X - b.X) <= px && Math.Abs(a.Y - b.Y) <= px;
    }

    private DragKind HitTest(PointF img)
    {
      _dragHandle = -1;
      if (_shape == null) return DragKind.None;
      if (_shape is CvsCogRegion && NearScreen(img, RotateHandlePoint(), HandleSize))
        return DragKind.Rotate;

      PointF[] hs = Handles();
      for (int i = 0; i < hs.Length; i++)
        if (NearScreen(img, hs[i], HandleSize)) { _dragHandle = i; return DragKind.Handle; }

      float tol = 6 / _zoom;
      if (_shape is CvsCogRegion || _shape is CvsCogPolygon)
      {
        PointF[] o = Outline();
        if (o.Length >= 3)
          using (var path = new GraphicsPath())
          {
            path.AddPolygon(o);
            if (path.IsVisible(img)) return DragKind.Move;
          }
      }
      else if (_shape is CvsCogPoint)
      {
        var p = (CvsCogPoint)_shape;
        if (Dist(img, S2D(p.X, p.Y)) <= 14 / _zoom) return DragKind.Move;
      }
      else if (_shape is CvsCogLine)
      {
        PointF[] o = Outline();
        if (DistToSegment(img, o[0], o[1]) <= tol) return DragKind.Move;
      }
      else if (_shape is CvsCogCircle)
      {
        var c = (CvsCogCircle)_shape;
        if (Dist(img, S2D(c.X, c.Y)) <= c.Radius) return DragKind.Move;
      }
      else if (_shape is CvsCogAnnulus)
      {
        var a = (CvsCogAnnulus)_shape;
        if (Dist(img, S2D(a.X, a.Y)) <= Math.Max(a.InnerRadius, a.OuterRadius)) return DragKind.Move;
      }
      return DragKind.None;
    }

    private void MoveBy(float dx, float dy)
    {
      double sx = _xy ? dx : dy, sy = _xy ? dy : dx;   // 화면 이동량 → 센서 축
      if (_shape is CvsCogRegion) _rO = new PointF(_rO.X + dx, _rO.Y + dy);
      else if (_shape is CvsCogPoint) { var p = (CvsCogPoint)_shape; p.X += sx; p.Y += sy; }
      else if (_shape is CvsCogLine) { var l = (CvsCogLine)_shape; l.X0 += sx; l.Y0 += sy; l.X1 += sx; l.Y1 += sy; }
      else if (_shape is CvsCogCircle) { var c = (CvsCogCircle)_shape; c.X += sx; c.Y += sy; }
      else if (_shape is CvsCogAnnulus) { var a = (CvsCogAnnulus)_shape; a.X += sx; a.Y += sy; }
      else if (_shape is CvsCogPolygon)
      {
        double[] p = ((CvsCogPolygon)_shape).Points;
        for (int i = 0; i + 1 < p.Length; i += 2) { p[i] += sx; p[i + 1] += sy; }
      }
    }

    private void DragHandle(int index, PointF img)
    {
      double x, y;
      if (_shape is CvsCogRegion)
        ResizeRegion(img, RegionHandles[index, 0], RegionHandles[index, 1]);
      else if (_shape is CvsCogLine)
      {
        var l = (CvsCogLine)_shape;
        D2S(img, out x, out y);
        if (index == 0) { l.X0 = x; l.Y0 = y; } else { l.X1 = x; l.Y1 = y; }
      }
      else if (_shape is CvsCogPolygon)
      {
        double[] p = ((CvsCogPolygon)_shape).Points;
        D2S(img, out x, out y);
        p[index * 2] = x;
        p[index * 2 + 1] = y;
      }
      else if (_shape is CvsCogCircle)
      {
        var c = (CvsCogCircle)_shape;
        c.Radius = Math.Round(Math.Max(1, Dist(img, S2D(c.X, c.Y))), 2);
      }
      else if (_shape is CvsCogAnnulus)
      {
        var a = (CvsCogAnnulus)_shape;
        double r = Math.Round(Math.Max(1, Dist(img, S2D(a.X, a.Y))), 2);
        if (index == 0) a.InnerRadius = r; else a.OuterRadius = r;
      }
    }

    private static double Dist(PointF a, PointF b)
    {
      double dx = a.X - b.X, dy = a.Y - b.Y;
      return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double DistToSegment(PointF p, PointF a, PointF b)
    {
      double vx = b.X - a.X, vy = b.Y - a.Y, len2 = vx * vx + vy * vy;
      double t = len2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * vx + (p.Y - a.Y) * vy) / len2));
      return Dist(p, new PointF((float)(a.X + t * vx), (float)(a.Y + t * vy)));
    }

    #endregion

    #region 마우스

    private void View_MouseDown(object sender, MouseEventArgs e)
    {
      _view.Focus();
      if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle)
      {
        _drag = DragKind.Pan;
        _panStartMouse = e.Location;
        _panStart = _pan;
        return;
      }
      if (e.Button != MouseButtons.Left || _shape == null) return;

      PointF img = ToImage(e.Location);
      _drag = HitTest(img);
      _dragLastImage = img;
      if (_drag == DragKind.Handle) _activeHandle = _dragHandle;
      else if (_drag == DragKind.Move) _activeHandle = -1;

      // 점은 빈 곳을 클릭해도 그 위치로 옮김 (Suite와 같음)
      if (_drag == DragKind.None && _shape is CvsCogPoint)
      {
        var p = (CvsCogPoint)_shape;
        double x, y;
        D2S(img, out x, out y);
        p.X = x;
        p.Y = y;
        _drag = DragKind.Move;
        Changed(false);
      }
    }

    private void View_MouseMove(object sender, MouseEventArgs e)
    {
      PointF img = ToImage(e.Location);

      // 드래그 중에는 도형만 다시 그림 (숫자 칸/상태줄 갱신은 마우스 뗄 때) → 끊김 없음
      switch (_drag)
      {
        case DragKind.Pan:
          _pan = new PointF(_panStart.X + e.X - _panStartMouse.X, _panStart.Y + e.Y - _panStartMouse.Y);
          InvalidateView(true);
          return;
        case DragKind.Move:
          MoveBy(img.X - _dragLastImage.X, img.Y - _dragLastImage.Y);
          _dragLastImage = img;
          Changed(false);
          return;
        case DragKind.Handle:
          DragHandle(_dragHandle, img);
          Changed(false);
          return;
        case DragKind.Rotate:
          RotateRegion(img);
          Changed(false);
          return;
      }

      double sx, sy;
      D2S(img, out sx, out sy);
      _status.Text = string.Format(_xy ? "X {0:0.0}  Y {1:0.0}   · " : "Row {0:0.0}  Col {1:0.0}   · ", sx, sy) + HelpText;

      DragKind hover = HitTest(img);
      _view.Cursor = hover == DragKind.Move ? Cursors.SizeAll
                   : hover == DragKind.Handle ? Cursors.Cross
                   : hover == DragKind.Rotate ? Cursors.Hand
                   : Cursors.Default;
    }

    private void View_MouseUp(object sender, MouseEventArgs e)
    {
      bool edited = _drag == DragKind.Move || _drag == DragKind.Handle || _drag == DragKind.Rotate;
      _drag = DragKind.None;
      if (edited)
      {
        Changed(true);
        RaiseCommitted();
      }
      else InvalidateView(true);   // 팬 끝 → 고화질로 다시
    }

    private void View_MouseWheel(object sender, MouseEventArgs e)
    {
      float factor = e.Delta > 0 ? 1.2f : 1 / 1.2f;
      float zoom = Math.Max(0.05f, Math.Min(40f, _zoom * factor));
      PointF img = ToImage(e.Location);
      _zoom = zoom;
      _pan = new PointF(e.X - img.X * _zoom, e.Y - img.Y * _zoom);
      InvalidateView(true);
    }

    /// <param name="syncFields">false = 드래그 중 (숫자 칸 갱신 생략)</param>
    private void Changed(bool syncFields)
    {
      if (syncFields) SyncFieldsFromShape();
      _view.Invalidate();
    }

    #endregion

    #region 키보드 (1픽셀 미세 조정)

    /// <summary>
    /// 방향키 = 1px 이동 (Shift = 10px). 선택된 핸들이 있으면 그 핸들만, 없으면 도형 전체.
    /// Ctrl+방향키 = 크기 (←→ 가로/반지름, ↑↓ 세로), Q/E = 회전 1° (Shift 10°), Tab = 다음 핸들 선택
    /// 숫자 칸에 포커스가 있으면 방향키는 숫자 칸 몫
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
      if (_shape == null || ActiveControl is NumericUpDown || (ActiveControl != null && ActiveControl.Parent is NumericUpDown))
        return base.ProcessCmdKey(ref msg, keyData);

      Keys key = keyData & Keys.KeyCode;
      bool shift = (keyData & Keys.Shift) != 0, ctrl = (keyData & Keys.Control) != 0;
      float step = shift ? 10 : 1;
      float dx = key == Keys.Left ? -step : key == Keys.Right ? step : 0;
      float dy = key == Keys.Up ? -step : key == Keys.Down ? step : 0;

      if (dx != 0 || dy != 0)
      {
        if (ctrl) ResizeByKey(dx, dy);
        else if (_activeHandle >= 0 && _activeHandle < Handles().Length)
        {
          PointF h = Handles()[_activeHandle];
          DragHandle(_activeHandle, new PointF(h.X + dx, h.Y + dy));
        }
        else MoveBy(dx, dy);
        KeyEdited();
        return true;
      }

      if ((key == Keys.Q || key == Keys.E) && _shape is CvsCogRegion && !ctrl)
      {
        double deg = (shift ? 10 : 1) * (key == Keys.E ? 1 : -1);
        SetRegionAngleAroundCenter(Math.Round(_rPhi + deg, 1));
        KeyEdited();
        return true;
      }

      if (key == Keys.Tab && !ctrl)
      {
        int n = Handles().Length;
        if (n > 0)
        {
          // -1(전체) → 0 → 1 … → n-1 → -1
          _activeHandle = shift ? (_activeHandle < 0 ? n - 1 : _activeHandle - 1) : (_activeHandle + 1 >= n ? -1 : _activeHandle + 1);
          _view.Invalidate();
        }
        return true;
      }

      return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>연속으로 누르는 동안은 화면만, 멈추고 0.4초 뒤 1번 적용</summary>
    private void KeyEdited()
    {
      Changed(true);
      if (!LiveCommit) return;
      _nudgeTimer.Stop();
      _nudgeTimer.Start();
    }

    private void ResizeByKey(float dx, float dy)
    {
      if (_shape is CvsCogRegion)
      {
        _rL1 = Math.Max(2, _rL1 + dx);
        _rL2 = Math.Max(2, _rL2 + dy);
      }
      else if (_shape is CvsCogCircle)
      {
        var c = (CvsCogCircle)_shape;
        c.Radius = Math.Max(1, c.Radius + dx - dy);
      }
      else if (_shape is CvsCogAnnulus)
      {
        var a = (CvsCogAnnulus)_shape;
        a.OuterRadius = Math.Max(1, a.OuterRadius + dx);
        a.InnerRadius = Math.Max(1, a.InnerRadius - dy);
      }
    }

    #endregion

    #region 그리기

    private void View_Paint(object sender, PaintEventArgs e)
    {
      Graphics g = e.Graphics;
      if (_backDirty || _backBuffer == null || _backBuffer.Size != _view.ClientSize)
        RebuildBackBuffer();
      if (_backBuffer != null)
        g.DrawImageUnscaled(_backBuffer, 0, 0);

      // 연결 그래픽 (편집 셀을 참조하는 PlotString 등): 도형이 움직인 만큼 같이 이동해서 미리보기
      if (_linkedContext.Length > 0)
      {
        PointF a = Anchor();
        float offX = (a.X - _baselineAnchor.X) * _zoom, offY = (a.Y - _baselineAnchor.Y) * _zoom;
        var dc = new DisplayContext(_xy, _zoom, (int)Math.Round(_pan.X + offX), (int)Math.Round(_pan.Y + offY), _view.ClientRectangle);
        try { GraphicsHelper.DrawGraphics(g, _linkedContext, dc); }
        catch (Exception ex) { Debug.WriteLine("ShapeEditorControl linked: " + ex.Message); }
      }

      if (_shape == null) return;

      g.SmoothingMode = SmoothingMode.AntiAlias;
      Color main = Color.FromArgb(0, 230, 120);
      using (var pen = new Pen(main, 1.5f))
      {
        if (_shape is CvsCogRegion)
        {
          PointF[] o = Outline().Select(ToScreen).ToArray();
          g.DrawPolygon(pen, o);
          // 1축(가로) 방향 화살표
          using (var arrow = new Pen(Color.Yellow, 2f) { CustomEndCap = new AdjustableArrowCap(4, 5) })
            g.DrawLine(arrow, ToScreen(RegionPoint(_rL1 * 0.15, _rL2 / 2)), ToScreen(RegionPoint(_rL1 * 0.85, _rL2 / 2)));
          PointF rh = ToScreen(RotateHandlePoint());
          g.DrawLine(Pens.Yellow, ToScreen(RegionPoint(_rL1, _rL2 / 2)), rh);
          g.FillEllipse(Brushes.Yellow, rh.X - HandleSize / 2, rh.Y - HandleSize / 2, HandleSize, HandleSize);
        }
        else if (_shape is CvsCogPolygon)
        {
          PointF[] o = Outline().Select(ToScreen).ToArray();
          if (o.Length >= 2) g.DrawPolygon(pen, o);
        }
        else if (_shape is CvsCogLine)
        {
          PointF[] o = Outline().Select(ToScreen).ToArray();
          g.DrawLine(pen, o[0], o[1]);
        }
        else if (_shape is CvsCogPoint)
        {
          // 작은 십자 + 가운데 1px 점 (정확한 위치가 보이게 비워 둠)
          var p = (CvsCogPoint)_shape;
          PointF c = ToScreen(S2D(p.X, p.Y));
          g.SmoothingMode = SmoothingMode.None;
          g.DrawLine(pen, c.X - 9, c.Y, c.X - 3, c.Y);
          g.DrawLine(pen, c.X + 3, c.Y, c.X + 9, c.Y);
          g.DrawLine(pen, c.X, c.Y - 9, c.X, c.Y - 3);
          g.DrawLine(pen, c.X, c.Y + 3, c.X, c.Y + 9);
          g.FillRectangle(Brushes.Red, c.X - 0.5f, c.Y - 0.5f, Math.Max(1, _zoom), Math.Max(1, _zoom));
        }
        else if (_shape is CvsCogCircle)
        {
          var c = (CvsCogCircle)_shape;
          DrawCircle(g, pen, S2D(c.X, c.Y), c.Radius);
        }
        else if (_shape is CvsCogAnnulus)
        {
          var a = (CvsCogAnnulus)_shape;
          DrawCircle(g, pen, S2D(a.X, a.Y), a.InnerRadius);
          DrawCircle(g, pen, S2D(a.X, a.Y), a.OuterRadius);
        }
      }

      PointF[] hs = Handles();
      for (int i = 0; i < hs.Length; i++)
      {
        PointF s = ToScreen(hs[i]);
        var rect = new RectangleF(s.X - HandleSize / 2, s.Y - HandleSize / 2, HandleSize, HandleSize);
        g.FillRectangle(i == _activeHandle ? Brushes.OrangeRed : Brushes.White, rect);   // 방향키 대상 핸들 = 빨강
        g.DrawRectangle(Pens.Black, rect.X, rect.Y, rect.Width, rect.Height);
      }
    }

    /// <summary>이미지 + 고정 참고 그래픽을 화면 크기 비트맵에 미리 그림 (확대/이동/크기 변경 때만)</summary>
    private void RebuildBackBuffer()
    {
      _backDirty = false;
      Size size = _view.ClientSize;
      if (size.Width < 1 || size.Height < 1) return;
      if (_backBuffer == null || _backBuffer.Size != size)
      {
        _backBuffer?.Dispose();
        _backBuffer = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
      }

      using (Graphics g = Graphics.FromImage(_backBuffer))
      {
        g.Clear(_view.BackColor);
        if (_image != null)
        {
          // 화면 이동 중엔 빠른 보간, 멈추면 고화질
          g.InterpolationMode = _zoom >= 2 || _drag == DragKind.Pan ? InterpolationMode.NearestNeighbor : InterpolationMode.Bilinear;
          g.PixelOffsetMode = PixelOffsetMode.Half;
          g.DrawImage(_image, new RectangleF(_pan.X, _pan.Y, _image.Width * _zoom, _image.Height * _zoom));
        }
        if (_staticContext.Length > 0)
        {
          var dc = new DisplayContext(_xy, _zoom, (int)_pan.X, (int)_pan.Y, _view.ClientRectangle);
          try { GraphicsHelper.DrawGraphics(g, _staticContext, dc); }
          catch (Exception ex) { Debug.WriteLine("ShapeEditorControl context: " + ex.Message); }
        }
      }
    }

    private void DrawCircle(Graphics g, Pen pen, PointF center, double radius)
    {
      PointF c = ToScreen(center);
      float r = (float)radius * _zoom;
      if (r > 0) g.DrawEllipse(pen, c.X - r, c.Y - r, r * 2, r * 2);
      g.DrawLine(pen, c.X - 6, c.Y, c.X + 6, c.Y);
      g.DrawLine(pen, c.X, c.Y - 6, c.X, c.Y + 6);
    }

    #endregion

    #region 숫자 입력 칸

    private string[] FieldNames()
    {
      if (_shape is CvsCogRegion) return new[] { "X", "Y", "Width", "Height", "Angle" };
      if (_shape is CvsCogPoint) return new[] { "X", "Y" };
      if (_shape is CvsCogLine) return new[] { "X0", "Y0", "X1", "Y1" };
      if (_shape is CvsCogCircle) return new[] { "X", "Y", "Radius" };
      if (_shape is CvsCogAnnulus) return new[] { "X", "Y", "InnerRadius", "OuterRadius" };
      return new string[0];
    }

    private string Caption(string name)
    {
      if (_xy) return name;
      // 레거시 In-Sight: X = 행(Row), Y = 열(Col)
      if (name.StartsWith("X")) return "Row" + name.Substring(1);
      if (name.StartsWith("Y")) return "Col" + name.Substring(1);
      return name;
    }

    private void BuildFields()
    {
      _fields.SuspendLayout();
      foreach (Control c in _fields.Controls.Cast<Control>().ToList()) c.Dispose();
      _fields.Controls.Clear();
      _numbers.Clear();

      foreach (string name in FieldNames())
      {
        var label = new Label { Text = Caption(name), AutoSize = true, Margin = new Padding(8, 8, 2, 0) };
        var num = new NumericUpDown { Width = 80, DecimalPlaces = 2, Minimum = -100000, Maximum = 100000, Margin = new Padding(0, 4, 4, 0) };
        string n = name;
        num.ValueChanged += (s, e) =>
        {
          if (_syncingFields) return;
          SetValue(n, (double)num.Value);
          _view.Invalidate();
          if (LiveCommit) { _nudgeTimer.Stop(); _nudgeTimer.Start(); }
        };
        _fields.Controls.Add(label);
        _fields.Controls.Add(num);
        _numbers.Add(new KeyValuePair<string, NumericUpDown>(name, num));
      }
      _fields.ResumeLayout();
      SyncFieldsFromShape();
    }

    private void SyncFieldsFromShape()
    {
      if (_numbers.Count == 0 || _shape == null) return;
      SaveRegion();   // Region은 내부 표현 → 센서 값으로 맞춰서 표시
      _syncingFields = true;
      try
      {
        foreach (var kv in _numbers)
        {
          decimal v = (decimal)Math.Round(GetValue(kv.Key), 2);
          kv.Value.Value = Math.Max(kv.Value.Minimum, Math.Min(kv.Value.Maximum, v));
        }
      }
      finally { _syncingFields = false; }
    }

    private double GetValue(string name)
    {
      var prop = _shape.GetType().GetProperty(name);
      return prop == null ? 0 : Convert.ToDouble(prop.GetValue(_shape));
    }

    private void SetValue(string name, double value)
    {
      var prop = _shape.GetType().GetProperty(name);
      if (prop == null || !prop.CanWrite) return;
      SaveRegion();
      prop.SetValue(_shape, value);
      LoadRegion();
    }

    #endregion

    /// <summary>확대/이동 깜빡임 방지</summary>
    private class ImagePanel : Panel
    {
      public ImagePanel()
      {
        DoubleBuffered = true;
        ResizeRedraw = true;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
      }
    }
  }
}
