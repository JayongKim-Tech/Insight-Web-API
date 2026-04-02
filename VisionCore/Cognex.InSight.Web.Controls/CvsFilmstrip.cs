using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Cognex.InSight.Web.Controls
{
  public partial class CvsFilmstrip : UserControl
  {
    private CvsInSight _inSight;

    private bool _isFrozen = false;
    private int _statusStyle = 0;

    // The zero-based index of the queue slot that is selected
    private int _selectedSlotIndex = 0;

    private int _firstSlotID = -1;

    private int[] _statusMap = { 0, 1, 2, 3, 4, 5, 6, 5, 4, 3, 2, 1 };
    private int _statusMapIndex = 0;

    private int _buttonStartX = 60;

    private Bitmap _cellImage;
    private Bitmap[] _statusImages = new Bitmap[3];

    public CvsFilmstrip()
    {
      Assembly myAssembly = Assembly.GetExecutingAssembly();

      using (Stream myStream = myAssembly.GetManifestResourceStream("Cognex.InSight.Web.Controls.FilmstripCell.png"))
      {
        _cellImage = new Bitmap(myStream);
      }

      using (Stream myStream = myAssembly.GetManifestResourceStream("Cognex.InSight.Web.Controls.StatusGeo40.png"))
      {
        _statusImages[0] = new Bitmap(myStream);
      }
      using (Stream myStream = myAssembly.GetManifestResourceStream("Cognex.InSight.Web.Controls.StatusChk40.png"))
      {
        _statusImages[1] = new Bitmap(myStream);
      }
      using (Stream myStream = myAssembly.GetManifestResourceStream("Cognex.InSight.Web.Controls.StatusOk40.png"))
      {
        _statusImages[2] = new Bitmap(myStream);
      }

      InitializeComponent();
    }

    /// <summary>
    /// Repaint the control on resize.
    /// </summary>
    protected override void OnResize(EventArgs e)
    {
      this.Invalidate();
      base.OnResize(e);
    }

    /// <summary>
    /// Repaint the freeze button when enabled.
    /// </summary>
    protected override void OnEnabledChanged(EventArgs e)
    {
      btnFreeze.Invalidate();
      base.OnEnabledChanged(e);
    }

    /// <summary>
    /// Sets the CvsInSight for this control.
    /// </summary>                 
    public void SetInSight(CvsInSight inSight)
    {
      _inSight = inSight;
    }

    /// <summary>
    /// Updates the status icon style when first connected.
    /// </summary>
    public void OnConnected()
    {
      if (_inSight.Settings != null)
      {
        _statusStyle = Convert.ToInt32(_inSight.Settings["hmi.statusStyle"]);
      }
    }

    /// <summary>
    /// Repaint the control when the results are updated.
    /// </summary>
    public void UpdateResults()
    {
      this.Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
      // Determine if the rsystem result queue is frozen.
      bool isFrozen = false;
      JToken results = null;
      if (_inSight?.Results != null)
      {
        results = _inSight.Results;
        JToken token = results.SelectToken("rq.frozen");
        if ((token != null) && (token.Value<Boolean>() == true))
        {
          isFrozen = true;
        }
      }

      // If the state changed, then update the button text.
      if (isFrozen != _isFrozen)
      {
        btnFreeze.Text = isFrozen ? "Unfreeze" : "Freeze";
        _isFrozen = isFrozen;
      }

      if (_cellImage == null)
        return;

      Graphics gr = e.Graphics;
      gr.Clear(Color.Gray);

      const int cellWidth = 50;
      int numCells = (int)Math.Floor(((double)this.Width - (double)this._buttonStartX) / (double)cellWidth);

      // Draw the filmstrip background...
      for (int i = 0; i < numCells; i++)
      {
        gr.DrawImage(_cellImage, _buttonStartX + (cellWidth * i), 0, cellWidth, cellWidth);
      }

      if (results != null)
      {
        int filledSlots = 0;
        JToken token = results.SelectToken("rq.slots");
        if (token != null)
        {
          filledSlots = token.Value<int>();
        }
        else // No queue...
        {
          return;
        }

        if (filledSlots > 0)
        {
          int[] rqIds = results.SelectToken("rq.ids").ToObject<int[]>();
          int[] rqStatus = results.SelectToken("rq.status").ToObject<int[]>();

          // Only update the status map index (for intensity of the status icon), if the queue changed
          if (this._firstSlotID != rqIds[0])
          {
            this._firstSlotID = rqIds[0];

            this._statusMapIndex++;
            this._statusMapIndex %= this._statusMap.Length;
          }

          // Draw the status of each slot...
          const int statusWidth = 40;
          int startStatusMapIndex = this._statusMapIndex;

          int startIndex = 0;
          int numCellsToFill = filledSlots;
          if (filledSlots > numCells) // Cannot show the entire queue...
          {
            startIndex = filledSlots - numCells;
            numCellsToFill = numCells;
          }

          for (int i = 0; i < numCellsToFill; i++)
          {
            int slotStatusMapIndex = (startStatusMapIndex + i + startIndex) % this._statusMap.Length;
            int slotStatus = rqStatus[i + startIndex];

            RectangleF destRect = new RectangleF(this._buttonStartX + (cellWidth * i) + 5, 5, statusWidth, statusWidth);
            RectangleF srcRect = new RectangleF(this._statusMap[slotStatusMapIndex] * statusWidth, (slotStatus == 1) ? 0 : 80, statusWidth, statusWidth);
            gr.DrawImage(this._statusImages[_statusStyle], destRect, srcRect, GraphicsUnit.Pixel);
          }

          // Highlight the selected index, when the queue is frozen.
          int queueSlotIndex = _selectedSlotIndex - startIndex;
          if (isFrozen && queueSlotIndex >= 0 && queueSlotIndex < numCellsToFill)
          {
            gr.DrawRectangle(Pens.Yellow, this._buttonStartX + (cellWidth * queueSlotIndex) + 5, 5, statusWidth, statusWidth);
          }
        }
      }

      base.OnPaint(e);
    }

    private async void btnFreeze_Click(object sender, EventArgs e)
    {
      try
      {
        if (_inSight.Connected)
        {
          await _inSight.FreezeQueue(!_isFrozen);
          this.Invalidate();
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"btnFreeze_Click Exception: {ex.Message}");
      }
    }

    private async void CvsFilmstrip_MouseClick(object sender, MouseEventArgs e)
    {
      try
      {
        int x = e.X - _buttonStartX;
        int y = e.Y;

        // Index of the visible slot that was clicked
        int index = (int)Math.Floor((float)x / 50);

        JToken results = null;
        if (_inSight?.Results != null)
        {
          results = _inSight.Results;
        }

        if (results != null)
        {
          int filledSlots = 0;
          JToken token = results.SelectToken("rq.slots");
          if (token != null)
          {
            filledSlots = token.Value<int>();
          }

          if (filledSlots <= 0)
            return;

          // Freeze the queue to allow the slot to be selected
          if (!_isFrozen)
          {
            await _inSight.FreezeQueue(true);
          }

          // If we are showing the whole queue, then the start index isn't 0
          const int cellWidth = 50;
          int numCells = (int)Math.Floor((float)(this.ClientSize.Width - _buttonStartX) / (float)cellWidth);
          int startIndex = Math.Max(filledSlots - numCells, 0);
          index += startIndex;

          if ((index >= 0) && (index <= filledSlots))
          {
            await _inSight.SelectQueueSlot(index);
            _selectedSlotIndex = index;
          }

          this.Invalidate();
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"CvsFilmstrip_MouseClick Exception: {ex.Message}");
      }
    }
  }
}
