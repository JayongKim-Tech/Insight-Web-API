using System.Drawing;

namespace Cognex.InSight.Web.Controls
{
  /// <summary>
  /// Provides context information for drawing graphics.
  /// </summary>
  public interface ICvsDisplayContext
  {    
    /// <summary>
    /// Computes the location of the specified client point into screen coordinates.
    /// </summary>
    Point PointToScreen(Point pt);

    /// <summary>
    /// Converts the location of the specified display client point into Sensor coordinates.
    /// </summary>
    PointF ClientToSensor(PointF pt);

    /// <summary>
    /// Converts the location of the specified Sensor point into display client coordinates.
    /// </summary>
    PointF SensorToClient(PointF pt);

    /// <summary>
    /// Gets the bounds of the display in client coordinates.
    /// </summary>
    Rectangle DisplayBounds { get; }

    /// <summary>
    /// Converts the given rectangle in Sensor coordinates into display client coordinates.
    /// This is suitable for computing bounding boxes, since the rotation information is not
    /// conveyed in the resulting rectangle.
    /// </summary>
    RectangleF SensorToClient(RectangleF rect);

    /// <summary>
    /// Transforms a vector in client coordinates according to the image rotation in effect.
    /// </summary>
    /// <param name="pt">A vector in client coordinates, relative to the un-rotated image.</param>
    /// <returns>A new vector that accounts for the image rotation.</returns>
    PointF TransformClientVector(PointF pt);

    /// <summary>
    /// Transforms an angular direction in client coordinates according to the image rotation in effect.
    /// </summary>
    /// <param name="angle">An angular direction (in degrees) in client coordinates, relative to the un-rotated image.</param>
    /// <returns>A new angular direction (in degrees) that accounts for the image rotation.</returns>
    double TransformClientDirection(double angle);

    /// <summary>
    /// Transforms an angular sweep in client coordinates according to the image rotation in effect.
    /// </summary>
    /// <param name="angle">An angular sweep (in degrees) in client coordinates, relative to the un-rotated image.</param>
    /// <returns>A new angular sweep (in degrees) that accounts for the image rotation.</returns>
    /// <remarks>Returns the angle unchanged unless the image is mirrored, in which case it negates the angle.</remarks>
    double TransformClientSweep(double angle);
    
    /// <summary>
    /// Gets the scale factor of the displayed image.
    /// </summary>
    /// <remarks>1.0 designates the normal 1:1 display of the image. Larger value will proportionally zoom in on the image. Smaller values will zoom out.</remarks>
    double ImageScale { get; }

    /// <summary>
    /// Flag that designates use of XY Coordinate System.
    /// </summary>
    bool UsesXYCoordinates { get; }
  }
}
