using Newtonsoft.Json.Linq;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Cognex.InSight.Remoting.Serialization;

namespace Cognex.InSight.Web.Controls
{
  public partial class CvsDisplay : UserControl
  {
    private CvsInSight _inSight;

    private bool _usesXYCoordinates = false;

    // The graphics, if any
    private CvsCogShape[] _graphics = new CvsCogShape[0];
    private CvsCogShape[] _nextGraphics = new CvsCogShape[0];

    //wpf 그레픽 On/Off
    public bool IsDrawingEnabled { get; set; } = true;

    public CvsDisplay()
    {
      InitializeComponent();
    }

    /// <summary>
    /// Sets the CvsInSight for this Spreadsheet.
    /// </summary>
    public void SetInSight(CvsInSight inSight)
    {
      _inSight = inSight;
    }

    public void InitDisplay()
    {
      picBox.ImageLocation = "";
      _graphics = new CvsCogShape[0]; // Clear any old graphics
      _nextGraphics = new CvsCogShape[0];
    }

    public async Task OnConnected()
    {
      _usesXYCoordinates = _inSight.UsesXYCoordinates;
      await UpdateResults();
    }

    public async Task UpdateResults()
    {
      if (_inSight == null)
        return;

      // Get the graphics that may be rendered later in the OnPaint
      try
      {
        if (_inSight.LiveMode)
        {
          _nextGraphics = new CvsCogShape[0];
        }
        else
        {
          _nextGraphics = await _inSight.GetGraphicsAsync();
        }
      }
      catch (Exception ex)
      {
        _nextGraphics = new CvsCogShape[0];
        Debug.WriteLine($"UpdateResults Exception: {ex.Message}");
      }

      // Note: This event will arrive on a worker thread.
      // Before a windows control is directly updated, invoke to the main SynchronizationContext
      picBox.Invoke((Action)delegate
      {
          // Default image width and height in pixels for image retrieval

          int imageWidth = 0;
          int imageHeight = 0;
        CvsCogViewPort viewPort = _inSight?.ViewPort;

          if(viewPort != null)
          {
              imageWidth = viewPort.Width;
              imageHeight = viewPort.Height;
          }

          string url = _inSight.GetMainImageUrl(imageWidth, imageHeight);

        if (picBox.ImageLocation != url)
        {
          //picBox.ImageLocation = url;
          picBox.LoadAsync(url);
        }
        else
        {
          Task.Run(() => SendReady());
        }
      });
    }

    private async void SendReady()
    {
      if (!_inSight.LiveMode)
      {
        try
        {
          await _inSight.SendReady().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          Debug.WriteLine($"SendReady Exception: {ex.Message}");
        }
      }
    }

    /// <summary>
    /// This method provides an example of how to render the graphics
    /// over the picture box image.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void picBox_Paint(object sender, PaintEventArgs e)
    {
      Image img = (sender as PictureBox).Image;
      if (!IsDrawingEnabled)
      if (img == null)
      {
        e.Graphics.Clear(picBox.BackColor);
        return;
      }

      CvsCogViewPort viewPort = _inSight?.ViewPort;
      if (viewPort == null)
      {
        e.Graphics.Clear(picBox.BackColor);
        return;
      }

      int picWidth = e.ClipRectangle.Width;
      int picHeight = e.ClipRectangle.Height;
      if (img != null)
      {
        float xMax = img.Width;
        float yMax = img.Height;

        int startX = 0;
        int startY = 0;
        int endX = img.Width;
        int endY = img.Height;

        float imageRatio = xMax / (float)yMax; // image W:H ratio
        float containerRatio = picWidth / (float)picHeight; // container W:H ratio

        float scaleFactor;
        if (imageRatio >= containerRatio)
        {
          // horizontal image
          scaleFactor = picWidth / (float)xMax;
          float scaledHeight = yMax * scaleFactor;
          // calculate gap between top of container and top of image
          float filler = Math.Abs(picHeight - scaledHeight) / 2;
          startX = (int)(startX * scaleFactor);
          endX = (int)(endX * scaleFactor);
          startY = (int)((startY) * scaleFactor + filler);
          endY = (int)((endY) * scaleFactor + filler);
        }
        else
        {
          // vertical image
          scaleFactor = picHeight / (float)yMax;
          float scaledWidth = xMax * scaleFactor;
          float filler = Math.Abs(picWidth - scaledWidth) / 2;
          startX = (int)((startX) * scaleFactor + filler);
          endX = (int)((endX) * scaleFactor + filler);
          startY = (int)(startY * scaleFactor);
          endY = (int)(endY * scaleFactor);
        }

        const int resolutionFactor = 1; // Because we grab the image at 50% resolution
        scaleFactor = scaleFactor / resolutionFactor;

        // Get the size of the ViewPort that is scaled to 50% which is the number of pixels retrieved.
        CvsCogViewPort scaledViewPort = new CvsCogViewPort();
        scaledViewPort.Width = viewPort.Width / resolutionFactor;
        scaledViewPort.Height = viewPort.Height / resolutionFactor;

        // Adjust for a partially acquired image
        startY -= (int)(_inSight.ImageOffsetY * scaleFactor);

        // NOTE: Image is currently always centered
        DisplayContext dc = new DisplayContext(_usesXYCoordinates, scaleFactor, startX, startY, this.Bounds);

        Graphics gr = e.Graphics;

        if (!_inSight.LiveMode)
        {
          GraphicsHelper.DrawGraphics(gr, _graphics, dc);
        }

        JToken results = _inSight.Results;
        JToken token = results.SelectToken("queuedResult");
        if ((token != null) && (token.Value<Boolean>() == true))
        {
          DisplayQueuedResultBanner(gr);
        }
      }
    }

    private void DisplayQueuedResultBanner(Graphics gr)
    {
      RectangleF rect = new RectangleF(new PointF(0, 0), new SizeF(this.ClientRectangle.Width, 20));
      gr.FillRectangle(Brushes.Yellow, rect);

      Font font = new Font("Arial", 9);
      string label = "Showing Queued Result";
      SizeF strSize = gr.MeasureString(label, font);
      StringFormat sr = new StringFormat();
      sr.Alignment = StringAlignment.Center;
      sr.LineAlignment = StringAlignment.Center;
      gr.DrawString(label, font, Brushes.Black, rect, sr);
    }

    private async void picBox_LoadCompleted(object sender, AsyncCompletedEventArgs e)
    {
      try
      {
        _graphics = _nextGraphics;
        if (_inSight.Connected)
        {
          await _inSight.SendReady().ConfigureAwait(false);
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"SendReady Exception: {ex.Message}");
      }
    }

        private void grapic_Clear()
        {

        }
  }
}
