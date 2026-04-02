// Copyright (c) 2022 Cognex Corporation. All Rights Reserved

using System;
using System.Drawing;

namespace Cognex.InSight.Web.Controls
{
  /// <summary>
  /// Display Graphic's utility math functions
  /// </summary>
  internal abstract class GraphicMath
  {
    /// <summary>
    /// Rounds a point to the nearest pixel.  Use this instead of Math.Round or
    /// Point.Round since these round to the even number on half pixels.
    /// </summary>
    /// <param name="point">The point to round</param>
    /// <returns>The rounded point</returns>
    internal static Point RoundPoint(PointF point)
    {
      return RoundPoint(point.X, point.Y);
    }

    /// <summary>
    /// Find the point between two points
    /// </summary>
    /// <param name="point">One of the points</param>
    /// <param name="point2">Two of the points</param>
    /// <returns>The mid point</returns>
    internal static PointF MidPoint(PointF point, PointF point2)
    {
      return new PointF((point.X + point2.X) / 2.0F, (point.Y + point2.Y) / 2.0F);
    }

    /// <summary>
    /// Rounds a point to the nearest pixel.  Use this instead of Math.Round or
    /// Point.Round since these round to the even number on half pixels.
    /// </summary>
    /// <param name="x">The x-coordinate of the point to round</param>
    /// <param name="y">The x-coordinate of the point to round</param>
    /// <returns>The rounded point</returns>
    internal static Point RoundPoint(double x, double y)
    {
      return new Point(Round(x), Round(y));
    }

    /// <summary>
    /// Rounds to the nearest integer. Always rounds away from0 for 0.5.
    /// </summary>
    /// <param name="d">The floating point value to round</param>
    /// <returns>The rounded integer</returns>
    internal static int Round(double d)
    {
      return (int)Math.Round(d, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Returns the specified point rotated about the specified origin
    /// </summary>
    /// <param name="origin">The origin to rotate the point around</param>
    /// <param name="point">The point to rotate</param>
    /// <param name="theta">The angle to rotate the point</param>
    /// <returns>The rotated point</returns>
    internal static PointF RotatePoint(PointF origin, PointF point, double theta)
    {
      double dx = point.X - origin.X;
      double dy = point.Y - origin.Y;
      double d = Math.Sqrt(dx * dx + dy * dy);
      double a = Math.Atan2(dy, dx) + theta * Math.PI / 180.0;

      return new PointF((float)(origin.X + d * Math.Cos(a)),
                        (float)(origin.Y + d * Math.Sin(a)));
    }

    /// <summary>
    /// Returns a new point that is offset by the specified distance and angle from the original point
    /// </summary>
    /// <param name="point">The point to start the offset from</param>
    /// <param name="distance">The distance between the new point and the original point</param>
    /// <param name="degrees">The angle in degrees CCW from the original point to the new point</param>
    /// <returns></returns>
    internal static PointF OffsetPoint(PointF point, double distance, double degrees)
    {
      double radians = degrees * Math.PI / 180;
      return new PointF((float)(point.X + distance * Math.Cos(radians)),
                        (float)(point.Y + distance * Math.Sin(radians)));
    }

    /// <summary>
    /// Returns the distance between point p0 and point p1
    /// </summary>
    /// <param name="p0">The point p0</param>
    /// <param name="p1">The point p1</param>
    /// <returns>The distance between point p0 and point p1</returns>
    internal static float PointToPointDist(PointF p0, PointF p1)
    {
      double dx = p1.X - p0.X;
      double dy = p1.Y - p0.Y;

      return (float)System.Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Returns the point where the specified point from the specified line segment would intersect
    /// </summary>
    /// <param name="point">The point to check</param>
    /// <param name="lineStart">The start of the line segment</param>
    /// <param name="lineEnd">The end of the line segment</param>
    /// <returns>The point where the specified point and the line segment</returns>
    internal static PointF PointToLineSegIntersection(PointF point, PointF lineStart, PointF lineEnd)
    {
      const float e = 0.001f;
      LineStdForm line = new LineStdForm(lineStart, lineEnd);
      LineStdForm perpendicularLine = line.CreatePerpendicularLine(point);

      PointF intersection = perpendicularLine.Intersect(line);

      // pointToLineDist: return pointToPointDist(r, c, r0, c0);
      // check end points
      if (lineEnd.X < lineStart.X)
      {
        // swap
        PointF swap = lineStart;
        lineStart = lineEnd;
        lineEnd = swap;
      }
      if (lineStart.Y < lineEnd.Y)
      {
        if (intersection.X < lineStart.X || intersection.Y < lineStart.Y)
          return PointF.Empty;
        if (intersection.X > lineEnd.X || intersection.Y > lineEnd.Y)
          return PointF.Empty;
      }
      else
      {
        if (intersection.X < (lineStart.X - e) || intersection.Y > (lineStart.Y + e))
          return PointF.Empty;
        if (intersection.X > (lineEnd.X + e) || intersection.Y < (lineEnd.Y - e))
          return PointF.Empty;
      }
      return intersection;
    }

    /// <summary>
    /// Returns the distance of the specified point from the specified line segment
    /// </summary>
    /// <param name="point">The point to check</param>
    /// <param name="lineStart">The start of the line segment</param>
    /// <param name="lineEnd">The end of the line segment</param>
    /// <returns>The distance between the specified point and the line segment</returns>
    internal static double PointToLineSegDist(PointF point, PointF lineStart, PointF lineEnd)
    {
      PointF closest;
      float dx = lineEnd.X - lineStart.X;
      float dy = lineEnd.Y - lineStart.Y;
      if ((dx == 0) && (dy == 0))
      {
        // It's a point not a line segment.
        closest = lineStart;
        dx = point.X - lineStart.X;
        dy = point.Y - lineStart.Y;
        return Math.Sqrt(dx * dx + dy * dy);
      }

      // Calculate the t that minimizes the distance.
      float t = ((point.X - lineStart.X) * dx + (point.Y - lineStart.Y) * dy) /
                (dx * dx + dy * dy);

      // See if this represents one of the segment's
      // end points or a point in the middle.
      if (t < 0)
      {
        closest = new PointF(lineStart.X, lineStart.Y);
        dx = point.X - lineStart.X;
        dy = point.Y - lineStart.Y;
      }
      else if (t > 1)
      {
        closest = new PointF(lineEnd.X, lineEnd.Y);
        dx = point.X - lineEnd.X;
        dy = point.Y - lineEnd.Y;
      }
      else
      {
        closest = new PointF(lineStart.X + t * dx, lineStart.Y + t * dy);
        dx = point.X - closest.X;
        dy = point.Y - closest.Y;
      }

      return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Calculates the distance from p1 to the intersection of a line vector
    /// starting at p1 going through p2, and the line specified by lineStart and lineEnd.
    /// </summary>
    /// <param name="p1">The start of the line vector</param>
    /// <param name="p2">The point specifying the direction of the line vector</param>
    /// <param name="lineStart">The start of the intersection line</param>
    /// <param name="lineEnd">The end of the intersection line</param>
    /// <returns>The distance from p1 to the intersection point, or Double.MaxValue if
    /// the lines are parallel, or if the intersection point is not on the vector</returns>
    internal static double PointThruPointLineDist(
        PointF p1,
        PointF p2,
        PointF lineStart,
        PointF lineEnd)
    {
      LineStdForm vector = new LineStdForm(p1, p2);
      LineStdForm line = new LineStdForm(lineStart, lineEnd);

      PointF intersection = vector.Intersect(line);

      if (intersection.IsEmpty)
      {
        // the lines were parallel
        return System.Double.MaxValue;
      }

      // calculate the distance from p1 to the intersection point
      double a = intersection.Y - p1.Y;
      double b = intersection.X - p1.X;
      double d1 = a * a + b * b;

      // calculate the distance from p2 to the intersections point
      a = intersection.Y - p2.Y;
      b = intersection.X - p2.X;
      double d2 = a * a + b * b;

      // make sure that then distance from p1 to the intersection is 
      // greater than the distance from p2 to the intersection point
      // if this is not true, then the line is not going through p2
      if (d2 > d1)
      {
        return System.Double.MaxValue;
      }

      // return the distance from p1 to the intersection point
      return System.Math.Sqrt(d1);
    }

    /// <summary>
    /// Calculates the distance from the specified point to the intersection of a line vector
    /// starting at the point pointing at the specified angle, and the line specified by 
    /// lineStart and lineEnd.
    /// </summary>
    /// <param name="point">The start of the line vector</param>
    /// <param name="angle">The angle of the line vector</param>
    /// <param name="lineStart">The start of the intersection line</param>
    /// <param name="lineEnd">The end of the intersection line</param>
    /// <returns>The distance from p1 to the intersection point, or Double.MaxValue if
    /// the lines are parallel, or if the intersection point is not on the vector</returns>
    internal static double VectorToLineDist(
        PointF point,
        double angle,
        PointF lineStart,
        PointF lineEnd)
    {
      LineStdForm vector = new LineStdForm(point, angle);
      LineStdForm line = new LineStdForm(lineStart, lineEnd);

      PointF intersection = vector.Intersect(line);

      if (intersection.IsEmpty)
      {
        // the lines were parallel
        return System.Double.MaxValue;
      }

      // check direction
      double dx = intersection.X - point.X;
      double dy = intersection.Y - point.Y;

      if (((vector.A < -1e-10) ^ (dx > 1e-10)) ||
          ((vector.B > 1e-10) ^ (dy > -1e-10)))
      {
        return System.Double.MaxValue; // other direction
      }

      double d = dx * dx + dy * dy;

      return System.Math.Sqrt(d);
    }

    internal static double NormalizeAngle(double angle)
    {
      return angle - (Math.Floor(angle / 360.0) * 360.0);
    }

    internal static double PointToPointAngle(PointF p0, PointF p1)
    {
      return System.Math.Atan2(p0.Y - p1.Y, p0.X - p1.X) * 180.0 / System.Math.PI;
    }

    /// <summary>
    /// Verifies that an angle exists between a lower bound and an upper bound
    /// </summary>
    /// <param name="lowerBound">the lower bound of the range</param>
    /// <param name="angle">the angle to test</param>
    /// <param name="upperBound">the upper bound of the range</param>
    /// <returns>true if lowerBound &lt; angle &lt; upperBound</returns>
    internal static bool AngleInRange(double lowerBound, double angle, double upperBound)
    {
      // force angle and upperBound to be between lowerBound and lowerBound + 360
      angle = NormalizeAngle(angle - lowerBound) + lowerBound;
      upperBound = NormalizeAngle(upperBound - lowerBound) + lowerBound;
      return (angle >= lowerBound) && (angle <= upperBound);
    }

    #region LineStdForm

    /// <summary>
    /// Holds the A, B, and C parameters for a line in standard form:
    ///   Ax + By = C
    /// </summary>
    private struct LineStdForm
    {
      public double A,
                    B,
                    C;

      /// <summary>
      /// Calculates the line standard form from two points
      /// </summary>
      /// <param name="point0">The first point on the line</param>
      /// <param name="point1">The second point on the line</param>
      public LineStdForm(PointF point0, PointF point1)
      {
        // calculate line standard form from points
        A = point0.X - point1.X;
        B = point1.Y - point0.Y;
        C = point1.X * point0.Y - point0.X * point1.Y;
      }

      /// <summary>
      /// Calculates the line standard form from a point vector
      /// </summary>
      /// <param name="point">The starting point of the vector</param>
      /// <param name="angle">The angle of the vector</param>
      public LineStdForm(PointF point, double angle)
      {
        angle = NormalizeAngle(angle);
        double angleRads = angle * System.Math.PI / 180;

        // make this more accurate for angles of 0, 90, 180, and 270
        if (angle == 0.0 || angle == 180.0)
        {
          A = (angle == 0.0) ? -1.0 : 1.0;
          B = 0;
        }
        else if (angle == 90.0 || angle == 270.0)
        {
          A = 0;
          B = (angle == 90.0) ? -1.0 : 1.0;
        }
        else
        {
          A = -System.Math.Cos(angleRads);
          B = -System.Math.Sin(angleRads);
        }

        C = (-point.Y) * A - point.X * B;
      }

      /// <summary>
      /// Creates a line perpendicular to this line, which contains the specified point
      /// </summary>
      /// <param name="startPoint">The point which the new line contains</param>
      /// <returns>A new line</returns>
      public LineStdForm CreatePerpendicularLine(PointF startPoint)
      {
        LineStdForm newLine;
        newLine.A = -B;
        newLine.B = A;
        newLine.C = B * startPoint.Y - A * startPoint.X;

        return newLine;
      }

      /// <summary>
      /// Intersects two lines
      /// </summary>
      /// <param name="rhs">The line to intersect with this line.</param>
      /// <returns>The intesection point.  If the two lines are parallel, then returns 
      /// PointF.Empty</returns>
      public PointF Intersect(LineStdForm rhs)
      {
        double f = rhs.A * B - A * rhs.B;

        if (f == 0)
        {
          // the lines are parallel
          return PointF.Empty;
        }

        double x = (A * rhs.C - rhs.A * C) / f;
        double y = (rhs.B * C - B * rhs.C) / f;

        return new PointF((float)x, (float)y);
      }
    }

    #endregion
  }
}
