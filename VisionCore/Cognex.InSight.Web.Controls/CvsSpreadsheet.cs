using Cognex.InSight.Remoting.Serialization;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

using Cognex.InSight.Web;

namespace Cognex.InSight.Web.Controls
{
  public partial class CvsSpreadsheet : UserControl
  {
    protected CvsInSight _inSight;
    protected CellRange _cellRange = new CellRange();
    private bool _isCustomView = false;

    // Cells created by the HMI and not part of the actual In-Sight job
    private HmiSpreadsheetCells _hmiCells;

    public CvsSpreadsheet()
    {
      InitializeComponent();
    }

    /// <summary>
    /// Creates a new instance to display the spreadsheet.
    /// </summary>
    /// <param name="isCustomView">A flag to display the sheet using custom view styling</param>
    public CvsSpreadsheet(bool isCustomView)
    {
      _isCustomView = isCustomView;
      InitializeComponent();
    }

    /// <summary>
    /// Sets the CvsInSight for this Spreadsheet.
    /// </summary>
    /// <param name="inSight"></param>
    public virtual void SetInSight(CvsInSight inSight, CellRange range = null)
    {
      _inSight = inSight;
      if (range != null)
      {
        _cellRange = range;
      }
    }

    public bool IsCustomView
    {
      get
      {
        return _isCustomView;
      }
    }
    
    /// <summary>
    /// Initialize/clear the spreadsheet.
    /// </summary>
    public virtual void InitSpreadsheet()
    {
      // Format the Spreadsheet
      gridView.InitGrid(_inSight, _cellRange, IsCustomView);
      if (IsCustomView)
      {
        gridView.AllowUserToResizeRows = false;
        gridView.AllowUserToResizeColumns = false;
      }

      gridView.EditingControlShowing += new DataGridViewEditingControlShowingEventHandler(gridView_EditingControlShowing);

      // Display the current results, if any
      if (_inSight?.Results != null)
      {
        UpdateResults(_inSight.Results);
      }
    }

    public void SizeToContents()
    {
      int width, height;
      gridView.GetSizeOfContents(out width, out height);
      Size sz = new Size(width, height);
      this.Size = sz; 
    }

    public void EndEdit()
    {
      // Change cell focus to force any changed value to be applied
      gridView.CurrentCell = gridView[(gridView.CurrentCell.ColumnIndex + 1) % gridView.ColumnCount, (gridView.CurrentCell.RowIndex + 1) % gridView.RowCount];
    }
    
    public void SetHmiSpreadsheetCells(HmiSpreadsheetCells hmiCells)
    {
      _hmiCells = hmiCells;
      UpdateHmiCells();
    }

    private void UpdateHmiCells()
    {
      gridView.Invoke((Action)delegate
      {
        foreach (HmiSpreadsheetCell cell in _hmiCells.Cells)
        {
          int rowIndex, colIndex;
          if (HmiCellResult.LocationParse(cell.Location, out rowIndex, out colIndex))
          {
            if (_cellRange.Contains(rowIndex, colIndex))
            {
              int row = _isCustomView ? rowIndex - 1 : rowIndex;

              if (cell is HmiBitmapCell)
              {
                string fileName = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_hmiCells.FilePath), ((HmiBitmapCell)cell).File);
                Bitmap bmp = Bitmap.FromFile(fileName) as Bitmap;
               
                InSightBitmapCell iCell = new InSightBitmapCell();
                iCell.SetBitmap(bmp);
                gridView[colIndex + 1, row] = iCell;
                gridView[colIndex + 1, row].Tag = "HMI"; // Prevent any result from the camera from overwriting this cell.   
              }
              else if (cell is HmiDialogCell)
              {
                HmiDialogButtonCell bCell = new HmiDialogButtonCell(_inSight, (HmiDialogCell)cell, IsCustomView);
                bCell.FlatStyle = FlatStyle.Popup;
                gridView[colIndex + 1, row] = bCell;
                gridView[colIndex + 1, row].Style.ForeColor = Color.Navy;
                gridView[colIndex + 1, row].Value = "\U0001F5D4" + ((HmiDialogCell)cell).Label;
                gridView[colIndex + 1, row].Tag = "HMI";
              }
              else if (cell is HmiWizardCell)
              {
                HmiWizardButtonCell bCell = new HmiWizardButtonCell(_inSight, (HmiWizardCell)cell, IsCustomView, _hmiCells);
                bCell.FlatStyle = FlatStyle.Popup;
                gridView[colIndex + 1, row] = bCell;
                gridView[colIndex + 1, row].Style.ForeColor = Color.Navy;
                gridView[colIndex + 1, row].Value = "\U0001F5D7" + ((HmiWizardCell)cell).Label;
                gridView[colIndex + 1, row].Tag = "HMI";
              }
            }
          }
        }
      });
    }

    private void gridView_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
    {
      ComboBox combo = e.Control as ComboBox;
      if (combo != null)
      {
        // Remove an existing event-handler, if present, to avoid 
        // adding multiple handlers when the editing control is reused.
        combo.SelectedIndexChanged -=
            new EventHandler(ComboBox_SelectedIndexChanged);

        // Add the event handler. 
        combo.SelectedIndexChanged +=
            new EventHandler(ComboBox_SelectedIndexChanged);
      }
    }

    private void ComboBox_SelectedIndexChanged(object sender, EventArgs e)
    {
      if (!_inSight.Connected)
        return;

      InSightListBoxCell inSightListBox = ((System.Windows.Forms.DataGridViewComboBoxEditingControl)sender).EditingControlDataGridView.CurrentCell as InSightListBoxCell;
      if (inSightListBox != null)
      {
        (sender as ComboBox).SelectedIndexChanged -=
              new EventHandler(ComboBox_SelectedIndexChanged);
              
        int selectedIndex = (sender as ComboBox).SelectedIndex;
        if (selectedIndex >= 0)
        {
          inSightListBox.SetValue(selectedIndex);
        }
        
        // Re-connect the handler
        (sender as ComboBox).SelectedIndexChanged +=
              new EventHandler(ComboBox_SelectedIndexChanged);
              
      }
    }

    /// <summary>
    /// Update the spreadsheet with the latest results.
    /// </summary>
    /// <param name="results"></param>
    public void UpdateResults(JToken results)
    {
      gridView.Invoke((Action)delegate
      {
        gridView.UpdateGrid(results, _inSight, _cellRange, IsCustomView);
      });
    }
    
    private async void gridView_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
    {
      string cellLocation = string.Format("{0}{1}", (char)('A' + (e.ColumnIndex - 1)), e.RowIndex);
      string value;

      if (!_inSight.Connected || _inSight.Online || _inSight.EditorAttached || IsCustomView)
        return;

      // Only admins should be able to set an expression.
      if (_inSight.AccessLevel.ToLower() != "full")
        return;

      try
      {
        // Get the current expression...
        value = await _inSight.GetCellExpression(cellLocation);

        if (InputBox.Show("Enter Expression", "Expression:", ref value) == DialogResult.OK)
        {
          if (!_inSight.Connected)
            return;

          await _inSight.SetCellExpression(cellLocation, value);
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine("gridView_CellDoubleClick:" + ex.Message);
        MessageBox.Show("Unable to set Expression.", "Error");
      }
    }
  }
}
