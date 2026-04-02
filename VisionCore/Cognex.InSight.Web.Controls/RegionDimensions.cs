// Copyright (c) 2022 Cognex Corporation. All Rights Reserved

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using Cognex.InSight.Remoting.Serialization;

namespace Cognex.InSight.Web.Controls
{
  /// <summary>
  /// Static class to hold named region indexes for region arrays.
  ///  The array of points which make up the four corners of the region are as follows:
  ///  (These are specified in Sensor coordinates)
  ///   Point[0] = the point specified by the region's row/col
  ///   Point[1] = the point adjacent to Point[0] along the 'x' axis
  ///   Point[2] = the point opposite Point[0]
  ///   Point[3] = the point adjacent to Point[0] along the 'y' axis
  ///
  ///              x
  ///          0--->---1
  ///          |       |
  ///        y v       |
  ///          |       |
  ///          3-------2
  /// </summary>
  public static class CvsRegionIndex
  {
    /// <summary>Top/Left corner index.</summary>
    public const int TopLeft = 0;

    /// <summary>Top/Right corner index.</summary>
    public const int TopRight = 1;

    /// <summary>Bottom/Right corner index.</summary>
    public const int BottomRight = 2;

    /// <summary>Bottom/Left corner index.</summary>
    public const int BottomLeft = 3;

    /// <summary>Origin index.</summary>
    public const int Origin = 0;

    /// <summary>Start point of a line index.</summary>
    public const int LineStart = 0;

    /// <summary>End point of a line index.</summary>
    public const int LineEnd = 1;
  }

  /// <summary>
  /// This class handles all conversions to and from In-Sight coordinate space to
  /// display coordinate space.
  /// </summary>
  internal class RegionDimensions
  {
    #region Fields

    /// <summary>
    /// The region graphic that is being displayed or edited.
    /// </summary>
    private readonly CvsCogRegion mRegion;

    /// <summary>The possible locations in Sensor coordinates where the
    /// label could be drawn</summary>
    private PointF[] mLabelPoints;

    /// <summary>
    /// The array of points which make up the four corners of the region.
    /// These are specified in Sensor coordinates.
    ///   mPoint[0] = the point specified by the region's row/col
    ///   mPoint[1] = the point adjacent to mPoint[0] along the 'x' axis
    ///   mPoint[2] = the point opposite mPoint[0]
    ///   mPoint[3] = the point adjacent to mPoint[0] along the 'y' axis
    ///
    ///              x
    ///          0--->---1
    ///          |       |
    ///        y v       |
    ///          |       |
    ///          3-------2
    /// </summary>
    private PointF[] mPoints = null;

    /// <summary>
    /// The region parameters when calculate was last called.
    /// </summary>
    private CvsCogRegion mLastCalculatedRegion;

    /// <summary>
    /// The rectangle that this graphic fits completely inside of
    /// </summary>
    private RectangleF mBounds = RectangleF.Empty;

    /// <summary>
    /// This structure holds the data used to draw the arcs for the region
    /// </summary>
    public struct CurveDimensions
    {
      public PointF CurveCenter;
      public double CurveRadiusTop;
      public double CurveRadiusBottom;
      public double CurveStartAngle;
      public double CurveEndAngle;
    }

    /// <summary>
    /// The current curve dimensions for this region.  Only valid if IsBent is true.
    /// </summary>
    private CurveDimensions mCurveDimensions;

    private bool mUsesXYCoordinates = true;

    #endregion

    /// <summary>
    /// Default constructor.  Initializes all values to a default region.
    /// </summary>
    public RegionDimensions()
    {
      mRegion = new CvsCogRegion(0, 0, 320, 240, 0, 0);
    }

    /// <summary>
    /// Creates a new RegionDimensions object based on the specified region graphic.
    /// </summary>
    /// <param name="region">The region graphic to initialize the dimensions with</param>
    public RegionDimensions(CvsCogRegion region, bool usesXYCoordinates)
    {
      mRegion = region;
      mUsesXYCoordinates = usesXYCoordinates;
    }

    #region Public Properties

    /// <summary>
    /// Gets a region graphic that represents the current state of these dimensions
    /// </summary>
    public CvsCogRegion Region
    {
      get { return mRegion; }
    }

    /// <summary>
    /// Returns true if this region should be drawn bent
    /// </summary>
    public bool IsBent
    {
      get { return Math.Abs(mRegion.Curve) > 0.1; }
    }

    /// <summary>
    /// Returns true if this region should be drawn rotated
    /// </summary>
    public bool IsRotated
    {
      get { return Math.Abs(mRegion.Angle) > 0.05; }
    }

    /// <summary>
    /// Gets or sets the origin point of this region graphic
    /// </summary>
    public PointF Location
    {
      get { return new PointF((float)mRegion.X, (float)mRegion.Y); }
      set
      {
        mRegion.X = value.X;
        mRegion.Y = value.Y;
        Invalidate();
      }
    }

    /// <summary>
    /// Gets or sets the width parameter of this region
    /// </summary>
    public double Width
    {
      get { return mRegion.Width; }
      set
      {
        mRegion.Width = value;
        Invalidate();
      }
    }

    /// <summary>
    /// Gets or sets the height parameter of this region
    /// </summary>
    public double Height
    {
      get { return mRegion.Height; }
      set
      {
        mRegion.Height = value;
        Invalidate();
      }
    }

    /// <summary>
    /// Gets or sets the rotation parameter of this region
    /// </summary>
    public double Rotation
    {
      get { return mRegion.Angle; }
      set
      {
        mRegion.Angle = value;
        Invalidate();
      }
    }

    /// <summary>
    /// Gets or sets the curve parameter of this region
    /// If curve is negative (radius is positive), then CurveRadiusTop is the inner radius.
    /// If curve is positive (radius is negative), then CurveRadiusTop is the outer radius.
    /// --
    /// Significant because order is important while creating paths
    /// </summary>
    public double Curve
    {
      get { return mRegion.Curve; }
      set
      {
        mRegion.Curve = value;
        Invalidate();
      }
    }

    /// <summary>
    /// Gets the label associated with this graphic
    /// </summary>
    public string Label
    {
      get { return mRegion.Label; }
    }

    public bool UsesXYCoordinates
    {
      get { return mUsesXYCoordinates; }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Override to produce a useful string describing the region
    /// </summary>
    /// <returns></returns>
    public override string ToString()
    {
      return
          string.Format(
              @"<X>{0}<\X><Y>{1}<\Y><Width>{2}<\Width><Height>{3}<\Height><Angle>{4}<\Angle><Curve>{5}<\Curve>",
              new object[] { Location.X, Location.Y, Width, Height, Rotation, Curve });
    }

    /// <summary>
    /// Create a .NET Region object which describes this object
    /// </summary>
    public System.Drawing.Region CreateRegion(ICvsDisplayContext context)
    {
      System.Drawing.Region region = null;

      if (IsBent)
      {
        RegionDimensions.CurveDimensions curve = GetCurveDimensions();

        PointF[] corners = GetCornerSensorPoints();
        PointF TL = context.SensorToClient(corners[CvsRegionIndex.TopLeft]);
        PointF TR = context.SensorToClient(corners[CvsRegionIndex.TopRight]);
        PointF BR = context.SensorToClient(corners[CvsRegionIndex.BottomRight]);
        PointF BL = context.SensorToClient(corners[CvsRegionIndex.BottomLeft]);

        System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();

        if ((Math.Abs(curve.CurveRadiusBottom) < 1000 &&
             Math.Abs(curve.CurveRadiusTop) < 1000) ||
            Math.Abs(Curve) >= 30)
        {
          System.Drawing.PointF center = context.SensorToClient(curve.CurveCenter);

          // If curve is negative (radius is positive), then CurveRadiusTop is the inner radius.
          // If curve is positive (radius is negative), then CurveRadiusTop is the outer radius.
          // --
          // Significant because order is important while creating paths

          float topRadius =
              Math.Abs((float)(curve.CurveRadiusTop * context.ImageScale - 0.5 * Math.Sign(curve.CurveRadiusTop)));
          float bottomRadius =
              Math.Abs((float)(curve.CurveRadiusBottom * context.ImageScale - 0.5 * Math.Sign(curve.CurveRadiusBottom)));
          float startAngle = (float)context.TransformClientDirection(curve.CurveStartAngle);
          float sweepDist = (float)context.TransformClientSweep(-curve.CurveStartAngle + curve.CurveEndAngle);

          float innerRadius,
                outerRadius;
          if (curve.CurveRadiusTop <= 0)
          {
            innerRadius = topRadius;
            outerRadius = bottomRadius;
          }
          else
          {
            innerRadius = bottomRadius;
            outerRadius = topRadius;
          }

          RectangleF outer = new RectangleF();
          outer.X = center.X - outerRadius;
          outer.Y = center.Y - outerRadius;
          outer.Width = outerRadius * 2;
          outer.Height = outer.Width;

          RectangleF inner = new RectangleF();
          inner.X = center.X - innerRadius;
          inner.Y = center.Y - innerRadius;
          inner.Width = innerRadius * 2;
          inner.Height = inner.Width;

          if (curve.CurveRadiusTop <= 0)
          {
            //startAngle = -startAngle;
            startAngle = 180 + startAngle;
            //startAngle += (float) context.TransformClientDirection(Rotation)*2;
            path.AddArc(outer, startAngle + sweepDist, -sweepDist);
            path.AddLine(BR, TR);
            path.AddArc(inner, startAngle, sweepDist);
            path.AddLine(TL, BL);
          }
          else
          {
            if (sweepDist < 0)
            {
              startAngle = startAngle + sweepDist;
              sweepDist = -sweepDist;
            }
            path.AddArc(inner, startAngle, sweepDist);
            path.AddLine(TR, BR);
            path.AddArc(outer, startAngle + sweepDist, -sweepDist);
            path.AddLine(BL, TL);
          }
        }
        else // curves must be drawn as a series of lines
        {
          Point[] innerArc = null,
                  outerArc = null;
          ApproximateArcs(context, out innerArc, out outerArc);
          path.AddLines(outerArc);
          path.AddLine(TR, BR);
          path.AddLines(innerArc);
          path.AddLine(BL, TL);
        }

        region = new System.Drawing.Region(path);
      }
      else
      {
        PointF[] corners = GetCornerSensorPoints();
        if (corners.Length != 4)
          return null;

        PointF[] lines = new PointF[5];
        lines[0] = context.SensorToClient(corners[CvsRegionIndex.TopLeft]);
        lines[1] = context.SensorToClient(corners[CvsRegionIndex.TopRight]);
        lines[2] = context.SensorToClient(corners[CvsRegionIndex.BottomRight]);
        lines[3] = context.SensorToClient(corners[CvsRegionIndex.BottomLeft]);
        lines[4] = context.SensorToClient(corners[CvsRegionIndex.TopLeft]);

        System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddLines(lines);
        region = new System.Drawing.Region(path);
      }
      return region;
    }

    /// <summary>
    /// Returns a series of lines used to approximate the curve.
    /// in the end, this is more accurate than the DrawArc method in the
    /// ImageBuffer, however it may be slower than draw arc.
    /// </summary>
    public void ApproximateArcs(ICvsDisplayContext context, out Point[] InnerCurve, out Point[] OuterCurve)
    {
      Point[] cornerPoints = GetCornerClientPoints(context);
      RegionDimensions.CurveDimensions curveDimensions = GetCurveDimensions();

      // Note, the angle is reversed because the display context treats angles
      // CW positive, however the GraphicMath library is CCW-positive
      float startAngle = -(float)(context.TransformClientDirection(
                                         -curveDimensions.CurveStartAngle));
      float sweepAngle = (float)(context.TransformClientSweep(
                                        curveDimensions.CurveEndAngle - curveDimensions.CurveStartAngle));

      // When the display is in size to fit or fill mode, it will adjust the
      // aspect ratio of the display to precisely fill the displayed pixels.
      // as a result, we must recalculate the arc using the current display
      // pixel locations.
      double sinAngle = Math.Abs(Curve * Math.PI / 360);

      float topRadius = (float)(Math.Sign(Curve) *
                                (GraphicMath.PointToPointDist(
                                     cornerPoints[CvsRegionIndex.TopLeft],
                                     cornerPoints[CvsRegionIndex.TopRight]) / 2) /
                                Math.Sin(sinAngle));

      float bottomRadius = topRadius - (float)(Height *
                                               context.ImageScale);

      float radAngle = -(float)(context.TransformClientDirection(
                                       -90 - Rotation));

      PointF curveCenter = GraphicMath.OffsetPoint(
          cornerPoints[CvsRegionIndex.Origin],
          topRadius,
          radAngle);

      // calculate points
      float maxRadius = Math.Max(Math.Abs(topRadius), Math.Abs(bottomRadius));

      // this formula determines the number of steps required such that the
      // lines don't deflect from the actual curve more than 1 pixel.
      int steps = (int)Math.Ceiling(Math.Abs(sweepAngle) / (Math.Acos(1 - 1 / maxRadius) * 180 / Math.PI));
      float stepSize = sweepAngle / steps;
      float angle = startAngle;

      Point[] topPoints = new Point[steps * 2];
      Point[] bottomPoints = new Point[steps * 2];

      int index = 0;
      for (int i = 0; i <= steps; i++)
      {
        if (i > 1)
        {
          // copy previous point
          topPoints[index] = topPoints[index - 1];
          bottomPoints[index] = bottomPoints[index - 1];
          index++;
        }

        topPoints[index] = GraphicMath.RoundPoint(
            GraphicMath.OffsetPoint(curveCenter, topRadius, angle));
        bottomPoints[index] = GraphicMath.RoundPoint(
            GraphicMath.OffsetPoint(curveCenter, bottomRadius, angle));

        index++;
        angle += stepSize;
      }

      InnerCurve = bottomPoints;
      OuterCurve = topPoints;
    }

    /// <summary>
    /// Gets the corner points of the region in Client coordinates. These are adjusted so
    /// that they lie just inside the region dimension by 1/2 client pixel, so that the
    /// 1-pixel-wide drawn line reaches just to the outside of the region.
    /// </summary>
    /// <param name="context">The display context used to transform the points</param>
    /// <returns>An array of four points</returns>
    public Point[] GetCornerClientPoints(ICvsDisplayContext context)
    {
      PointF[] sensorCorners = GetCornerSensorPoints();

      Point[] clientCorners = new Point[4];
      for (int i = 0; i < 4; i++)
      {
        // Get the corner point in client coordinates
        PointF clientPt = context.SensorToClient(sensorCorners[i]);

        // Now round to integers
        clientCorners[i] = GraphicMath.RoundPoint(clientPt);
      }

      if (!context.UsesXYCoordinates && IsBent) // This is a bit of hack to fix the bent point locations for legacy In-Sight...
      {
        PointF[] pts = this.GetCornerSensorPoints();
        for (int i = 0; i < 4; ++i)
        {
          pts[i] = GraphicsHelper.SwapCoordinates(pts[i]);
          PointF clientPt = context.SensorToClient(pts[i]);
          clientCorners[i] = GraphicMath.RoundPoint(clientPt);
        }
      }

      return clientCorners;
    }

    /// <summary>
    /// Get the mid point on the height edges
    /// </summary>
    /// <returns>2 points are returned the first point is the start and the second is the end.</returns>
    internal List<PointF> GetCenterLinePoints()
    {
      List<PointF> pts = new List<PointF>();
      PointF start = GetHandlePoint(CvsRegionIndex.BottomLeft);
      PointF end = GetHandlePoint(CvsRegionIndex.TopRight);
      pts.Add(new PointF(start.X, start.Y));
      pts.Add(new PointF(end.X, end.Y));
      return pts;
    }

    /// <summary>
    /// Get a point along the center line defined by its distance from the start. This
    /// will work for both bent and straight regions
    /// </summary>
    /// <param name="offset">The distance from the start poinf along the center.</param>
    /// <returns>The point in sensor coordinates.</returns>
    internal PointF GetPointFromOffsetOnCenter(float offset)
    {
      if (IsBent)
      {
        CurveDimensions dims = GetCurveDimensions();
        double centerRadius = (dims.CurveRadiusTop + dims.CurveRadiusBottom) / 2.0;
        double start = (Curve > 0 ? dims.CurveStartAngle : dims.CurveEndAngle) * Math.PI / 180.0;
        double angle = start + offset / centerRadius;
        return new PointF((float)(centerRadius * Math.Cos(angle) + dims.CurveCenter.X),
                          (float)(centerRadius * Math.Sin(angle) + dims.CurveCenter.Y));
      }
      else
      {
        return GraphicMath.OffsetPoint(GetHandlePoint(3), offset, this.Rotation);
      }
    }

    private const double OffsetEpsilon = 0.05F;

    /// <summary>
    /// Return the distance along the center line from a point on the line.
    /// </summary>
    /// <param name="pt">The point on the line. It is assumed that this point is on the center line.</param>
    /// <returns>The distance. Will return -1 if the offset is past the end of the region.</returns>
    internal float GetOffsetFromPointOnCenter(PointF pt)
    {
      if (IsBent)
      {
        CurveDimensions dims = GetCurveDimensions();
        double centerRadius = (dims.CurveRadiusTop + dims.CurveRadiusBottom) / 2.0;
        double angleToPoint = GraphicMath.PointToPointAngle(pt, dims.CurveCenter);
        double startAngle = Curve > 0 ? dims.CurveStartAngle : dims.CurveEndAngle;
        double endAngle = Curve < 0 ? dims.CurveStartAngle : dims.CurveEndAngle;
        if (centerRadius < 0)
        {
          startAngle += 180;
          endAngle += 180;
          centerRadius = -centerRadius;
        }
        double start =
            GraphicMath.NormalizeAngle(Math.Round(Curve < 0 ? startAngle - angleToPoint : angleToPoint - startAngle, 2));
        double offset = (double)(((start) * Math.PI / 180.0) * centerRadius);
        // allow a bit of slop
        if (offset < 0)
        {
          offset = offset > -OffsetEpsilon ? 0 : -1;
        }
        // do this after because -1 < 0
        if (offset > this.mRegion.Width)
        {
          offset = offset < this.mRegion.Width + OffsetEpsilon ? this.mRegion.Width : -1;
        }
        return (float)offset;
      }
      else
      {
        List<PointF> pts = GetCenterLinePoints();
        if (GraphicMath.PointToPointDist(pt, pts[CvsRegionIndex.LineStart]) < 0.5)
        {
          return 0;
        }
        else if (GraphicMath.PointToPointDist(pt, pts[CvsRegionIndex.LineEnd]) < 0.5)
        {
          return (float)Width;
        }
        else
        {
          PointF point = GraphicMath.PointToLineSegIntersection(pt, pts[CvsRegionIndex.LineStart], pts[CvsRegionIndex.LineEnd]);
          if (point != PointF.Empty)
          {
            float offset = GraphicMath.PointToPointDist(pts[CvsRegionIndex.LineStart], point);
            return offset >= 0 && offset <= Width ? offset : -1;
          }
          else
          {
            return -1;
          }
        }
      }
    }

    #region GetCornerSensorPoints

    /// <summary>
    /// Gets the corner points in Sensor coordinates for this region
    /// </summary>
    /// <returns>An array of four PointF structures containing the Sensor
    /// coordinates of the region corners.</returns>
    public PointF[] GetCornerSensorPoints()
    {
      if (!IsValid)
        Invalidate();

      if (mPoints == null)
      {
        calculate();
      }

      return mPoints;
    }

    #endregion

    #region GetCurveDimensions

    /// <summary>
    /// Gets the curve dimensions of this region in screen coordinates
    /// </summary>
    /// <returns>A CurveDimensions structure</returns>
    public CurveDimensions GetCurveDimensions()
    {
      if (!IsValid)
        Invalidate();

      if (!IsBent)
      {
        throw new ApplicationException("Curve must be in bent shape to get curve dimensions");
      }

      if (mCurveDimensions.CurveCenter.IsEmpty)
      {
        calculate();
      }

      CurveDimensions dims = new CurveDimensions();

      dims.CurveCenter = mCurveDimensions.CurveCenter;
      dims.CurveEndAngle = mCurveDimensions.CurveEndAngle;
      dims.CurveRadiusBottom = mCurveDimensions.CurveRadiusBottom;
      dims.CurveRadiusTop = mCurveDimensions.CurveRadiusTop;
      dims.CurveStartAngle = mCurveDimensions.CurveStartAngle;

      return dims;
    }

    /// <summary>
    /// Gets the handle point with index i
    /// </summary>
    /// <param name="i">Must be between 0 and 4</param>
    /// <returns>A point structure giving the location of the handle point in
    /// Sensor coordinates</returns>
    public PointF GetHandlePoint(int i)
    {
      if (!IsValid)
        Invalidate();

      PointF[] cornerPoints = GetCornerSensorPoints();

      // if the region is not bent, or we are getting the midpoint of a straight side
      if (!IsBent || (i % 2) == 1)
      {
        return new PointF(
            (cornerPoints[i].X + cornerPoints[(i + 1) % 4].X) / 2.0F,
            (cornerPoints[i].Y + cornerPoints[(i + 1) % 4].Y) / 2.0F);
      }
      else
      {
        CurveDimensions curveDims = GetCurveDimensions();

        // calculate angle of center line of curved region
        double angle = (mRegion.Angle - 90.0 + (mRegion.Curve / 2.0)) * Math.PI / 180.0;

        // if i = 0 (top side) get the top side radius, otherwise get the bottom side
        double radius = (i == 0) ? curveDims.CurveRadiusTop : curveDims.CurveRadiusBottom;

        if (mUsesXYCoordinates)
        {
          return new PointF(
            (float)(curveDims.CurveCenter.X + radius * Math.Cos(angle)),
            (float)(curveDims.CurveCenter.Y + radius * Math.Sin(angle)));
        }
        else // Hack for legacy In-Sight
        {
          return new PointF(
              (float)(curveDims.CurveCenter.X + radius * Math.Cos(angle)),
              (float)(curveDims.CurveCenter.Y - radius * Math.Sin(angle)));
        }
      }
    }

    /// <summary>
    /// Gets the center point for bend operations
    /// </summary>
    /// <returns></returns>
    public PointF GetBendCenter()
    {
      if (!IsValid)
        Invalidate();

      PointF p1 = GetHandlePoint(0);
      PointF p2 = GetHandlePoint(2);

      return new PointF((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
    }

    /// <summary>
    /// Gets the center point for rotation operations
    /// </summary>
    /// <returns></returns>
    public PointF GetRotationCenter()
    {
      if (!IsValid)
        Invalidate();

      if (Math.Abs(mRegion.Curve) < 180)
      {
        // use the center of the area as the rotation center
        PointF p1 = GetHandlePoint(0);
        PointF p2 = GetHandlePoint(2);

        return new PointF((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
      }
      else
      {
        return GetCurveDimensions().CurveCenter;
      }
    }

    /// <summary>
    /// Gets the maximum curve value for the current region width
    /// </summary>
    /// <returns>The maximum curve value</returns>
    public float GetPhiMax()
    {
      if (!IsValid)
        Invalidate();

      // Note: we have to limit the bend using both the 2*w/h and 2*(w-1)/(h-1)
      // since InSight uses the 2*w/h method which allows over bending when
      // h > w, but doesn't allow as much bend when w > h as the 2*(w-1)/(h-1)
      // method.
      double phiMax = Math.Min(
          (2 * (mRegion.Width) / (mRegion.Height)) * 180 / Math.PI,
          (2 * (mRegion.Width - 1) / (mRegion.Height - 1)) * 180 / Math.PI);

      // subtract an extra 1 degree for good measure
      phiMax -= 1.0;

      if (phiMax > 360.0)
      {
        phiMax = 360.0;
      }

      return (float)phiMax;
    }

    #endregion

    /// <summary>
    /// Returns the possible locations for the label to be printed on the display
    /// </summary>
    /// <returns>The possible locations for the label in Client coordinates.</returns>
    public PointF[] GetLabelPoints()
    {
      if (!IsValid)
        Invalidate();

      // force the bounds to be calculated
      if (mLabelPoints == null)
      {
        calculateBounds();
      }

      return mLabelPoints;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// The rectangle that this region fits completely inside of
    /// </summary>
    public virtual RectangleF Bounds
    {
      get
      {
        if (!IsValid)
          Invalidate();

        if (mBounds.IsEmpty)
          mBounds = calculateBounds();

        return mBounds;
      }
    }

    #endregion

    #region Overridden from GraphicsDimensions

    /// <summary>
    /// Returns true if the specified point in In-Sight coordinates is inside the region
    /// </summary>
    /// <param name="insightPoint">A point in screen coordinates</param>
    /// <returns>True if the point is inside the region</returns>
    public bool Contains(PointF insightPoint)
    {
      if (!IsValid)
        Invalidate();

      if (Bounds.Contains(insightPoint))
      {
        if (!IsBent)
        {
          PointF[] points = GetCornerSensorPoints();
          double hd0 = GraphicMath.PointToLineSegDist(insightPoint, points[CvsRegionIndex.BottomLeft], points[CvsRegionIndex.BottomRight]);
          double wd0 = GraphicMath.PointToLineSegDist(insightPoint, points[CvsRegionIndex.TopLeft], points[CvsRegionIndex.BottomLeft]);
          double hd1 = GraphicMath.PointToLineSegDist(insightPoint, points[CvsRegionIndex.TopLeft], points[CvsRegionIndex.TopRight]);
          double wd1 = GraphicMath.PointToLineSegDist(insightPoint, points[CvsRegionIndex.TopRight], points[CvsRegionIndex.BottomRight]);

          if (Math.Abs(wd0 + wd1 - mRegion.Width) <= 3.0 && Math.Abs(hd0 + hd1 - mRegion.Height) <= 3.0)
          {
            return true;
          }
        }
        else
        {
          CurveDimensions dims = GetCurveDimensions();
          float rad = GraphicMath.PointToPointDist(insightPoint, dims.CurveCenter);
          double ang = GraphicMath.PointToPointAngle(insightPoint, dims.CurveCenter);

          ang = GraphicMath.NormalizeAngle(ang);
          double p0 = mRegion.Angle - 90;
          double p1 = p0 + mRegion.Curve;
          double dp = mRegion.Curve;
          int st = (dims.CurveRadiusTop > 0 ? 1 : -1);
          int sb = (dims.CurveRadiusBottom > 0 ? 1 : -1);

          p0 = GraphicMath.NormalizeAngle(p0 * st);
          p1 = GraphicMath.NormalizeAngle(p1 * st);
          double beginArc;
          double endArc;

          if (mRegion.Curve > 0)
          {
            beginArc = -90 + mRegion.Angle;
            // .001 allows full circle bent areas to be moved
            endArc = beginArc + mRegion.Curve - .001;
          }
          else
          {
            beginArc = 90 + mRegion.Angle - mRegion.Curve;
            endArc = beginArc + mRegion.Curve - .001;
          }

          // otherwise everything is good, check for normal moving conditions
          if (((rad >= Math.Abs(dims.CurveRadiusTop) &&
                rad <= Math.Abs(dims.CurveRadiusBottom)) ||
               (rad <= Math.Abs(dims.CurveRadiusTop) &&
                rad >= Math.Abs(dims.CurveRadiusBottom))) &&
              GraphicMath.AngleInRange(beginArc, ang, endArc))
          {
            return true;
          }
        }
      }

      return false;
    }

    /// <summary>
    /// Calculates the dimensions for the current state of the region
    /// </summary>
    protected void calculate()
    {
      mLastCalculatedRegion = (CvsCogRegion)mRegion.Clone();

      if (IsBent)
      {
        calculateBentRegion();
      }
      else if (IsRotated)
      {
        calculateRotatedRectangle();
      }
      else
      {
        calculateRectangle();
      }
    }

    /// <summary>
    /// Calculates the dimensions for a non-rotated, non-bent region
    /// </summary>
    private void calculateRectangle()
    {
      // determine the origin point in image coordinates

      // calculate rectangle that is just inside the boundaries of the region
      float x = (float)mRegion.X;
      float y = (float)mRegion.Y;
      double w;
      double h;
      if (mUsesXYCoordinates)
      {
        w = mRegion.Width;
        h = mRegion.Height;
      }
      else // Legacy In-Sight Coordinates...
      {
        w = mRegion.Height;
        h = mRegion.Width;
      }

      mPoints = new PointF[4];
      mPoints[CvsRegionIndex.TopLeft] = new PointF(x, y);
      mPoints[CvsRegionIndex.TopRight] = new PointF(x + (float)w, y);
      mPoints[CvsRegionIndex.BottomRight] = new PointF(x + (float)w, y + (float)h);
      mPoints[CvsRegionIndex.BottomLeft] = new PointF(x, y + (float)h);
    }

    /// <summary>
    /// Calculates the dimensions for a non-bent region
    /// </summary>
    private void calculateRotatedRectangle()
    {
      // determine the origin point in image coordinates
      double rotation;
      if (mUsesXYCoordinates)
        rotation = (mRegion.Angle * Math.PI) / 180;
      else
        rotation = ((360-mRegion.Angle) * Math.PI) / 180;

      double sinr = Math.Sin(rotation);
      double cosr = Math.Cos(rotation);

      // calculate origin point of drawing
      float x = (float)mRegion.X;
      float y = (float)mRegion.Y;

      // adjust width and height for current display conditions
      double w;
      double h;
      if (mUsesXYCoordinates)
      {
        w = mRegion.Width;
        h = mRegion.Height;
      }
      else // Legacy In-Sight Coordinates...
      {
        w = mRegion.Height;
        h = mRegion.Width;
      }

      mPoints = new PointF[4];
      mPoints[CvsRegionIndex.TopLeft] = new PointF((float)x, (float)y);
      if (mUsesXYCoordinates)
      {
        mPoints[CvsRegionIndex.TopRight] = new PointF((float)(x + w * cosr), (float)(y + w * sinr));
        mPoints[CvsRegionIndex.BottomRight] = new PointF((float)(x + w * cosr - h * sinr), (float)(y + w * sinr + h * cosr));
        mPoints[CvsRegionIndex.BottomLeft] = new PointF((float)(x - h * sinr), (float)(y + h * cosr));
      }
      else
      {
        mPoints[CvsRegionIndex.TopRight] = new PointF((float)(x + w * cosr), (float)(y - w * sinr));
        mPoints[CvsRegionIndex.BottomRight] = new PointF((float)(x + w * cosr + h * sinr), (float)(y - w * sinr + h * cosr));
        mPoints[CvsRegionIndex.BottomLeft] = new PointF((float)(x + h * sinr), (float)(y + h * cosr));
      }
    }

    /// <summary>
    /// Calculates the dimensions for a bent and rotated region
    /// </summary>
    private void calculateBentRegion()
    {
      // calculate sine and cosine of rotation
      double rotation = (mRegion.Angle * Math.PI) / 180.0;
      double sinr = Math.Sin(rotation);
      double cosr = Math.Cos(rotation);

      // calculate origin point of drawing
      double x = (float)mRegion.X;
      double y = (float)mRegion.Y;

      // adjust widths for zoom factor
      double w = mRegion.Width;
      double h = mRegion.Height;

      // calculate phi: the bend amount of the region
      double phi = Math.Abs(mRegion.Curve) * Math.PI / 180.0; //영역 Curve값은 무조건 양수로 나와야함.. 임시로 절대값으로 수정

      // calculate distance from origin to curve center point
      // circumfrence = width = phi * radius
      // radius = width / phi
      // add height/2 to adjust from center of height to origin of rect
      if (mUsesXYCoordinates)
      {
        mCurveDimensions.CurveRadiusTop = (w / phi) + (h / 2.0);
        mCurveDimensions.CurveRadiusBottom = mCurveDimensions.CurveRadiusTop - h;
      }
      else
      {
        mCurveDimensions.CurveRadiusTop = - (- (w / phi) + (h / 2.0));
        mCurveDimensions.CurveRadiusBottom = mCurveDimensions.CurveRadiusTop + h;
      }

      // calculate center point
      // since radius is taken from center of height of
      if (mUsesXYCoordinates)
      {
        mCurveDimensions.CurveCenter.X = (float)(x - mCurveDimensions.CurveRadiusTop * sinr);
        mCurveDimensions.CurveCenter.Y = (float)(y + mCurveDimensions.CurveRadiusTop * cosr);
      }
      else
      {
        mCurveDimensions.CurveCenter.X = (float)(x + mCurveDimensions.CurveRadiusTop * sinr);
        mCurveDimensions.CurveCenter.Y = (float)(y - mCurveDimensions.CurveRadiusTop * cosr);
      }

      double phi0;
      // calculate curve start and end angles
      if (mUsesXYCoordinates)
      {
        phi0 = -(Math.PI / 2) + rotation;
      }
      else
      {
        phi0 = (Math.PI / 2) + rotation;
      }
      double phi1 = phi0 + phi;

      mPoints = new PointF[4];
      mPoints[CvsRegionIndex.TopLeft] = new PointF((float)x, (float)y);

      if (mUsesXYCoordinates)
      {
        mPoints[CvsRegionIndex.TopRight] = new PointF(
          (float)(mCurveDimensions.CurveCenter.X + mCurveDimensions.CurveRadiusTop * Math.Cos(phi1)),
          (float)(mCurveDimensions.CurveCenter.Y + mCurveDimensions.CurveRadiusTop * Math.Sin(phi1)));
        mPoints[CvsRegionIndex.BottomRight] = new PointF(
          (float)(mCurveDimensions.CurveCenter.X + mCurveDimensions.CurveRadiusBottom * Math.Cos(phi1)),
          (float)(mCurveDimensions.CurveCenter.Y + mCurveDimensions.CurveRadiusBottom * Math.Sin(phi1)));
        mPoints[CvsRegionIndex.BottomLeft] = new PointF(
          (float)(mCurveDimensions.CurveCenter.X + mCurveDimensions.CurveRadiusBottom * Math.Cos(phi0)),
          (float)(mCurveDimensions.CurveCenter.Y + mCurveDimensions.CurveRadiusBottom * Math.Sin(phi0)));
      }
      else
      {
        mPoints[CvsRegionIndex.TopRight] = new PointF(
          (float)(mCurveDimensions.CurveCenter.X - mCurveDimensions.CurveRadiusTop * Math.Cos(phi1)),
          (float)(mCurveDimensions.CurveCenter.Y + mCurveDimensions.CurveRadiusTop * Math.Sin(phi1)));
        mPoints[CvsRegionIndex.BottomRight] = new PointF(
          (float)(mCurveDimensions.CurveCenter.X - mCurveDimensions.CurveRadiusBottom * Math.Cos(phi1)),
          (float)(mCurveDimensions.CurveCenter.Y + mCurveDimensions.CurveRadiusBottom * Math.Sin(phi1)));
        mPoints[CvsRegionIndex.BottomLeft] = new PointF(
          (float)(mCurveDimensions.CurveCenter.X - mCurveDimensions.CurveRadiusBottom * Math.Cos(phi0)),
          (float)(mCurveDimensions.CurveCenter.Y + mCurveDimensions.CurveRadiusBottom * Math.Sin(phi0)));
      }

      // if phi is positive, the start angle is phi1, and the end angle is phi0
      // otherwise it is reversed
      mCurveDimensions.CurveStartAngle = ((phi < 0) ? phi1 : phi0) * 180 / Math.PI;
      mCurveDimensions.CurveEndAngle = ((phi < 0) ? phi0 : phi1) * 180 / Math.PI;
    }

    /// <summary>
    /// Calculates a rectangle that the region will fit into, in Sensor coordinates.
    /// Also calculates the candidate points for drawing the label, in Sensor coordinates.
    /// </summary>
    /// <returns>A RectangleF structure</returns>
    protected RectangleF calculateBounds()
    {
      // This is the list of possible label points in Sensor coordinates
      ArrayList labelPoints = new ArrayList();

      // First, make sure we have calculated the dimensions
      if (mPoints == null)
      {
        calculate();
      }

      // now, create a new 1x1 rectangle at the origin point
      RectangleF bounds = new RectangleF(mPoints[CvsRegionIndex.Origin], new SizeF(0, 0));
      labelPoints.Add(mPoints[CvsRegionIndex.Origin]);

      // Then we start expanding the region to include the 4 corner points
      for (int i = 1; i < 4; i++)
      {
        bounds = IntersectBounds(bounds, mPoints[i]);
        labelPoints.Add(mPoints[i]);
      }

      // if the region is bent, we need to expand the region to include any arcs that would extend beyond
      // the corner points.  This includes any arc points at 0, 90, 180, and 270 degrees, on either the upper
      // or lower arc
      if (IsBent)
      {
        PointF center = mCurveDimensions.CurveCenter;

        // go through each of the following angles: 0, 90, 180, and 270
        int angle;
        double angleRads;
        for (int i = 0; i < 4; i++)
        {
          angle = i * 90;
          angleRads = i * Math.PI / 2;

          // check to see if the current angle is on the top arc
          if (AngleInRange(angle, true))
          {
            // expand the bounds to include the point on the top arc
            double radius = Math.Abs(mCurveDimensions.CurveRadiusTop);

            PointF location = new PointF(
                (float)(center.X + radius * Math.Cos(angleRads)),
                (float)(center.Y + radius * Math.Sin(angleRads)));

            bounds = IntersectBounds(bounds, location);
            labelPoints.Add(location);
          }

          // check to see if the current angle is on the bottom arc
          if (AngleInRange(angle, false))
          {
            // expand the bounds to include the point on the bottom arc
            double radius = Math.Abs(mCurveDimensions.CurveRadiusBottom);

            PointF location = new PointF(
                (float)(center.X + radius * Math.Cos(angleRads)),
                (float)(center.Y + radius * Math.Sin(angleRads)));

            bounds = IntersectBounds(bounds, location);
            labelPoints.Add(location);
          }
        }
      }

      mLabelPoints = (PointF[])labelPoints.ToArray(typeof(PointF));

      return bounds;
    }

    private RectangleF IntersectBounds(RectangleF bounds, PointF point)
    {
      return RectangleF.Union(bounds, new RectangleF(point.X, point.Y, 0, 0));
    }

    /// <summary>
    /// Returns true if the specified region arg includes the specified angle
    /// </summary>
    /// <param name="angle"></param>
    /// <param name="top"></param>
    /// <returns></returns>
    public bool AngleInRange(double angle, bool top)
    {
      double startAngle = mCurveDimensions.CurveStartAngle;

      // subtract .01 from end angle to allow 360 degree regions to work.
      double endAngle = mCurveDimensions.CurveEndAngle - 0.01;
      double radius = top ? mCurveDimensions.CurveRadiusTop : mCurveDimensions.CurveRadiusBottom;

      if (radius < 0)
      {
        startAngle -= 180;
        endAngle -= 180;
        radius = -radius;
      }

      return GraphicMath.AngleInRange(startAngle, angle, endAngle);
    }

    /// <summary>
    /// Invalidates any cached parameters when something changes, either the screen dimensions changed,
    /// or a region parameter changed.
    /// </summary>
    public void Invalidate()
    {
      mPoints = null;
      mCurveDimensions.CurveCenter = PointF.Empty;
      mLabelPoints = null;
      mBounds = RectangleF.Empty;
    }

    /// <summary>
    /// Returns true if the region hasn't changed since it was last calculated.
    /// </summary>
    public bool IsValid
    {
      get
      {
        return mLastCalculatedRegion != null &&
               Region.X == mLastCalculatedRegion.X &&
               Region.Y == mLastCalculatedRegion.Y &&
               Region.Height == mLastCalculatedRegion.Height &&
               Region.Width == mLastCalculatedRegion.Width &&
               Region.Angle == mLastCalculatedRegion.Angle &&
               Region.Curve == mLastCalculatedRegion.Curve;
      }
    }

    #endregion

  }
}
