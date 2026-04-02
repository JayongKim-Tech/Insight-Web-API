using System;
using System.Drawing;

namespace Cognex.InSight.Web.Controls
{
  public class DisplayContext : ICvsDisplayContext
  {
    private bool _usesXYCoordinates;
    private double _imageScale;
    private int _panX;
    private int _panY;
    private Rectangle _displayBounds;

    public DisplayContext(bool usesXYCoordinates, double imageScale, int panX, int panY, Rectangle displayBounds)
    {
      _usesXYCoordinates = usesXYCoordinates;
      _imageScale = imageScale;
      _panX = panX;
      _panY = panY;
      _displayBounds = displayBounds;
    }

    public bool UsesXYCoordinates
    {
      get { return _usesXYCoordinates; }
    }

    public double ImageScale
    {
      get { return _imageScale; }
    }

    public PointF ClientToSensor(PointF pt)
    {
      PointF clientPt = new PointF(pt.X / (float)_imageScale, pt.Y / (float)_imageScale);
      return clientPt;
    }

    public Point PointToScreen(Point pt)
    {
      throw new NotImplementedException();
    }

    public PointF SensorToClient(PointF pt)
    {
      if (_usesXYCoordinates)
      {
        PointF clientPt = new PointF(_panX + (pt.X * (float)_imageScale), _panY + (pt.Y * (float)_imageScale));
        return clientPt;
      }
      else // Use legacy In-Sight coordinates...
      {
        PointF clientPt = new PointF(_panX + (pt.Y * (float)_imageScale), _panY + (pt.X * (float)_imageScale));
        return clientPt;
      }
    }

    public RectangleF SensorToClient(RectangleF rect)
    {
      RectangleF client = new RectangleF();

      PointF pt = SensorToClient(new PointF((float)rect.X, (float)rect.Y));
      client.X = pt.X;
      client.Y = pt.Y;
      if (_usesXYCoordinates)
      {
        client.Width = rect.Width * (float)_imageScale;
        client.Height = rect.Height * (float)_imageScale;
      }
      else
      {
        client.Width = rect.Height * (float)_imageScale;
        client.Height = rect.Width * (float)_imageScale;
      }
      return client;
    }

    public Rectangle DisplayBounds
    {
      get { return _displayBounds; }
    }

    public double TransformClientDirection(double angle)
    {
      // TODO - Add code when ImageOrientation is supported.
      return angle;
    }

    public double TransformClientSweep(double angle)
    {
      if (_usesXYCoordinates)
        return angle;
      else
        return -angle;
    }

    public PointF TransformClientVector(PointF pt)
    {
      throw new NotImplementedException();
    }
  }
}
