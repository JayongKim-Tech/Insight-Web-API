// Copyright (c) 2021 Cognex Corporation. All Rights Reserved

using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Cognex.Extensions
{
  /// <summary>
  /// Math-based extension methods.
  /// </summary>
  public static class MathExtensions
  {
    #region Rounding Methods

    /// <summary>
    /// Round a <see cref="PointF"/> to a <see cref="Point"/>.
    /// </summary>
    /// <remarks>
    /// This method differs from <see cref="Point.Round(PointF)"/> in that it uses
    /// MidpointRounding instead of ToEven, which means the results will be more
    /// consistent for drawing pixels.
    /// </remarks>
    /// <param name="self">PointF to round.</param>
    /// <param name="rounding">Midpoint rounding algorithm to use.</param>
    /// <returns>Rounded Point.</returns>
    public static Point Round(this PointF self, MidpointRounding rounding = MidpointRounding.AwayFromZero)
    {
      return new Point((int)Math.Round(self.X, rounding),
                       (int)Math.Round(self.Y, rounding));
    }

    /// <summary>
    /// Round a <see cref="SizeF"/> struct to a <see cref="Size"/>.
    /// </summary>
    /// <remarks>
    /// This method differs from <see cref="Size.Round(SizeF)"/> in that it uses
    /// MidpointRounding instead of ToEven, which means the results will be more
    /// consistent for drawing pixels.
    /// </remarks>
    /// <param name="szF">SizeF to round.</param>
    /// <param name="rounding">Midpoint rounding algorithm to use.</param>
    /// <returns>Rounded Size.</returns>
    public static Size Round(this SizeF szF, MidpointRounding rounding = MidpointRounding.AwayFromZero)
    {
      return new Size((int)Math.Round(szF.Width, rounding),
                      (int)Math.Round(szF.Height, rounding));
    }

    /// <summary>
    /// Round a <see cref="RectangleF"/> to a <see cref="Rectangle"/>.
    /// </summary>
    /// <remarks>
    /// This method differs from <see cref="Rectangle.Round(RectangleF)"/> in that it uses
    /// rounds the Top/Left and Bottom/Right points individually instead of rounding the
    /// width and height and it uses MidpointRounding instead of ToEven, which means the 
    /// results will be more consistent for drawing pixels.
    /// </remarks>
    /// <param name="rectF">RectangleF to round.</param>
    /// <param name="rounding">Midpoint rounding algorithm to use.</param>
    /// <returns>Rounded Rectangle.</returns>
    public static Rectangle Round(this RectangleF rectF, MidpointRounding rounding = MidpointRounding.AwayFromZero)
    {
      int left = (int)Math.Round(rectF.Left, rounding);
      int right = (int)Math.Round(rectF.Right, rounding);
      int top = (int)Math.Round(rectF.Top, rounding);
      int bottom = (int)Math.Round(rectF.Bottom, rounding);
      return new Rectangle(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Round a floating point value to an integer.
    /// </summary>
    /// <param name="num">Value to round.</param>
    /// <param name="rounding">Midpoint rounding algorithm to use.</param>
    /// <returns>Rounded integer value.</returns>
    public static int Round(this double num, MidpointRounding rounding = MidpointRounding.AwayFromZero)
    {
      return (int)Math.Round(num, rounding);
    }

    /// <summary>
    /// Round a floating point value to an integer.
    /// </summary>
    /// <param name="num">Value to round.</param>
    /// <param name="rounding">Midpoint rounding algorithm to use.</param>
    /// <returns>Rounded integer value.</returns>
    public static int Round(this float num, MidpointRounding rounding = MidpointRounding.AwayFromZero)
    {
      return (int)Math.Round(num, rounding);
    }

    #endregion

    #region Bounding Methods

    /// <summary>
    /// Returns the smallest bounding <see cref="Rectangle"/> for a <see cref="RectangleF"/>.
    /// </summary>
    /// <param name="rectF"><see cref="RectangleF"/> to find the bounding rect for.</param>
    /// <returns>Bounding <see cref="Rectangle"/> for <paramref name="rectF"/></returns>
    public static Rectangle BoundingRect(this RectangleF rectF)
    {
      int left = (int)Math.Floor(rectF.Left);
      int right = (int)Math.Ceiling(rectF.Right);
      int top = (int)Math.Floor(rectF.Top);
      int bottom = (int)Math.Ceiling(rectF.Bottom);
      return new Rectangle(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Returns the top/left integer value of the <see cref="PointF"/>.
    /// </summary>
    /// <param name="pointF">The <see cref="PointF"/> to find the top/left corner for.</param>
    /// <returns>Top/left portion of the point.</returns>
    public static Point Floor(this PointF pointF)
    {
      return new Point((int)Math.Floor(pointF.X),
                       (int)Math.Floor(pointF.Y));
    }

    /// <summary>
    /// Returns the smallest <see cref="Size"/> that is greater than or equal to a <see cref="SizeF"/>.
    /// </summary>
    /// <param name="sizeF"><see cref="SizeF"/> to find the ceiling for.</param>
    /// <returns>Smalles <see cref="Size"/> greater than or equal to <paramref name="sizeF"/></returns>
    public static Size Ceiling(this SizeF sizeF)
    {
      return new Size((int)Math.Ceiling(sizeF.Width),
                      (int)Math.Ceiling(sizeF.Height));
    }

    /// <summary> Gets the center point for the given rectangle. </summary>
    /// <param name="rect"> The rectangle of which to get the center. </param>
    /// <returns> The point that represents the center of the given rectangle. </returns>
    public static PointF GetCenterPoint(this RectangleF rect)
    {
      var x = (rect.Left + rect.Right) / 2.0f;
      var y = (rect.Top + rect.Bottom) / 2.0f;
      return new PointF(x, y);
    }

    #endregion

    #region Matrix Operations

    /// <summary>
    /// Multiplies the <see cref="PointF"/> by the <see cref="Matrix"/>. The translation elements of 
    /// the <see cref="Matrix"/> (third row) are ignored.
    /// </summary>
    /// <param name="matrix"><see cref="Matrix"/> to use for the transform.</param>
    /// <param name="vector"><see cref="PointF"/> to transform.</param>
    /// <returns>Transformed <see cref="PointF"/></returns>
    public static PointF TransformVector(this Matrix matrix, PointF vector)
    {
      PointF[] pts = { vector };
      matrix.TransformVectors(pts);
      return pts[0];
    }

    /// <summary>
    /// Applies the geometric transform represented by this <see cref="Matrix"/> to the <see cref="PointF"/>.
    /// </summary>
    /// <param name="matrix"><see cref="Matrix"/> to use for the transform.</param>
    /// <param name="vector"><see cref="PointF"/> to transform.</param>
    /// <returns>Transformed <see cref="PointF"/></returns>
    public static PointF TransformPoint(this Matrix matrix, PointF vector)
    {
      PointF[] pts = { vector };
      matrix.TransformPoints(pts);
      return pts[0];
    }

    private static PointF[] ToPointFArray(this RectangleF rect)
    {
      return new PointF[]
          {
                    rect.Location, // Top Left
                    new PointF(rect.Right, rect.Bottom), // Bottom Right
          };
    }

    private static RectangleF FromPointFArray(PointF[] points)
    {
      return new RectangleF(points[0], new SizeF(points[1].X - points[0].X, points[1].Y - points[0].Y));
    }

    /// <summary>
    /// Multiplies the <see cref="RectangleF"/> by the <see cref="Matrix"/>. The translation elements of the
    /// <see cref="Matrix"/> (third row) are ignored.
    /// </summary>
    /// <param name="matrix"><see cref="Matrix"/> to use for the transform.</param>
    /// <param name="rect"><see cref="RectangleF"/> to transform.</param>
    /// <returns>Transformed <see cref="RectangleF"/></returns>
    public static RectangleF VectorTransformRectPoints(this Matrix matrix, RectangleF rect)
    {
      PointF[] pts = rect.ToPointFArray();
      matrix.TransformVectors(pts);
      return FromPointFArray(pts);
    }

    /// <summary>
    /// Applies the geometric transform represented by this <see cref="Matrix"/> to the <see cref="RectangleF"/>.
    /// </summary>
    /// <param name="matrix"><see cref="Matrix"/> to use for the transform.</param>
    /// <param name="rect"><see cref="RectangleF"/> to transform.</param>
    /// <returns>Transformed <see cref="RectangleF"/></returns>
    public static RectangleF TransformRectPoints(this Matrix matrix, RectangleF rect)
    {
      PointF[] pts = rect.ToPointFArray();
      matrix.TransformPoints(pts);
      return FixTransformRect(FromPointFArray(pts));
    }

    /// <summary>
    /// Fix the rectangle dimensions after a transformation so the Width and Height are non-negative.
    /// This could include quadrant errors when rotating, or capping the min Width/Height to 1 pixel.
    /// </summary>
    /// <remarks>
    /// This can occur, if the rect is rotated and what was the top/right corner is now the
    /// top/left. In that case the width would then be negative.
    /// </remarks>
    /// <param name="rect">Transformed rectangle to fix.</param>
    /// <returns>Rectangle with the appropriate dimensions.</returns>
    private static RectangleF FixTransformRect(RectangleF rect)
    {
      RectangleF fixedRect = rect;

      if (rect.X > rect.Right)
      {
        fixedRect.X = rect.Right;
        fixedRect.Width = rect.X - rect.Right;
      }

      if (rect.Y > rect.Bottom)
      {
        fixedRect.Y = rect.Bottom;
        fixedRect.Height = rect.Y - rect.Bottom;
      }

      fixedRect.Width = Math.Max(fixedRect.Width, 1);
      fixedRect.Height = Math.Max(fixedRect.Height, 1);

      return fixedRect;
    }

    #endregion

    #region Trig Methods

    /// <summary>
    /// Converts an angle measurement from degrees to radians.
    /// </summary>
    /// <param name="deg">Angle in degrees.</param>
    /// <returns>Angle in radians.</returns>
    public static double DegToRad(this double deg)
    {
      return deg * (Math.PI / 180);
    }

    /// <summary>
    /// Converts an angle measurement from radians to degrees.
    /// </summary>
    /// <param name="rad">Angle in radians.</param>
    /// <returns>Angle in degrees.</returns>
    public static double RadToDeg(this double rad)
    {
      return rad * (180 / Math.PI);
    }

    /// <summary>
    /// Rotate a point about an origin point.
    /// </summary>
    /// <param name="pt">Point to rotate.</param>
    /// <param name="pOrigin">Center point of rotation.</param>
    /// <param name="cosTheta">Cosine of the angle of rotation.</param>
    /// <param name="sinTheta">Sine of the angle of rotation.</param>
    /// <returns>Rotated point.</returns>
    public static PointF Rotate(this PointF pt, PointF pOrigin, double cosTheta, double sinTheta)
    {
      double x = (pt.X - pOrigin.X) * cosTheta - (pt.Y - pOrigin.Y) * sinTheta + pOrigin.X;
      double y = (pt.X - pOrigin.X) * sinTheta + (pt.Y - pOrigin.Y) * cosTheta + pOrigin.Y;
      return new PointF((float)x, (float)y);
    }

    /// <summary>
    /// Rotate a set of points about an origin point.
    /// </summary>
    /// <param name="pts">Points to rotate.</param>
    /// <param name="pOrigin">Center point of rotation.</param>
    /// <param name="theta">Angle of rotation.</param>
    /// <returns>Rotated points.</returns>
    public static PointF[] Rotate(this PointF[] pts, PointF pOrigin, double theta)
    {
      double thetaRad = theta.DegToRad();
      double cosTheta = Math.Cos(thetaRad);
      double sinTheta = Math.Sin(thetaRad);

      PointF[] rotPts = new PointF[pts.Length];

      for (int i = 0; i < pts.Length; i++)
      {
        rotPts[i] = pts[i].Rotate(pOrigin, cosTheta, sinTheta);
      }

      return rotPts;
    }

    #endregion
  }
}
