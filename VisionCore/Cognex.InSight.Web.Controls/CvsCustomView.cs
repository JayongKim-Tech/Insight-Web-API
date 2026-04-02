using Cognex.InSight.Remoting.Serialization;

namespace Cognex.InSight.Web.Controls
{
  public partial class CvsCustomView : CvsSpreadsheet
  {
    public CvsCustomView() : base(true)
    {
      InitializeComponent();
    }

    public override void InitSpreadsheet()
    {
      if (IsCustomView)
      {
        if (_inSight.CustomViewSettings?.Length > 0)
        {
          // This will only show the first custom view
          HmiCustomViewSettings cvSettings = _inSight.CustomViewSettings[0];
          if (cvSettings != null)
          {
            _cellRange = new CellRange(cvSettings.Top, cvSettings.Left, cvSettings.Bottom - cvSettings.Top + 1, cvSettings.Right - cvSettings.Left + 1);
          }
        }
      }
      base.InitSpreadsheet();
    }

    /// <summary>
    /// Sets the CvsInSight and cell range for this Spreadsheet.
    /// </summary>
    /// <param name="inSight"></param>
    /// <param name="range"></param>
    public override void SetInSight(CvsInSight inSight, CellRange range = null)
    {
      base.SetInSight(inSight, range);
    }
  }
}
