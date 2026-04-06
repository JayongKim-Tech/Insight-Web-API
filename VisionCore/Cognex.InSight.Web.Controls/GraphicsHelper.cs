// Copyright (c) 2022 Cognex Corporation. All Rights Reserved*

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using Cognex.Extensions;
using Cognex.InSight.Remoting.Serialization;

namespace Cognex.InSight.Web.Controls
{
  public abstract class GraphicsHelper
  {
    public static Font DefaultFont = new Font("Arial", 12);
    public static SolidBrush DefaultBrush = new SolidBrush(Color.Blue);

    public static void DrawGraphics(Graphics gr, CvsCogShape [] graphics, ICvsDisplayContext dc)
    {
      if (graphics?.Length > 0)
      {
        // Render each graphic that is part the result...
        foreach (var graphic in graphics)
        {
          if (graphic is CvsCogRegion)
          {
            CvsCogRegion region = graphic as CvsCogRegion;
            DrawRegion(gr, region, dc);
          }
          else if (graphic is CvsCogBlobChain)
          {
            CvsCogBlobChain blobChain = graphic as CvsCogBlobChain;
            DrawBlobChain(gr, blobChain, dc);
          }
          else if (graphic is CvsCogCircle)
          {
            CvsCogCircle circle = graphic as CvsCogCircle;
            DrawCircle(gr, circle, dc);
          }
          else if (graphic is CvsCogAnnulus)
          {
            CvsCogAnnulus ann = graphic as CvsCogAnnulus;
            DrawAnnulus(gr, ann, dc);
          }
          else if (graphic is CvsCogPolygon)
          {
            CvsCogPolygon polygon = graphic as CvsCogPolygon;
            DrawPolygon(gr, polygon, dc);
          }
          else if (graphic is CvsCogText)
          {
            CvsCogText text = graphic as CvsCogText;
            DrawText(gr, text, dc, text.Color, text.BackgroundColor);
          }
          else if (graphic is CvsCogLine)
          {
            CvsCogLine line = graphic as CvsCogLine;
            DrawLine(gr, line, dc);
          }
          else if (graphic is CvsCogLineList)
          {
            CvsCogLineList lineList = graphic as CvsCogLineList;
            DrawLineList(gr, lineList, dc);
          }
          else if (graphic is CvsCogFixture)
          {
            CvsCogFixture fixture = graphic as CvsCogFixture;
            DrawFixture(gr, fixture, dc);
          }
          else if (graphic is CvsCogPoint)
          {
            CvsCogPoint point = graphic as CvsCogPoint;
            DrawPoint(gr, point, dc);
          }
          else if (graphic is CvsCogCompositeRegion)
          {
            CvsCogCompositeRegion cr = graphic as CvsCogCompositeRegion;
            DrawCompositeRegion(gr, cr, dc);
          }
          else if (graphic is CvsCogFilledBox)
          {
            CvsCogFilledBox box = graphic as CvsCogFilledBox;
            DrawFilledBox(gr, box, dc);
          }
        }
      }
    }

    public static void DrawCompositeRegion(Graphics gr, CvsCogCompositeRegion cr, ICvsDisplayContext dc)
    {
      Pen pen = new Pen(Color.FromArgb(cr.Color), cr.LineThickness);

      foreach (CvsCogSubRegion subRegion in cr.SubRegions)
      {
        DrawSubRegion(gr, subRegion, dc);
      }

      // -----------------------------------------------------
      // Draw the bounding box and hash out invalid area
      // if there is more than 1 subregion.
      // -----------------------------------------------------
      HashInvalidArea(gr, cr, dc);

      //PointF pt = dc.SensorToClient(new PointF((float)cr.X, (float)cr.Y));
      //PaintLabel(gr, dc, pt, cr.Label);
    }

    private static RectangleF CalcBoundingBox(CvsCogSubRegion sr, ICvsDisplayContext dc)
    {
      RectangleF box = RectangleF.Empty;

      if (sr.Shape is CvsCogRegion)
      {
        RegionDimensions regionDimensions = new RegionDimensions(sr.Shape as CvsCogRegion, dc.UsesXYCoordinates);
        return regionDimensions.Bounds;
      }
      else if (sr.Shape is CvsCogCircle)
      {
        CvsCogCircle circle = sr.Shape as CvsCogCircle;
        return new RectangleF((float)circle.X - (float)circle.Radius,
                                      (float)circle.Y - (float)circle.Radius,
                                      ((float)circle.Radius * 2) + 1,
                                      ((float)circle.Radius * 2) + 1);
      }
      else if (sr.Shape is CvsCogAnnulus)
      {
        CvsCogAnnulus ann = sr.Shape as CvsCogAnnulus;
        float radius = Math.Max((float)ann.InnerRadius, (float)ann.OuterRadius);
        return new RectangleF((float)ann.X - radius,
                              (float)ann.Y - radius,
                              radius * 2,
                              radius * 2);
      }
      else if (sr.Shape is CvsCogPolygon)
      {
        CvsCogPolygon polygon = sr.Shape as CvsCogPolygon;
        if (polygon.Length < 1)
          return System.Drawing.RectangleF.Empty;

        double[] pts = polygon.Points;
        float MinX = (float)pts[0];
        float MinY = (float)pts[1];
        float MaxX = (float)pts[0];
        float MaxY = (float)pts[1];

        for (int i = 2; i < pts.Length; i += 2)
        {
          MinX = Math.Min(MinX, (float)pts[i]);
          MaxX = Math.Max(MaxX, (float)pts[i]);
          MinY = Math.Min(MinY, (float)pts[i + 1]);
          MaxY = Math.Max(MaxY, (float)pts[i + 1]);
        }

        return new System.Drawing.RectangleF(MinX, MinY, MaxX - MinX + 1, MaxY - MinY + 1);
      }

      return box;
    }

    /// <summary>
    /// Returns the bounding box of all the subregions.
    /// </summary>
    private static RectangleF CalcBoundingBox(CvsCogSubRegion[] subregions, ICvsDisplayContext dc)
    {
      RectangleF box = RectangleF.Empty;

      if (subregions != null && subregions.Length > 0)
      {
        box = CalcBoundingBox(subregions[0], dc);

        for (int i = 1; i < subregions.Length; i++)
        {
          RectangleF tmp = CalcBoundingBox(subregions[i], dc);
          box = RectangleF.Union(tmp, box);
        }
      }

      return box;
    }

    public static void HashInvalidArea(Graphics gr, CvsCogCompositeRegion cr, ICvsDisplayContext dc)
    {
      CvsCogSubRegion[] sr = cr.SubRegions;
      if (sr == null || sr.Length == 0)
        return;

      // if only one 'Add' graphic, don't hash anything, and don't draw the
      // bounding box.
      if (sr.Length == 1)
      {
        if (sr[0].Add)
          return;
      }

      Region invalidRegion = null;

      try
      {
        Color clr = Color.FromArgb(cr.Color);

        if (sr.Length == 1)
        {
          // this single subregion must be subtractive - draw the hash marks
          // within it; do not hash out the entire bounding box
          // TODO
          //invalidRegion = sr[0].CreateRegion(context);
        }
        else
        {
          // hash out the invalid regions of the bounding box
          RectangleF box = dc.SensorToClient(CalcBoundingBox(cr.SubRegions, dc));

          // create the invalid region
          using (Region validRegion = CreateRegion(gr, cr, dc))
          {
            invalidRegion = new Region(box);
            invalidRegion.Exclude(validRegion);
          }

          // draw the bounding box
          DrawBoundingBox(gr, dc, box);
        }

        if (invalidRegion != null)
        {
          using (Brush brush = new HatchBrush(HatchStyle.BackwardDiagonal, clr, Color.Transparent))
          {
            gr.FillRegion(brush, invalidRegion);
          }
        }
      }
      finally
      {
        if (invalidRegion != null)
          invalidRegion.Dispose();
      }
    }

    /// <summary>
    /// Drawing the dashed lines can be slow if the box is very large.
    /// Clip the bounding rectangle to the size of the image.
    /// </summary>
    private static void DrawBoundingBox(Graphics gr, ICvsDisplayContext dc, RectangleF clientBox)
    {
      Color clr = Color.Yellow; // Just use yellow for now
      Pen pen = new Pen(clr, 1);
      pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;

      Point TL = GraphicMath.RoundPoint(clientBox.Left, clientBox.Top);
      Point BR = GraphicMath.RoundPoint(clientBox.Right, clientBox.Bottom);

      int MaxX = dc.DisplayBounds.Right;
      int MaxY = dc.DisplayBounds.Bottom;

      // top edge
      if (0 <= TL.Y && TL.Y <= MaxY)
      {
        int L = Math.Max(TL.X, 0);
        int R = Math.Min(BR.X, MaxX);
        gr.DrawLine(pen, L, TL.Y, R, TL.Y);
      }
      // bottom edge
      if (0 <= BR.Y && BR.Y <= MaxY)
      {
        int L = Math.Max(TL.X, 0);
        int R = Math.Min(BR.X, MaxX);
        gr.DrawLine(pen, L, BR.Y, R, BR.Y);
      }
      // left edge
      if (0 <= TL.X && TL.X <= MaxX)
      {
        int T = Math.Max(TL.Y, 0);
        int B = Math.Min(BR.Y, MaxY);
        gr.DrawLine(pen, TL.X, T, TL.X, B);
      }
      // right edge
      if (0 <= BR.X && BR.X <= MaxX)
      {
        int T = Math.Max(TL.Y, 0);
        int B = Math.Min(BR.Y, MaxY);
        gr.DrawLine(pen, BR.X, T, BR.X, B);
      }
    }

    /// <summary>
    /// Create a .NET Region object which describes the region graphic.
    /// </summary>
    private static System.Drawing.Region CreateRegionRegion(CvsCogRegion region, ICvsDisplayContext dc)
    {
      RegionDimensions regionDimensions = new RegionDimensions(region, dc.UsesXYCoordinates);
      return regionDimensions.CreateRegion(dc);
    }

    /// <summary>
    /// Create a .NET Region object which describes the polygon graphic.
    /// </summary>
    private static System.Drawing.Region CreatePolygonRegion(CvsCogPolygon polygon, ICvsDisplayContext dc)
    {
      int nPts = polygon.Length;
      if (nPts < 3)
        return null;

      PointF[] pts = new PointF[nPts];
      for (int i = 0; i < polygon.Points.Length; i += 2)
      {
        pts[i / 2] = dc.SensorToClient(new PointF((float)polygon.Points[i], (float)polygon.Points[i + 1]));
      }

      System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();
      path.AddPolygon(pts);
      return new System.Drawing.Region(path);
    }

    /// <summary>
    /// Create a .NET Region object which describes the circle graphic.
    /// </summary>
    private static System.Drawing.Region CreateCircleRegion(CvsCogCircle circle, ICvsDisplayContext dc)
    {
      PointF center = dc.SensorToClient(new PointF((float)circle.X, (float)circle.Y));
      float radius = (float)(circle.Radius * dc.ImageScale);

      System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();
      path.AddEllipse(center.X - radius, center.Y - radius, radius * 2, radius * 2);
      return new System.Drawing.Region(path);
    }

    /// <summary>
    /// Create a .NET Region object which describes the annulus graphic.
    /// </summary>
    private static System.Drawing.Region CreateAnnulusRegion(CvsCogAnnulus ann, ICvsDisplayContext dc)
    {
      float inner = (float)(ann.InnerRadius * dc.ImageScale);
      float outer = (float)(ann.OuterRadius * dc.ImageScale);
      PointF center = dc.SensorToClient(new PointF((float)ann.X, (float)ann.Y));

      if (inner > outer)
      {
        float tmp = inner;
        inner = outer;
        outer = tmp;
      }

      System.Drawing.Drawing2D.GraphicsPath innerPath,
                                            outerPath;
      innerPath = new System.Drawing.Drawing2D.GraphicsPath();
      outerPath = new System.Drawing.Drawing2D.GraphicsPath();

      innerPath.AddEllipse(center.X - inner, center.Y - inner, inner * 2, inner * 2);
      outerPath.AddEllipse(center.X - outer, center.Y - outer, outer * 2, outer * 2);

      Region innerRegion = new Region(innerPath);
      Region outerRegion = new Region(outerPath);

      // remove the inner portion from the region
      outerRegion.Exclude(innerRegion);
      return outerRegion;
    }

    /// <summary>
    /// Create a .NET Region object which describes the composite region graphic.
    /// </summary>
    private static Region CreateRegion(Graphics gr, CvsCogCompositeRegion cr, ICvsDisplayContext dc)
    {
      Region tmpRegion = null;
      Region validRegion = new Region();
      validRegion.MakeEmpty();

      for (int i = 0; i < cr.SubRegions.Length; i++)
      {
        CvsCogSubRegion sr = cr.SubRegions[i];
        if (sr.Shape is CvsCogRegion)
        {
          tmpRegion = CreateRegionRegion(sr.Shape as CvsCogRegion, dc);
        }
        else if (sr.Shape is CvsCogCircle)
        {
          tmpRegion = CreateCircleRegion(sr.Shape as CvsCogCircle, dc);
        }
        else if (sr.Shape is CvsCogAnnulus)
        {
          tmpRegion = CreateAnnulusRegion(sr.Shape as CvsCogAnnulus, dc);
        }
        else if (sr.Shape is CvsCogPolygon)
        {
          tmpRegion = CreatePolygonRegion(sr.Shape as CvsCogPolygon, dc);
        }

        if (tmpRegion == null)
          continue;

        if (sr.Add)
        {
          validRegion = UnionRegions(gr, validRegion, tmpRegion); // Region.Union(..) is buggy.
        }
        else
        {
          validRegion.Exclude(tmpRegion);
        }
      }
      return validRegion;
    }

    private static Region UnionRegions(Graphics graphics, Region target, Region region2)
    {
      IntPtr hRgn2 = region2.GetHrgn(graphics);
      IntPtr hRgnTarget = target.GetHrgn(graphics);

      NativeMethods.CombineRgn(hRgnTarget, hRgnTarget, hRgn2, (int)NativeMethods.COMBINERGN_MODE.RGN_OR);
      Region region = Region.FromHrgn(hRgnTarget);

      NativeMethods.DeleteObject(hRgn2);
      NativeMethods.DeleteObject(hRgnTarget);

      return region;
    }

    public static void DrawSubRegion(Graphics gr, CvsCogSubRegion sr, ICvsDisplayContext dc)
    {
      if (sr.Shape is CvsCogRegion)
      {
        DrawRegion(gr, sr.Shape as CvsCogRegion, dc);
      }
      else if (sr.Shape is CvsCogCircle)
      {
        DrawCircle(gr, sr.Shape as CvsCogCircle, dc);
      }
      else if (sr.Shape is CvsCogAnnulus)
      {
        DrawAnnulus(gr, sr.Shape as CvsCogAnnulus, dc);
      }
      else if (sr.Shape is CvsCogPolygon)
      {
        DrawPolygon(gr, sr.Shape as CvsCogPolygon, dc);
      }
    }

    public static void DrawPoint(Graphics gr, CvsCogPoint point, ICvsDisplayContext dc)
    {
      const int IS_POINT_GRAPH_SIZE = 10;
      const int IS_POINT_GRAPH_DIST = 2;

      PointF pt = dc.SensorToClient(new PointF((float)point.X, (float)point.Y));

      Pen pen = new Pen(Color.FromArgb(point.Color), point.LineThickness);

      gr.DrawLine(pen, pt.X, pt.Y - IS_POINT_GRAPH_SIZE, pt.X, pt.Y - IS_POINT_GRAPH_DIST);
      gr.DrawLine(pen, pt.X, pt.Y - IS_POINT_GRAPH_SIZE, pt.X, pt.Y - IS_POINT_GRAPH_DIST);
      gr.DrawLine(pen, pt.X, pt.Y + IS_POINT_GRAPH_DIST, pt.X, pt.Y + IS_POINT_GRAPH_SIZE);
      gr.DrawLine(pen, pt.X - IS_POINT_GRAPH_SIZE, pt.Y, pt.X - IS_POINT_GRAPH_DIST, pt.Y);
      gr.DrawLine(pen, pt.X + IS_POINT_GRAPH_DIST, pt.Y, pt.X + IS_POINT_GRAPH_SIZE, pt.Y);

      float x = (float)pt.X - IS_POINT_GRAPH_SIZE;
      float y = (float)pt.Y - IS_POINT_GRAPH_SIZE;
      float width = 2 * IS_POINT_GRAPH_SIZE;
      float height = 2 * IS_POINT_GRAPH_SIZE;
      gr.DrawArc(pen, x, y, width, height, 0, 360);

      PaintLabel(gr, dc, pt, point.Label, point.Color, StringAlignment.Center, StringAlignment.Far);
    }

    public static void DrawFixture(Graphics gr, CvsCogFixture fixture, ICvsDisplayContext dc)
    {
      Color color = Color.FromArgb(fixture.Color);
      Pen pen = new Pen(color, fixture.LineThickness);

      PointF[] points = GetFixturePoints(fixture, 1.0 / dc.ImageScale);
      Point[] imagePoints = new Point[4];

      for (int i = 0; i < 4; i++)
      {
        imagePoints[i] = GraphicMath.RoundPoint(dc.SensorToClient(points[i]));
      }

      // Draw the cross lines
      int edgeLength = 4 + fixture.LineThickness;
      gr.DrawLine(pen, imagePoints[0].X, imagePoints[0].Y, imagePoints[1].X, imagePoints[1].Y);
      DrawArrow(gr, color, imagePoints[1].X, imagePoints[1].Y, dc.UsesXYCoordinates ? (float)fixture.Angle : 90 - (float)fixture.Angle, edgeLength);

      gr.DrawLine(pen, imagePoints[2].X, imagePoints[2].Y, imagePoints[3].X, imagePoints[3].Y);
      DrawArrow(gr, color, imagePoints[3].X, imagePoints[3].Y, dc.UsesXYCoordinates ? (float)fixture.Angle + 90: - (float)fixture.Angle, edgeLength);


      PointF pt = dc.SensorToClient(new PointF((float)fixture.X, (float)fixture.Y));
      PaintLabel(gr, dc, pt, fixture.Label, fixture.Color, StringAlignment.Near, StringAlignment.Far);
    }

    private static PointF[] GetFixturePoints(CvsCogFixture fixture, double scale)
    {
      const int FIXTURE_GRAPH_SIZE = 15;
      PointF[] points = new PointF[4];

      double radians = fixture.Angle * Math.PI / 180.0;
      float sin = (float)(FIXTURE_GRAPH_SIZE * scale * Math.Sin(radians));
      float cos = (float)(FIXTURE_GRAPH_SIZE * scale * Math.Cos(radians));

      points[0] = new PointF((float)fixture.X - cos, (float)fixture.Y - sin);
      points[1] = new PointF((float)fixture.X + cos, (float)fixture.Y + sin);
      points[2] = new PointF((float)fixture.X + sin, (float)fixture.Y - cos);
      points[3] = new PointF((float)fixture.X - sin, (float)fixture.Y + cos);

      return points;
    }

    public static void DrawLine(Graphics gr, CvsCogLine line, ICvsDisplayContext dc)
    {
      Pen pen = new Pen(Color.FromArgb(line.Color), line.LineThickness);

      PointF pt0 = dc.SensorToClient(new PointF((float)line.X0, (float)line.Y0));
      PointF pt1 = dc.SensorToClient(new PointF((float)line.X1, (float)line.Y1));

      gr.DrawLine(pen, pt0, pt1);

      PointF pt = pt0.Y > pt1.Y ? pt1 : pt0;
      PaintLabel(gr, dc, pt, line.Label, line.Color, StringAlignment.Near, StringAlignment.Far);
    }

    public static void DrawLineList(Graphics gr, CvsCogLineList lineList, ICvsDisplayContext dc)
    {
      if (lineList == null)
      {
        return;
      }

      Pen pen = new Pen(Color.FromArgb(lineList.Color), lineList.LineThickness);

      double[] lines = lineList.Lines;
      for (int i = 0; i < lines.Length-3; i += 4)
      {
        PointF pt0 = dc.SensorToClient(new PointF((float)lines[i], (float)lines[i+1]));
        PointF pt1 = dc.SensorToClient(new PointF((float)lines[i+2], (float)lines[i+3]));
        PointF pt0_extended;
        PointF pt1_extended;
        ExtendLine(pt0, pt1, 1, out pt0_extended, out pt1_extended);

        gr.DrawLine(pen, pt0_extended, pt1_extended);
      }
    }

    public static void ExtendLine(PointF p1, PointF p2, double distance, out PointF start, out PointF end)
    {
      System.Windows.Vector direction = new System.Windows.Vector(p2.X - p1.X, p2.Y - p1.Y);
      direction.Normalize();
      System.Windows.Vector offset = (direction * distance);

      // Subtract the offset from the first point to find the new first point
      start = new PointF((float)(p1.X - offset.X), (float)(p1.Y - offset.Y));

      // Add the offset to the second point to find the new second point
      end = new PointF((float)(p2.X + offset.X), (float)(p2.Y + offset.Y));
    }

    // This function checks the room size and your text and appropriate font
    //  for your text to fit in room
    // PreferedFont is the Font that you wish to apply
    // Room is your space in which your text should be in.
    // LongString is the string which it's bounds is more than room bounds.
    private static Font FindFont(
      System.Drawing.Graphics g,
      string longString,
      Size Room,
      Font PreferedFont
      )
    {
      // you should perform some scale functions!!!
      SizeF RealSize = g.MeasureString(longString, PreferedFont);
            //float HeightScaleRatio = Room.Height / RealSize.Height;
            //float WidthScaleRatio = Room.Width / RealSize.Width;
            float HeightScaleRatio = 1;
            float WidthScaleRatio = 1;

            float ScaleRatio = (HeightScaleRatio < WidthScaleRatio)
         ? ScaleRatio = HeightScaleRatio
         : ScaleRatio = WidthScaleRatio;

      float ScaleFontSize = PreferedFont.Size * ScaleRatio;

      return new Font(PreferedFont.FontFamily, Math.Max(1,ScaleFontSize));
    }

    public static void DrawText(Graphics gr, CvsCogText text, ICvsDisplayContext dc, int color, int bgColor)
    {
      Pen pen = new Pen(Color.FromArgb(color), text.LineThickness);
      SolidBrush brush = new SolidBrush(Color.FromArgb(color));

      PointF loc = dc.SensorToClient(new PointF((float)text.X, (float)text.Y));
      Font font = new Font((text.Font.Length > 0)?text.Font:"Arial", (text.FontSize > 0) ? text.FontSize : 9);

      string label = text.Text;
      if (label.Length <= 0)
        return;

      SizeF strSize = gr.MeasureString(label, font);

      Size sz = new Size((int)(strSize.Width * dc.ImageScale), (int)(strSize.Height  * dc.ImageScale));
      Font goodFont = FindFont(gr, label, sz, font);

      if (bgColor != 0)
      {
        SolidBrush bgBrush = new SolidBrush(Color.FromArgb(bgColor));
        SizeF size = gr.MeasureString(label, (goodFont != null) ? goodFont : DefaultFont);
        gr.FillRectangle(bgBrush, new RectangleF(new PointF(loc.X, loc.Y), size));
      }

      gr.DrawString(label, (goodFont != null) ? goodFont : DefaultFont, brush, (int)loc.X, (int)loc.Y);
    }

    public static void DrawPolygon(Graphics gr, CvsCogPolygon polygon, ICvsDisplayContext dc)
    {
      Pen pen = new Pen(Color.FromArgb(polygon.Color), polygon.LineThickness);

      // enumerate through the points
      double[] pts = polygon.Points;
      float x = (float)polygon.Points[0];
      float y = (float)polygon.Points[1];
      PointF polygonPt = new PointF(x, y);
      PointF clientTop = dc.SensorToClient(polygonPt);
      int nPts = pts.Length;
      PointF[] points = new PointF[nPts / 2];
      PointF pt;
      for (int i = 0; i < nPts; i += 2)
      {
        pt = new PointF((float)pts[i], (float)pts[i + 1]);

        // Get the corner point in client coordinates
        PointF clientPt = dc.SensorToClient(pt);

        points[i / 2] = clientPt;
      }

      gr.DrawPolygon(pen, points);

      PaintLabel(gr, dc, polygonPt, polygon.Label, polygon.Color, StringAlignment.Near, StringAlignment.Far);
    }

    public static void DrawAnnulus(Graphics gr, CvsCogAnnulus ann, ICvsDisplayContext dc)
    {
      Pen pen = new Pen(Color.FromArgb(ann.Color), ann.LineThickness);

      float innerRadius = (float)ann.InnerRadius;
      float x = (float)ann.X - innerRadius;
      float y = (float)ann.Y - innerRadius;
      float width = 2 * innerRadius;
      float height = 2 * innerRadius;
      PointF loc = dc.SensorToClient(new PointF(x, y));
      gr.DrawArc(pen, loc.X, loc.Y, width * (float)dc.ImageScale, height * (float)dc.ImageScale, 0, 360);

      float outerRadius = (float)ann.OuterRadius;
      x = (float)ann.X - outerRadius;
      y = (float)ann.Y - outerRadius;
      width = 2 * outerRadius;
      height = 2 * outerRadius;
      loc = dc.SensorToClient(new PointF(x, y));
      gr.DrawArc(pen, loc.X, loc.Y, width * (float)dc.ImageScale, height * (float)dc.ImageScale, 0, 360);
    }

    public static void DrawCircle(Graphics gr, CvsCogCircle circle, ICvsDisplayContext dc)
    {
      Pen pen = new Pen(Color.FromArgb(circle.Color), circle.LineThickness);

      float radius = (float)circle.Radius;
      if (radius <= 0)
        return;

      float x = (float)circle.X - radius;
      float y = (float)circle.Y - radius;
      float width = 2 * radius;
      float height = 2 * radius;

      PointF center = dc.SensorToClient(new PointF(x, y));

      gr.DrawArc(pen, center.X, center.Y, width * (float)dc.ImageScale, height * (float)dc.ImageScale, 0, 360);

      if (dc.UsesXYCoordinates)
      {
        PointF pt = dc.SensorToClient(new PointF((float)circle.X, (float)circle.Y - radius));
        PaintLabel(gr, dc, pt, circle.Label, circle.Color, StringAlignment.Center, StringAlignment.Far);
      }
      else
      {
        PointF pt = dc.SensorToClient(new PointF((float)circle.X - radius, (float)circle.Y));
        PaintLabel(gr, dc, pt, circle.Label, circle.Color, StringAlignment.Far, StringAlignment.Center);
      }
    }

    public static void DrawBlobChain(Graphics gr, CvsCogBlobChain blobChain, ICvsDisplayContext dc)
    {
      Pen pen = new Pen(Color.FromArgb(blobChain.Color), blobChain.LineThickness);

      // enumerate through the points
      double [] pts = blobChain.Points;
      float x = (float)blobChain.X;
      float y = (float)blobChain.Y;
      PointF chainPt = new PointF(x, y);
      PointF clientTop = dc.SensorToClient(chainPt);
      int nPts = pts.Length;
      PointF[] points = new PointF[nPts / 2];
      PointF pt;
      for (int i = 0; i < nPts; i += 2)
      {
        pt = new PointF((float)pts[i], (float)pts[i + 1]);

        // Get the corner point in client coordinates
        PointF clientPt = dc.SensorToClient(pt);

        points[i / 2] = clientPt;
      }

      gr.DrawPolygon(pen, points);

      string chainNumber = blobChain.Index.ToString("d", CultureInfo.CurrentCulture);
      PaintLabel(gr, dc, clientTop, chainNumber, blobChain.Color, StringAlignment.Center, StringAlignment.Far);
    }

    private static void PaintLabel(Graphics gr, ICvsDisplayContext dc, PointF p, string label, int color, StringAlignment hAlign, StringAlignment vAlign, Font font = null)
    {
      if ((label == null) || (label.Length <= 0))
        return;

      if (font == null)
        font = DefaultFont;

      Color clr = Color.FromArgb(color);
      Brush brush = new SolidBrush(clr);

      SizeF strSize = gr.MeasureString(label, font);

      StringFormat sf = new StringFormat();
      sf.Alignment = hAlign;
      sf.LineAlignment = vAlign;

      gr.DrawString(label, font, brush, (int)(p.X), (int)(p.Y), sf);
    }

    public static void DrawRegion(Graphics gr, CvsCogRegion region, ICvsDisplayContext dc)
    {
      RegionDimensions regionDimensions = new RegionDimensions(region, dc.UsesXYCoordinates);

      // if the region is bent, draw a bent rectangle
      if (regionDimensions.IsBent)
      {
        PaintBentRectangle(gr, regionDimensions, dc, region.LineThickness, region.Color);
      }
      // if the region is not very bent, draw a straight rectangle
      else
      {
        PaintRectangle(gr, regionDimensions, dc, region.LineThickness, region.Color);
        PointF pt = dc.SensorToClient(new PointF((float)region.X, (float)region.Y));
        PaintLabel(gr, dc, pt, region.Label, region.Color, StringAlignment.Near, StringAlignment.Far);
      }

      Rectangle[] axisBounds = PaintAxisLabels(gr, regionDimensions, dc, region.LineThickness, false, region.Color,
                                               region.ShowAxesLabels, region.ShowXArrow, region.ShowYArrow);
    }

    public static void DrawFilledBox(Graphics gr, CvsCogFilledBox box, ICvsDisplayContext dc)
    {
      CvsCogRegion region = new CvsCogRegion(box.X, box.Y, box.Width, box.Height, 0, 0);
      RegionDimensions regionDimensions = new RegionDimensions(region, dc.UsesXYCoordinates);
      Point[] cornerPoints = regionDimensions.GetCornerClientPoints(dc);
      float width = cornerPoints[1].X - cornerPoints[0].X;
      float height = cornerPoints[2].Y - cornerPoints[0].Y;
      gr.FillRectangle(new SolidBrush(Color.FromArgb(box.Color)), cornerPoints[0].X, cornerPoints[0].Y, width, height);
      PointF pt = new PointF(cornerPoints[0].X, cornerPoints[0].Y);
      PaintLabel(gr, dc, pt, box.Label, box.Color, StringAlignment.Center, StringAlignment.Far);
    }

    private static void PaintRectangle(Graphics gr, RegionDimensions regionDimensions, ICvsDisplayContext context, int lineWidth, int color)
    {
      Point[] cornerPoints = regionDimensions.GetCornerClientPoints(context);

      Pen pen = new Pen(Color.FromArgb(color), lineWidth);

      // make sure the origin point is drawn
      gr.DrawArc(pen, cornerPoints[CvsRegionIndex.Origin].X, cornerPoints[CvsRegionIndex.Origin].Y, 1, 1, 0, 360);

      // Drawing starts at the top/left of the rectangle. Because of this, drawing a
      // full width/height rectangle will mean that the drawn rect is actually one
      // pixel too large. In order to fix this, subtract a single pixel from the X &
      // Y values of the right and bottom edges.
      int topRightIndex = CvsRegionIndex.TopRight;
      int bottomRightIndex = CvsRegionIndex.BottomRight;
      int bottomLeftIndex = CvsRegionIndex.BottomLeft;

      cornerPoints[topRightIndex].X -= 1;
      cornerPoints[bottomRightIndex].X -= 1;
      cornerPoints[bottomLeftIndex].Y -= 1;
      cornerPoints[bottomRightIndex].Y -= 1;

      // draw the region
      for (int i = 0; i < 4; ++i)
      {
        gr.DrawLine(pen,
                        (float)cornerPoints[i].X,
                        (float)cornerPoints[i].Y,
                        (float)cornerPoints[(i + 1) % 4].X,
                        (float)cornerPoints[(i + 1) % 4].Y);
      }
    }

    public static PointF SwapCoordinates(PointF pt)
    {
      float tmp = pt.X;
      pt.X = pt.Y;
      pt.Y = tmp;
      return pt;
    }

    private static void PaintBentRectangle(Graphics gr, RegionDimensions regionDimensions, ICvsDisplayContext context, int lineWidth, int color)
    {
      Pen pen = new Pen(Color.FromArgb(color), lineWidth);

      Point[] cornerPoints = regionDimensions.GetCornerClientPoints(context);

      // make sure the origin point is drawn
      gr.DrawArc(pen, cornerPoints[CvsRegionIndex.Origin].X, cornerPoints[CvsRegionIndex.Origin].Y, 1, 1, 0, 360);

      gr.DrawLine(pen,
                 (float)cornerPoints[CvsRegionIndex.TopRight].X,
                 (float)cornerPoints[CvsRegionIndex.TopRight].Y,
                 (float)cornerPoints[CvsRegionIndex.BottomRight].X,
                 (float)cornerPoints[CvsRegionIndex.BottomRight].Y);
       gr.DrawLine(pen,
                 (float)cornerPoints[CvsRegionIndex.BottomLeft].X,
                 (float)cornerPoints[CvsRegionIndex.BottomLeft].Y,
                 (float)cornerPoints[CvsRegionIndex.TopLeft].X,
                 (float)cornerPoints[CvsRegionIndex.TopLeft].Y);

      RegionDimensions.CurveDimensions curveDimensions = regionDimensions.GetCurveDimensions();

      // if curve center point distance is small, then errors due to minor
      // aspect ratio changes in the display can be safely ignored.
      if ((Math.Abs(curveDimensions.CurveRadiusBottom) < 1000 &&
           Math.Abs(curveDimensions.CurveRadiusTop) < 1000) ||
          Math.Abs(regionDimensions.Curve) >= 30)
      {
        float startAngle = (float)(context.TransformClientDirection(
                                          curveDimensions.CurveStartAngle));
        float sweepAngle = (float)(context.TransformClientSweep(
                                          curveDimensions.CurveEndAngle - curveDimensions.CurveStartAngle));

        PointF curveCenter = curveDimensions.CurveCenter;
        if (context.UsesXYCoordinates)
        {
          curveCenter = context.SensorToClient(curveCenter);
        }
        else // Legacy In-Sight Coordinates...
        {
          curveCenter = SwapCoordinates(curveCenter);
          curveCenter = context.SensorToClient(curveCenter);
        }

        float topRadius = (float)(curveDimensions.CurveRadiusTop * context.ImageScale -
                                  0.5* Math.Sign(curveDimensions.CurveRadiusTop));
        float bottomRadius = (float)(curveDimensions.CurveRadiusBottom * context.ImageScale -
                                     0.5 * Math.Sign(curveDimensions.CurveRadiusBottom));
        float x;
        float y;
        x = curveCenter.X - topRadius;
        y = curveCenter.Y - topRadius;
        float width = 2 * topRadius;
        float height = 2 * topRadius;
        gr.DrawArc(pen, x, y, width, height, startAngle, sweepAngle);

        x = curveCenter.X - bottomRadius;
        y = curveCenter.Y - bottomRadius;
        width = 2 * bottomRadius;
        height = 2 * bottomRadius;
        gr.DrawArc(pen, x, y, width, height, startAngle, sweepAngle);
      }
      else
      {
        // Here we draw a series of lines to approximate the curve.
        // in the end, this is more accurate than the DrawArc method in the
        // ImageBuffer, however it may be slower than draw arc.

        Point[] innerArc = null;
        Point[] outerArc = null;
        regionDimensions.ApproximateArcs(context, out innerArc, out outerArc);

        gr.DrawLines(pen, outerArc);
        gr.DrawLines(pen, innerArc);
      }
    }

    /// <summary>
    /// Draws the axis labels on the display
    /// </summary>
    /// <returns>An array of 2 Rectangles containing the bounds of the axis labels
    /// in client coordinates.</returns>
    private static Rectangle[] PaintAxisLabels(Graphics gr, RegionDimensions regionDimensions, ICvsDisplayContext context, int lineWidth, bool calculateOnly, int color,
                                               bool ShowAxesLabels = true, bool ShowXArrow = true, bool ShowYArrow = true)
    {
      Rectangle[] axisBounds = new Rectangle[2];

      int edgeLength = 4 + lineWidth;

      if (!regionDimensions.UsesXYCoordinates && regionDimensions.IsBent)
      {
        return null; // TODO - This code does not render the labels and arrows on a bent region is legacy In-sight correctly.
      }

      // Not showing labels or arrows, so return empty axis bounds.
      if (!ShowAxesLabels && !ShowXArrow && !ShowYArrow)
        return axisBounds;

      Color clr = Color.FromArgb(color);
      Brush brush = new SolidBrush(clr);
      Point[] pts = regionDimensions.GetCornerClientPoints(context);

      // The X axis center point is the midpoint of the line or arc drawn in *Client* coordinates
      PointF xAxis;
      if (!regionDimensions.IsBent)
      {
        // Compute the X axis midpoint by taking the middle of the boundary line
        // in Client coordinates.
        xAxis = new PointF((pts[CvsRegionIndex.TopLeft].X + pts[CvsRegionIndex.TopRight].X) / 2.0f,
                           (pts[CvsRegionIndex.TopLeft].Y + pts[CvsRegionIndex.TopRight].Y) / 2.0f);
      }
      else
      {
        // Get the X axis handle point in sensor coordinates
        PointF xHandleSensor = regionDimensions.GetHandlePoint(0);
        // Transform to client coordinates
        xAxis = context.SensorToClient(xHandleSensor);
      }

      // The Y axis center point is always the midpoint of the straight line between its corners
      // in *Client* coordinates.
      PointF yAxis;
      yAxis = new PointF((pts[CvsRegionIndex.TopLeft].X + pts[CvsRegionIndex.BottomLeft].X) / 2.0f,
                         (pts[CvsRegionIndex.TopLeft].Y + pts[CvsRegionIndex.BottomLeft].Y) / 2.0f);

      if (!calculateOnly)
      {
        if (ShowXArrow)
        {
          double xAngle = context.TransformClientDirection(regionDimensions.Rotation + regionDimensions.Curve / 2);

          if (regionDimensions.Width < 0)
          {
            xAngle = -xAngle;
          }

          DrawArrow(gr, clr, xAxis.X, xAxis.Y, context.UsesXYCoordinates ? xAngle : 90-xAngle, edgeLength);
        }

        if (ShowYArrow)
        {
          double yAngle = context.TransformClientDirection(regionDimensions.Rotation + 90);
          DrawArrow(gr, clr, yAxis.X, yAxis.Y, context.UsesXYCoordinates ? yAngle : 90-yAngle, edgeLength);
        }
      }

      if (ShowAxesLabels)
      {
        string s = "X";
        double a = context.TransformClientDirection((-regionDimensions.Rotation -
                                                     regionDimensions.Curve / 2 + 90)) * Math.PI / 180;

        SizeF size = gr.MeasureString(s, DefaultFont);
        float dist = size.Height;
        PointF loc;
        if (regionDimensions.UsesXYCoordinates)
        {
          loc = new PointF(
              (float)(xAxis.X - dist * Math.Cos(a) - size.Width / 2),
              (float)(xAxis.Y - dist * Math.Sin(a) - size.Height / 2));
        }
        else
        {
          loc = new PointF(
                (float)(xAxis.X - dist * Math.Sin(a) - size.Width / 2),
                (float)(xAxis.Y - dist * Math.Cos(a) - size.Height / 2));
        }
        axisBounds[0] = new Rectangle(Point.Round(loc), Size.Ceiling(size));

        if (!calculateOnly)
          gr.DrawString(s, DefaultFont, brush, axisBounds[0].X, axisBounds[0].Y);

        s = "Y";
        a = context.TransformClientDirection(-regionDimensions.Rotation) * Math.PI / 180;

        size = gr.MeasureString(s, DefaultFont);
        if (regionDimensions.UsesXYCoordinates)
        {
          loc = new PointF(
            (float)(yAxis.X - dist * Math.Cos(a) - size.Width / 2),
            (float)(yAxis.Y - dist * Math.Sin(a) - size.Height / 2));
        }
        else
        {
          loc = new PointF(
              (float)(yAxis.X + dist * Math.Sin(a) - size.Width / 2),
              (float)(yAxis.Y + dist * Math.Cos(a) - size.Height / 2));
        }
        axisBounds[1] = new Rectangle(Point.Round(loc), Size.Ceiling(size));

        if (!calculateOnly)
          gr.DrawString(s, DefaultFont, brush, axisBounds[1].X, axisBounds[1].Y);
      }

      return axisBounds;
    }

    /// <summary>
    /// Draw an arrow.
    /// </summary>
    /// <param name="g"><see cref="System.Drawing.Graphics"/> object to draw the arrow into.</param>
    /// <param name="color">The color in which to draw.</param>
    /// <param name="x">X value of the location of the arrow.</param>
    /// <param name="y">X value of the location of the arrow.</param>
    /// <param name="angle">The angle in degrees in client coordinates (positive clockwise).</param>
    /// <param name="edgeLength">Edge width of the arrow.</param>
    public static void DrawArrow(Graphics g, Color color, float x, float y, double angle, float edgeLength)
    {
      float hyp = (float)Math.Sqrt(1.25 * edgeLength * edgeLength);

      PointF ptCenter = new PointF(x, y);

      PointF[] pts = new PointF[]
          {
                    new PointF(x + hyp / 2, y),
                    new PointF(x - hyp, y - edgeLength),
                    new PointF(x - hyp, y + edgeLength)
          };

      PointF[] rotPts = pts.Rotate(ptCenter, angle);

      using (Brush b = new SolidBrush(color))
      {
        g.FillPolygon(b, rotPts);
      }
    }
  }
}
