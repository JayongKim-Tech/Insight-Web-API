using System;
using System.Windows.Forms;
using Cognex.InSight.Remoting.Serialization;

namespace Cognex.InSight.Web.Controls
{
  public partial class CvsCustomViewSettingsDialog : Form
  {
    private HmiCustomViewSettings[] _settings;
    private int _currentSettingIndex;

    public HmiCustomViewSettings[] Settings 
    { 
      get 
      {
        persistCurrentSettings();
        return _settings; 
      } 
    }

    public CvsCustomViewSettingsDialog()
    {
      InitializeComponent();
    }

    public CvsCustomViewSettingsDialog(HmiCustomViewSettings[] settings)
    {
      _settings = settings;
      _currentSettingIndex = 0;
      InitializeComponent();
    }

    private void persistCurrentSettings()
    {
      if (_settings[_currentSettingIndex] != null)
      {
        _settings[_currentSettingIndex].FromCellRange(tbCellRange.Text);
        _settings[_currentSettingIndex].X = (int)numX.Value;
        _settings[_currentSettingIndex].Y = (int)numY.Value;
        _settings[_currentSettingIndex].Width = (int)numWidth.Value;
        _settings[_currentSettingIndex].Height = (int)numHeight.Value;
      }
    }

    private void CvsCustomViewSettingsDialog_Load(object sender, EventArgs e)
    {
      if (_settings?.Length > 0 && _settings[_currentSettingIndex] != null)
      {
        tbCellRange.Text = _settings[_currentSettingIndex].ToCellRange();
        numX.Value = _settings[_currentSettingIndex].X;
        numY.Value = _settings[_currentSettingIndex].Y;
        numWidth.Value = _settings[_currentSettingIndex].Width;
        numHeight.Value = _settings[_currentSettingIndex].Height;
      }
    }

    private void btnNext_Click(object sender, EventArgs e)
    {
      if (_settings?.Length > 0)
      {
        persistCurrentSettings();
        if (_currentSettingIndex >= _settings.Length-1)
        {
          _currentSettingIndex = 0;
        }
        else
        {
          _currentSettingIndex++;
        }
        CvsCustomViewSettingsDialog_Load(sender, e);
      }
    }
    private void btnPrev_Click(object sender, EventArgs e)
    {
      if (_settings?.Length > 0)
      {
        persistCurrentSettings();
        if (_currentSettingIndex <= 0)
        {
          _currentSettingIndex = _settings.Length-1;
        }
        else
        {
          _currentSettingIndex--;
        }
      }
      CvsCustomViewSettingsDialog_Load(sender, e);
    }
  }
}
