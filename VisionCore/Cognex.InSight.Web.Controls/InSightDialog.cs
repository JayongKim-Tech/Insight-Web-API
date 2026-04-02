using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using Cognex.InSight.Remoting.Serialization;
using Newtonsoft.Json.Linq;

namespace Cognex.InSight.Web.Controls
{
  public partial class InSightDialog : Form
  {
    private CvsInSight _inSight;
    private HmiDialogCell _cell;
    private bool _isCustomView;
    private CvsSpreadsheet _spreadsheet;

    public InSightDialog()
    {
      InitializeComponent();
    }

    public InSightDialog(CvsInSight inSight, HmiDialogCell cell, bool isCustomView)
    {
      _inSight = inSight;
      _cell = cell;
      _isCustomView = isCustomView;
      InitializeComponent();

      this.Text = _cell.Title;
            
      _spreadsheet = new CvsSpreadsheet(_isCustomView);

      _spreadsheet.AllowDrop = true;
      _spreadsheet.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
      _spreadsheet.BackColor = System.Drawing.SystemColors.ControlDark;
      _spreadsheet.Location = new System.Drawing.Point(10, 10);
      _spreadsheet.Margin = new System.Windows.Forms.Padding(0);
      _spreadsheet.Name = "inSightDialog";
      _spreadsheet.Size = new System.Drawing.Size(600,400);
            
      this.Controls.Add(_spreadsheet);
      
      inSight.ResultsChanged += InSight_ResultsChanged;
    }

    protected override void OnLoad(EventArgs e)
    {
      base.OnLoad(e);
      
      CellRange range = new CellRange(_cell.Range);

      _spreadsheet.SetInSight(_inSight, range);

      _spreadsheet.Invoke((Action)delegate
      {
        _spreadsheet.InitSpreadsheet();
        _spreadsheet.SizeToContents();
        // Account for border around the spreadsheet
        int xOffset = 20;
        int yOffset = 16 + btnOK.Height;
        this.ClientSize = new Size(_spreadsheet.Width + xOffset, _spreadsheet.Height + yOffset);
        btnOK.Location = new Point((this.Width / 2) - (btnOK.Width / 2), btnOK.Location.Y);
      });
    }

    protected override void OnClosing(CancelEventArgs e)
    {
      _inSight.ResultsChanged -= InSight_ResultsChanged;
      _spreadsheet.EndEdit();
      base.OnClosing(e);
    }

    private void InSight_ResultsChanged(object sender, EventArgs e)
    {
      JToken results = _inSight.Results;

      _spreadsheet.Invoke((Action)delegate
      {
        _spreadsheet.UpdateResults(results);
      });
    }

    private void btnOK_Click(object sender, EventArgs e)
    {
      Close();
    }
  }
}
