// Copyright (c) 2022 Cognex Corporation. All Rights Reserved

using Cognex.InSight.Remoting.Serialization;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace Cognex.InSight.Web.Controls
{
  public class CellRange
  {
    private int _firstCol = 0;
    private int _numCols = 26;
    private int _firstRow = 0;
    private int _numRows = 600;

    public int FirstRow { get { return _firstRow; } }
    public int NumRows { get { return _numRows; } }
    public int FirstCol { get { return _firstCol; } }
    public int NumCols { get { return _numCols; } }

    public CellRange()
    {
    }

    public CellRange(int firstRow, int firstCol, int numRows, int numCols)
    {
      _firstCol = firstCol;
      _numCols = numCols;
      _firstRow = firstRow;
      _numRows = numRows;
    }

    public CellRange(string range)
    {
      string [] strRange = range.Split(new char[1] { ':' });
      HmiCellResult.LocationParse(strRange[0], out _firstRow, out _firstCol);

      int lastRow, lastCol;
      HmiCellResult.LocationParse(strRange[1], out lastRow, out lastCol);
      _numRows = lastRow - _firstRow + 1;
      _numCols = lastCol - _firstCol + 1;
    }

     public bool Contains(int row, int col)
    {
      return (row >= _firstRow) && (col >= FirstCol) && (row < (_firstRow + _numRows)) && (col < (_firstCol + _numCols));
    }
  }

  /// <summary>
  /// Provides externsion methods for a simple spreadsheet display of In-Sight results in a DataGridView.
  /// </summary>
  public static class SpreadsheetGridViewExtensions
  {
    public static TextFormatFlags ConvertToTextFormatFlags(DataGridViewContentAlignment alignment)
    {
      switch (alignment)
      {
        case DataGridViewContentAlignment.TopLeft:
          return TextFormatFlags.Top | TextFormatFlags.Left;
        case DataGridViewContentAlignment.TopCenter:
          return TextFormatFlags.Top | TextFormatFlags.HorizontalCenter;
        case DataGridViewContentAlignment.TopRight:
          return TextFormatFlags.Top | TextFormatFlags.Right;
        case DataGridViewContentAlignment.MiddleLeft:
          return TextFormatFlags.VerticalCenter | TextFormatFlags.Left;
        case DataGridViewContentAlignment.MiddleCenter:
          return TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter;
        case DataGridViewContentAlignment.MiddleRight:
          return TextFormatFlags.VerticalCenter | TextFormatFlags.Right;
        case DataGridViewContentAlignment.BottomLeft:
          return TextFormatFlags.Bottom | TextFormatFlags.Left;
        case DataGridViewContentAlignment.BottomCenter:
          return TextFormatFlags.Bottom | TextFormatFlags.HorizontalCenter;
        case DataGridViewContentAlignment.BottomRight:
          return TextFormatFlags.Bottom | TextFormatFlags.Right;
      }

      return TextFormatFlags.Default;
    }

    public static bool CellHasAlignment(CvsInSight inSight, string cellLocation)
    {
      HmiCellFormat[] cellFmt = inSight?.SheetFormat?.CellFormats;
      if (cellFmt == null)
        return false;

      for (int i = 0; i < cellFmt.Length; i++)
      {
        HmiCellFormat fmt = cellFmt[i];
        if (fmt.Location == cellLocation)
        {
          // Currently, both must be set to render correctly in this grid.
          return ((fmt.TextAlign != null) && (fmt.VerticalAlign != null));
        }
      }

      return false;
    }

    public static void HandleError(string caption, string description)
    {
      MessageBox.Show(description, caption);
    }

    public static void DoubleBufferedGrid(this DataGridView dgv, bool setting)
    {
      Type dgvType = dgv.GetType();
      PropertyInfo pi = dgvType.GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
      pi.SetValue(dgv, setting, null);
    }

    public static void InitGrid(this DataGridView dgv, CvsInSight inSight, CellRange cellRange, bool isCustomView = false)
    {
      DoubleBufferedGrid(dgv, true);

      DataTable _dt;
      _dt = new DataTable();

      dgv.EnableHeadersVisualStyles = false; // Use the designated background color, etc
      dgv.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
      dgv.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
      dgv.DataSource = _dt;

      _dt.Columns.Add(" "); // Empty column header above row header column
      dgv.Columns[0].CellTemplate.Style.ForeColor = Color.Black;
      dgv.Columns[0].CellTemplate.Style.BackColor = SystemColors.Control;
      dgv.Columns[0].CellTemplate.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
      dgv.ReadOnly = false;

      if (isCustomView)
      {
        dgv.Columns[0].Visible = false;
      }

      int[] colWidths = new int[0];
      int[] rowHeights = new int[0];
      if (inSight != null)
      {
        colWidths = inSight.SheetFormat.ColumnWidths;
        rowHeights = inSight.SheetFormat.RowHeights;
      }
      
      for (int col = cellRange.FirstCol; col < cellRange.FirstCol + cellRange.NumCols; col++)
      {
        string header = ((char)(((int)'A') + col)).ToString();
        _dt.Columns.Add(header);
        dgv.Columns[col - cellRange.FirstCol + 1].SortMode = DataGridViewColumnSortMode.NotSortable;
        dgv.Columns[col - cellRange.FirstCol + 1].ReadOnly = false;
        dgv.Columns[col - cellRange.FirstCol + 1].Width = ((colWidths.Length > col) && (colWidths[col] > 0)) ? colWidths[col] : HmiCustomViewSettings.DefaultWidth;
        dgv.Columns[col - cellRange.FirstCol + 1].CellTemplate.Style.Alignment = DataGridViewContentAlignment.TopLeft;
      }

      for (int row = cellRange.FirstRow; row < cellRange.FirstRow + cellRange.NumRows; row++)
      {
        DataRow dataRow = _dt.NewRow();
        for (int col = cellRange.FirstCol + 1; col <= cellRange.FirstCol + cellRange.NumCols; col++)
        {
          dataRow[col - cellRange.FirstCol] = "";
        }
        _dt.Rows.Add(row);
        dgv.Rows[row - cellRange.FirstRow].Height = ((rowHeights.Length > row) && (rowHeights[row] > 0)) ? rowHeights[row] : HmiCustomViewSettings.DefaultHeight;
      }

      // Set the fixed-width row header
      dgv.Columns[0].Width = dgv.Columns[0].Visible ? 30 : 0;

      // Determine which scroll bars should be displayed, if any
      if ((inSight != null) && inSight.Connected)
      {
        var totalHeight = dgv.Rows.GetRowsHeight(DataGridViewElementStates.None);
        var totalWidth = dgv.Columns.GetColumnsWidth(DataGridViewElementStates.None) - dgv.Columns[0].Width;
        if ((totalHeight > dgv.Height) && (totalWidth > dgv.Width))
          dgv.ScrollBars = ScrollBars.Both;
        else if (totalHeight > dgv.Height)
          dgv.ScrollBars = ScrollBars.Vertical;
        else if (totalWidth > dgv.Width)
          dgv.ScrollBars = ScrollBars.Horizontal;
        else
          dgv.ScrollBars = ScrollBars.None;
      }

      // Process cell formats...
      if (inSight != null)
      {
        HmiCellFormat [] cellFmt = inSight.SheetFormat.CellFormats;
        if (cellFmt != null)
        {
          for (int i = 0; i < cellFmt.Length; i++)
          {
            HmiCellFormat fmt = cellFmt[i];
            int rowIndex, colIndex;
            if (HmiCellResult.LocationParse(fmt.Location, out rowIndex, out colIndex))
            {
              if (((colIndex - cellRange.FirstCol) < 0) || ((rowIndex - cellRange.FirstRow) < 0) ||
                  ((colIndex - cellRange.FirstCol) >= cellRange.NumCols) || ((rowIndex - cellRange.FirstRow) >= cellRange.NumRows))
                continue;

              Nullable<Color> cellBackColor = fmt.CellBackColor;
              if (cellBackColor.HasValue)
              {
                dgv[1 + colIndex - cellRange.FirstCol, rowIndex - cellRange.FirstRow].Style.BackColor = cellBackColor.Value;
              }
              else
              {
                dgv[1 + colIndex - cellRange.FirstCol, rowIndex - cellRange.FirstRow].Style.BackColor = SystemColors.ControlLight;
              }

              Nullable<Color> cellForeColor = fmt.CellForeColor;
              if (cellForeColor.HasValue)
              {
                dgv[1 + colIndex - cellRange.FirstCol, rowIndex - cellRange.FirstRow].Style.ForeColor = cellForeColor.Value;
              }
              else
              {
                dgv[1 + colIndex - cellRange.FirstCol, rowIndex - cellRange.FirstRow].Style.ForeColor = Color.Navy;
              }

              Font cellFont = fmt.CellFont;
              if (cellFont != null)
              {
                dgv[1 + colIndex - cellRange.FirstCol, rowIndex - cellRange.FirstRow].Style.Font = cellFont;
              }
              else
              {
                dgv[1 + colIndex - cellRange.FirstCol, rowIndex - cellRange.FirstRow].Style.Font = dgv.Font;
              }

              string textAlign = fmt.TextAlign;
              int textAlignIndex = -1;
              if (textAlign != null)
              {
                switch (textAlign)
                {
                  case "left":
                    textAlignIndex = 0;
                    break;
                  case "right":
                    textAlignIndex = 1;
                    break;
                  case "center":
                    textAlignIndex = 2;
                    break;
                }
              }

              string verticalAlign = fmt.VerticalAlign;
              int verticalAlignIndex = -1;
              if (verticalAlign != null)
              {
                switch (verticalAlign)
                {
                  case "bottom":
                    verticalAlignIndex = 0;
                    break;
                  case "top":
                    verticalAlignIndex = 1;
                    break;
                  case "middle":
                    verticalAlignIndex = 2;
                    break;
                }
              }

              // Note: This code only support having both the horizontal and vertical set (or neither)
              if ((textAlignIndex != -1) && (verticalAlignIndex != -1))
              {
                int mapIndex = textAlignIndex * 3 + verticalAlignIndex;
                DataGridViewContentAlignment[] map = new DataGridViewContentAlignment[9] { 
                                              DataGridViewContentAlignment.BottomLeft, DataGridViewContentAlignment.TopLeft, DataGridViewContentAlignment.MiddleLeft, // 'left' Row
                                              DataGridViewContentAlignment.BottomRight, DataGridViewContentAlignment.TopRight, DataGridViewContentAlignment.MiddleRight, // 'right' Row
                                              DataGridViewContentAlignment.BottomCenter, DataGridViewContentAlignment.TopCenter, DataGridViewContentAlignment.MiddleCenter, // 'center' Row
                                            };
                dgv[1 + colIndex - cellRange.FirstCol, rowIndex - cellRange.FirstRow].Style.Alignment = map[mapIndex];
              }
              else
              {
                dgv[1 + colIndex - cellRange.FirstCol, rowIndex - cellRange.FirstRow].Style.Alignment = DataGridViewContentAlignment.NotSet;
              }
            }
          }
        }
      }

      if (isCustomView)
      {
        // Hides the spreadsheet headers and cell borders so that it will look like a Custom View.
        dgv.CellBorderStyle = DataGridViewCellBorderStyle.None;
        dgv.ColumnHeadersVisible = false;
      }
    }

    public static void GetSizeOfContents(this DataGridView dgv, out int width, out int height)
    {
      width = 0;
      height = 0;
      foreach (DataGridViewColumn col in dgv.Columns)
      {
        if (col.Visible)
          width += col.Width;
      }
      foreach (DataGridViewRow row in dgv.Rows)
      {
        if (row.Visible)
          height += row.Height;
      }

      if (dgv.ColumnHeadersVisible)
      {
        height += dgv.ColumnHeadersHeight;
      }

      height++;
    }

    public static void CopyGridViewStyle(DataGridViewCell fromCell, DataGridViewCell toCell)
    {
      if (!fromCell.Style.BackColor.IsEmpty)
        toCell.Style.BackColor = fromCell.Style.BackColor;

      if (!fromCell.Style.ForeColor.IsEmpty)
        toCell.Style.ForeColor = fromCell.Style.ForeColor;

      // Prevent invisible text...
      if (toCell.Style.ForeColor == toCell.Style.BackColor)
        toCell.Style.ForeColor = Color.Navy;

      toCell.Style.Font = fromCell.Style.Font;
      toCell.Style.Alignment = fromCell.Style.Alignment;
    }

    public static void UpdateGrid(this DataGridView dgv, JToken results, CvsInSight inSight, CellRange cellRange, bool isCustomView = false)
    {
      if (results == null)
        return;

      int resultId = Convert.ToInt32(results["id"]);

      JArray cellsObj = results["cells"] as JArray;
      if (cellsObj == null)
        return;

      int numCells = cellsObj.Count;

      HmiCellResult[] cells = new HmiCellResult[numCells];
      for (int index = 0; index < numCells; index++)
      {
        string cellObj = cellsObj[index].ToString();
        HmiCellResult cell = CvsInSight.JsonSerializer.DeserializeObject(cellObj) as HmiCellResult;
        cells[index] = cell;
      }

      dgv.Invoke((Action)delegate
      {
        foreach (var cell in cells)
        {
          if (cell == null)
            continue;

          string cellLocation = cell.Location;
          if (cellLocation.Length <= 0)
            continue;

          int rowIndex, colIndex;
          if (!HmiCellResult.LocationParse(cellLocation, out rowIndex, out colIndex))
            continue;

          if ((rowIndex >= cellRange.FirstRow) && (rowIndex < cellRange.FirstRow + cellRange.NumRows) && (colIndex >= cellRange.FirstCol) && (colIndex < cellRange.FirstCol + cellRange.NumCols))
          {
            // Adjust to the custom view/dialog range...
            rowIndex -= cellRange.FirstRow;
            colIndex -= cellRange.FirstCol;
          }
          else
            continue; // Not in range

          // Skip any cells that are defined in the HMI
          if ((dgv[colIndex + 1, rowIndex].Tag as string) == "HMI")
            continue;

          // Account for the column header
          int cellColIndex = colIndex + 1;

          if ((cell is HmiFloatResult) || (cell is FloatResult))
          {
            float fValue = (float)Convert.ToDouble(cell.Value);
            if (!(dgv[cellColIndex, rowIndex] is InSightNumberCell))
            {
              InSightNumberCell numCell = new InSightNumberCell();
              CopyGridViewStyle(dgv[cellColIndex, rowIndex], numCell);
              // Must change the default cell alignment for numeric cells
              if (!CellHasAlignment(inSight, cellLocation))
              {
                numCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
              }

              dgv[cellColIndex, rowIndex] = numCell;
            }
            dgv[cellColIndex, rowIndex].Value = fValue;
          }
          else if ((cell is HmiEditFloatResult) || (cell is EditFloatResult))
          {
            string cellValueAsText = cell.Value.ToString();
            if (!(dgv[cellColIndex, rowIndex] is InSightEditNumberCell))
            {
              InSightEditNumberCell editCell = new InSightEditNumberCell(inSight, cellLocation);
              CopyGridViewStyle(dgv[cellColIndex, rowIndex], editCell);
              // Must change the default cell alignment for numeric cells
              if (!CellHasAlignment(inSight, cellLocation))
              {
                editCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
              }
              dgv[cellColIndex, rowIndex] = editCell;
            }
            float min = (cell is HmiEditFloatResult) ? ((HmiEditFloatResult)cell).Min : ((EditFloatResult)cell).Min;
            float max = (cell is HmiEditFloatResult) ? ((HmiEditFloatResult)cell).Max : ((EditFloatResult)cell).Max;
            (dgv[cellColIndex, rowIndex] as InSightEditNumberCell).SetMinMax((decimal)min, (decimal)max);
            dgv[cellColIndex, rowIndex].ReadOnly = cell.Disabled;
            dgv[cellColIndex, rowIndex].Value = cell.Value;
          }
          else if ((cell is HmiEditIntResult) || (cell is EditIntResult))
          {
            string cellValueAsText = cell.Value.ToString();
            if (!(dgv[cellColIndex, rowIndex] is InSightEditNumberCell))
            {
              InSightEditNumberCell editCell = new InSightEditNumberCell(inSight, cellLocation, false);
              CopyGridViewStyle(dgv[cellColIndex, rowIndex], editCell);
              // Must change the default cell alignment for numeric cells
              if (!CellHasAlignment(inSight, cellLocation))
              {
                editCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
              }
              dgv[cellColIndex, rowIndex] = editCell;
            }
            int min = (cell is HmiEditIntResult) ? ((HmiEditIntResult)cell).Min : ((EditIntResult)cell).Min;
            int max = (cell is HmiEditIntResult) ? ((HmiEditIntResult)cell).Max : ((EditIntResult)cell).Max;
            (dgv[cellColIndex, rowIndex] as InSightEditNumberCell).SetMinMax(min, max);
            dgv[cellColIndex, rowIndex].ReadOnly = cell.Disabled;
            dgv[cellColIndex, rowIndex].Value = cell.Value;
          }
          else if ((cell is HmiStringResult) || (cell is StringResult))
          {
            string cellValueAsText = cell.Value.ToString();
            if (!(dgv[cellColIndex, rowIndex] is InSightTextCell))
            {
              InSightTextCell textCell = new InSightTextCell();
              CopyGridViewStyle(dgv[cellColIndex, rowIndex], textCell);
              dgv[cellColIndex, rowIndex] = textCell;
            }
            dgv[cellColIndex, rowIndex].Value = cellValueAsText;
          }
          else if ((cell is HmiEditStringResult) || (cell is EditStringResult))
          {
            string cellValueAsText = cell.Value.ToString();
            if (!(dgv[cellColIndex, rowIndex] is InSightEditStringCell))
            {
              InSightEditStringCell editCell = new InSightEditStringCell(inSight, cellLocation);
              editCell.MaxInputLength = ((HmiEditStringResult)cell).MaxLength;
              editCell.ReadOnly = false;
              CopyGridViewStyle(dgv[cellColIndex, rowIndex], editCell);
              dgv[cellColIndex, rowIndex] = editCell;
            }
            dgv[cellColIndex, rowIndex].ReadOnly = cell.Disabled;
            dgv[cellColIndex, rowIndex].Value = cellValueAsText;
          }
          else if ((cell is HmiButtonResult) || (cell is ButtonResult))
          {
            string cellValueAsText = cell.Value.ToString();
            InSightButtonCell buttonCell;
            if (!(dgv[cellColIndex, rowIndex] is InSightButtonCell))
            {
              buttonCell = new InSightButtonCell(inSight, cellLocation);
              buttonCell.FlatStyle = FlatStyle.Flat;
              CopyGridViewStyle(dgv[cellColIndex, rowIndex], buttonCell);
              dgv[cellColIndex, rowIndex] = buttonCell;
            }
            else
            {
              buttonCell = dgv[cellColIndex, rowIndex] as InSightButtonCell;
            }
            buttonCell.SetCell(cell as HmiButtonResult);
            buttonCell.ReadOnly = cell.Disabled;
            dgv[cellColIndex, rowIndex].Value = ((HmiButtonResult)cell).Caption;
          }
          else if ((cell is HmiCheckBoxResult) || (cell is CheckBoxResult))
          {
            InSightCheckBoxCell checkBoxCell;
            if (!(dgv[cellColIndex, rowIndex] is InSightCheckBoxCell))
            {
              checkBoxCell = new InSightCheckBoxCell(inSight, cellLocation);
              CopyGridViewStyle(dgv[cellColIndex, rowIndex], checkBoxCell);
              checkBoxCell.SetText(((HmiCheckBoxResult)cell).Caption);
              // Must change the default cell alignment for checkbox cells
              if (!CellHasAlignment(inSight, cellLocation))
              {
                checkBoxCell.Style.Alignment = DataGridViewContentAlignment.MiddleLeft;
              }
              dgv[cellColIndex, rowIndex] = checkBoxCell;
            }
            else
            {
              checkBoxCell = dgv[cellColIndex, rowIndex] as InSightCheckBoxCell;
            }
            checkBoxCell.ReadOnly = cell.Disabled;
            checkBoxCell.Value = Convert.ToBoolean(cell.Value);
          }
          else if ((cell is HmiListBoxResult) || (cell is ListBoxResult))
          {
            InSightListBoxCell listBox;
            if (!(dgv[cellColIndex, rowIndex] is InSightListBoxCell))
            {
              listBox = new InSightListBoxCell(inSight, cellLocation);
              CopyGridViewStyle(dgv[colIndex + 1, rowIndex], listBox);
              dgv[colIndex + 1, rowIndex] = listBox;
            }
            else
            {
              listBox = dgv[cellColIndex, rowIndex] as InSightListBoxCell;
            }
            string[] options = ((HmiListBoxResult)cell).Options;
            int selectedIndex = Convert.ToInt32(cell.Value);
            listBox.SetSelectedIndex(selectedIndex);
            listBox.ReadOnly = cell.Disabled;

            if (!listBox.IsInEditMode) // Don't update the value when editing
            {
              List<string> items = new List<string>();
              items.AddRange(options);
              listBox.DataSource = items;
              listBox.Value = options[selectedIndex];
            }
          }
          else if ((cell is HmiStatusResult) || (cell is StatusResult))
          {
            InSightStatusCell statusCell;
            if (!(dgv[cellColIndex, rowIndex] is InSightStatusCell))
            {
              statusCell = new InSightStatusCell();
              CopyGridViewStyle(dgv[colIndex + 1, rowIndex], statusCell);
              dgv[cellColIndex, rowIndex] = statusCell;
            }
            else
            {
              statusCell = dgv[cellColIndex, rowIndex] as InSightStatusCell;
            }

            Color clr = (cell is StatusResult) ? Color.Transparent : Color.FromArgb((int)(((HmiStatusResult)cell).Color));
            string caption = (cell is StatusResult) ? ((StatusResult)cell).Caption : ((HmiStatusResult)cell).Caption;
            statusCell.SetCell(Convert.ToInt32(cell.Value), caption, clr, (cell is StatusResult));
            statusCell.Value = cell.Value;
          }
          else if ((cell is HmiMultiStatusResult) || (cell is MultiStatusResult))
          {
            InSightMultiStatusCell statusCell;
            if (!(dgv[cellColIndex, rowIndex] is InSightMultiStatusCell))
            {
              statusCell = new InSightMultiStatusCell();
              CopyGridViewStyle(dgv[cellColIndex, rowIndex], statusCell);
              dgv[cellColIndex, rowIndex] = statusCell;
            }
            else
            {
              statusCell = dgv[cellColIndex, rowIndex] as InSightMultiStatusCell;
            }

            statusCell.SetCell(cell as HmiMultiStatusResult);
            statusCell.Value = cell.Value;
          }
          else if ((cell is HmiStatusLightResult) || (cell is StatusLightResult))
          {
            InSightStatusCell statusCell;
            if (!(dgv[cellColIndex, rowIndex] is InSightStatusCell))
            {
              statusCell = new InSightStatusCell();
              CopyGridViewStyle(dgv[cellColIndex, rowIndex], statusCell);
              dgv[cellColIndex, rowIndex] = statusCell;
            }
            else
            {
              statusCell = dgv[cellColIndex, rowIndex] as InSightStatusCell;
            }

            statusCell.SetCell(Convert.ToInt32(cell.Value), ((HmiStatusLightResult)cell).Caption, Color.FromArgb((int)(((HmiStatusLightResult)cell).Color)), false);
            statusCell.Value = cell.Value;
          }
          else if ((cell is HmiColorLabelResult) || (cell is ColorLabelResult))
          {
            string cellValueAsText = cell.Value.ToString();
            dgv[cellColIndex, rowIndex].Value = cellValueAsText;
            dgv[cellColIndex, rowIndex].Style.ForeColor = Color.FromArgb((int)(((HmiColorLabelResult)cell).ForeColor));
            dgv[cellColIndex, rowIndex].Style.BackColor = Color.FromArgb((int)(((HmiColorLabelResult)cell).BackColor));
          }

          if (cell.Disabled)
          {
            if (dgv[cellColIndex, rowIndex].Style.ForeColor != Color.Gray)
              dgv[cellColIndex, rowIndex].Tag = dgv[cellColIndex, rowIndex].Style.ForeColor;

            dgv[cellColIndex, rowIndex].Style.ForeColor = Color.Gray;
          }
          else
          {
            // If the cell is re-enabled, then reset the color
            if (dgv[cellColIndex, rowIndex].Tag is Color)
            {
              dgv[cellColIndex, rowIndex].Style.ForeColor = (Color)dgv[cellColIndex, rowIndex].Tag;
            }
          }
        }
      });
    }
  }

  // Custom ButtonCell
  public class InSightButtonCell : DataGridViewButtonCell
  {
    private CvsInSight _inSight;
    private string _cellLocation;
    private HmiButtonResult _cell;

    public InSightButtonCell(CvsInSight inSight, string cellLocation)
    {
      _inSight = inSight;
      _cellLocation = cellLocation;
    }

    public void SetCell(HmiButtonResult value)
    {
      _cell = value;
    }

    protected async override void OnClick(DataGridViewCellEventArgs e)
    {
      try
      {
        if (_cell.Disabled)
          return;

        object value = _cell.Value;
        if (value is long) // Must be a push button
        {
          await _inSight.SetCellValue(_cellLocation, "1");
        }
        else
        {
          try
          {
            // Start the edit graphics operation
            await _inSight.BeginEdit(_cellLocation);

            JToken token = JToken.FromObject(value);
            string[] nonEditableFields = new string[] { "color", "font", "fontSize", "graphicId", "label", "lineThickness", "name", "runtimeEditable", "source", 
                "showAxesLabels", "showScanLine", "showXArrow", "showYArrow" };
            SerializationHelper.RemoveFields(token, nonEditableFields);
            string strValue = token.ToString();
            // Try to edit the shape...
            if ((value is CvsCogRegion) || (value is CvsCogCircle) || (value is CvsCogLine) ||
                (value is CvsCogPoint) || (value is CvsCogAnnulus) || (value is CvsCogPolygon) ||
                (value is CvsCogPolylinePath) || (value is CvsCogCompositeRegion) || (value is CvsCogMaskedRegion))
            {
              if (InputBox.Show("Enter Shape", "Value:", ref strValue) == DialogResult.OK)
              {
                object obj = CvsInSight.JsonSerializer.DeserializeObject(strValue);
                await _inSight.SetCellValue(_cellLocation, obj);
              }
            }
          }
          finally 
          {
            // End the graphic editing operation.
            await _inSight.EndEdit();
          }
        }
      }
      catch (Exception)
      {
        SpreadsheetGridViewExtensions.HandleError("Error", "Unable to set value.");
      }

      base.OnClick(e);
    }

    protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex, DataGridViewElementStates elementState, object value, object formattedValue, string errorText, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
    {
      advancedBorderStyle = new DataGridViewAdvancedBorderStyle();
      advancedBorderStyle.All = DataGridViewAdvancedCellBorderStyle.Outset;
      base.Paint(graphics, clipBounds, cellBounds, rowIndex, elementState, value, formattedValue, errorText, cellStyle, advancedBorderStyle, paintParts);
    }
  }

  // Custom CheckBoxCell
  public class InSightCheckBoxCell : DataGridViewCheckBoxCell
  {
    private CvsInSight _inSight;
    private string _cellLocation;
    private string _text = "";

    public InSightCheckBoxCell(CvsInSight inSight, string cellLocation)
    {
      this.FlatStyle = FlatStyle.Popup;

      _inSight = inSight;
      _cellLocation = cellLocation;
    }

    public void SetText(string text)
    {
      _text = text;
    }

    protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex, DataGridViewElementStates elementState, object value, object formattedValue, string errorText, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
    {
      base.Paint(graphics, clipBounds, cellBounds, rowIndex,
             elementState, value, formattedValue, errorText, cellStyle,
             advancedBorderStyle, paintParts);

      int checkBoxWidth = 14;
      Rectangle textBounds = new Rectangle(cellBounds.X + checkBoxWidth, cellBounds.Y + 1, cellBounds.Width - checkBoxWidth, cellBounds.Height - 2);
      TextRenderer.DrawText(graphics, _text, cellStyle.Font, textBounds, cellStyle.ForeColor, SpreadsheetGridViewExtensions.ConvertToTextFormatFlags(cellStyle.Alignment));
    }

    protected async override void OnClick(DataGridViewCellEventArgs e)
    {
      try
      {
        if (!this.ReadOnly)
        {
          await _inSight.SetCellValue(_cellLocation, ((bool)this.FormattedValue) ? 0 : 1);
        }
      }
      catch (Exception)
      {
        SpreadsheetGridViewExtensions.HandleError("Error", "Unable to set value.");
      }

      base.OnClick(e);
    }
  }

  // Custom EditStringCell
  public class InSightEditStringCell : DataGridViewTextBoxCell
  {
    private CvsInSight _inSight;
    private string _cellLocation;

    public InSightEditStringCell(CvsInSight inSight, string cellLocation)
    {
      _inSight = inSight;
      _cellLocation = cellLocation;
    }

    protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex, DataGridViewElementStates elementState, object value, object formattedValue, string errorText, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
    {
      advancedBorderStyle = new DataGridViewAdvancedBorderStyle();
      advancedBorderStyle.All = DataGridViewAdvancedCellBorderStyle.Inset;
      base.Paint(graphics, clipBounds, cellBounds, rowIndex, elementState, value, formattedValue, errorText, cellStyle, advancedBorderStyle, paintParts);
    }

    protected override void OnMouseClick(DataGridViewCellMouseEventArgs e)
    {
      if ((base.DataGridView != null) && (!this.ReadOnly))
      {
        Point point1 = base.DataGridView.CurrentCellAddress;
        if (point1.X == e.ColumnIndex &&
            point1.Y == e.RowIndex &&
            e.Button == MouseButtons.Left &&
            base.DataGridView.EditMode !=
            DataGridViewEditMode.EditProgrammatically)
        {
          this.ReadOnly = false;
          bool inEdit = base.DataGridView.BeginEdit(true);
        }
      }
      base.OnMouseClick(e);
    }

    protected async override void OnLeave(int rowIndex, bool throughMouseClick)
    {
      try
      {
        if (_inSight.Connected)
          await _inSight.SetCellValue(_cellLocation, this.Value.ToString());
      }
      catch (Exception)
      {
        SpreadsheetGridViewExtensions.HandleError("Error", "Unable to set value.");
      }
      base.OnLeave(rowIndex, throughMouseClick);
    }
  }

  // Custom ListBoxCell
  public class InSightListBoxCell : DataGridViewComboBoxCell
  {
    private CvsInSight _inSight;
    private string _cellLocation;
    private int _selectedIndex = 0;

    public InSightListBoxCell(CvsInSight inSight, string cellLocation)
    {
      _inSight = inSight;
      _cellLocation = cellLocation;
    }

    public void SetSelectedIndex(int selectedIndex)
    {
      _selectedIndex = selectedIndex;
    }

    public async void SetValue(int selectedIndex)
    {
      try
      {
        if (_inSight.Connected)
        {
          if (selectedIndex != _selectedIndex)
          {
            _selectedIndex = selectedIndex;
            await _inSight.SetCellValue(_cellLocation, selectedIndex.ToString());
          }
        }
      }
      catch (Exception)
      {
        SpreadsheetGridViewExtensions.HandleError("Error", "Unable to set value.");
      }
    }

    protected override void OnLeave(int rowIndex, bool throughMouseClick)
    {
      base.DataGridView.EndEdit();
      base.OnLeave(rowIndex, throughMouseClick);
    }

    protected override void OnMouseClick(DataGridViewCellMouseEventArgs e)
    {
      if ((base.DataGridView != null) && (!this.ReadOnly))
      {
        Point point1 = base.DataGridView.CurrentCellAddress;
        if (point1.X == e.ColumnIndex &&
            point1.Y == e.RowIndex &&
            e.Button == MouseButtons.Left &&
            base.DataGridView.EditMode !=
            DataGridViewEditMode.EditProgrammatically)
        {
          this.ReadOnly = false;
          if (!IsInEditMode)
          {
            bool inEdit = base.DataGridView.BeginEdit(true);
          }
        }
      }
      base.OnMouseClick(e);
    }

    protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex, DataGridViewElementStates elementState, object value, object formattedValue, string errorText, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
    {
      base.Paint(graphics, clipBounds, cellBounds, rowIndex,
             elementState, value, formattedValue, errorText, cellStyle,
             advancedBorderStyle, paintParts);
    }
  }

  // Custom TextCell
  public class InSightTextCell : DataGridViewCell
  {
    public InSightTextCell()
    {
    }

    protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex, DataGridViewElementStates elementState, object value, object formattedValue, string errorText, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
    {
      base.Paint(graphics, clipBounds, cellBounds, rowIndex, elementState, value, formattedValue, errorText, cellStyle, advancedBorderStyle, paintParts);

      Region saveClip = graphics.Clip;
      graphics.SetClip(cellBounds);
      graphics.FillRectangle(new SolidBrush(cellStyle.BackColor), cellBounds);
      PaintBorder(graphics, clipBounds, cellBounds, cellStyle, advancedBorderStyle);

      Font fnt = cellStyle.Font;

      string cellValueAsText = Convert.ToString(value);
      TextRenderer.DrawText(graphics, cellValueAsText,
                fnt, cellBounds, cellStyle.ForeColor, SpreadsheetGridViewExtensions.ConvertToTextFormatFlags(cellStyle.Alignment));

      graphics.Clip = saveClip;
    }
  }

  // Custom NumberCell
  public class InSightNumberCell : DataGridViewCell
  {
    public InSightNumberCell()
    {
    }

    protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex, DataGridViewElementStates elementState, object value, object formattedValue, string errorText, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
    {
      base.Paint(graphics, clipBounds, cellBounds, rowIndex, elementState, value, formattedValue, errorText, cellStyle, advancedBorderStyle, paintParts);

      Region saveClip = graphics.Clip;
      graphics.SetClip(cellBounds);
      graphics.FillRectangle(new SolidBrush(cellStyle.BackColor), cellBounds);
      PaintBorder(graphics, clipBounds, cellBounds, cellStyle, advancedBorderStyle);

      Font fnt = cellStyle.Font;

      float fValue = (float)Convert.ToDouble(value);
      string cellValueAsText = fValue.ToString("0.000");

      TextRenderer.DrawText(graphics, cellValueAsText,
                  fnt, cellBounds, cellStyle.ForeColor, SpreadsheetGridViewExtensions.ConvertToTextFormatFlags(cellStyle.Alignment));

      graphics.Clip = saveClip;
    }
  }
  
  // Custom EditNumberCell
  public class InSightEditNumberCell : NumericUpDownCell
  {
    private CvsInSight _inSight;
    private string _cellLocation;
    private bool _showDecimalPlaces;

    public InSightEditNumberCell(CvsInSight inSight, string cellLocation, bool showDecimalPlaces = true) :
      base(showDecimalPlaces)
    {
      _inSight = inSight;
      _cellLocation = cellLocation;
      _showDecimalPlaces = showDecimalPlaces;
    }
       
    protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex, DataGridViewElementStates elementState, object value, object formattedValue, string errorText, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
    {
      base.Paint(graphics, clipBounds, cellBounds, rowIndex, elementState, value, formattedValue, errorText, cellStyle, advancedBorderStyle, paintParts);

      Region saveClip = graphics.Clip;
      graphics.SetClip(cellBounds);
      graphics.FillRectangle(new SolidBrush(cellStyle.BackColor), cellBounds);
      DataGridViewAdvancedBorderStyle borderStyle = new DataGridViewAdvancedBorderStyle();
      borderStyle.All = DataGridViewAdvancedCellBorderStyle.Inset;
      PaintBorder(graphics, clipBounds, cellBounds, cellStyle, borderStyle);

      Font fnt = cellStyle.Font;

      float fValue = (float)Convert.ToDouble(value);
      string cellValueAsText = _showDecimalPlaces ? fValue.ToString("0.000") : fValue.ToString("F0");

      TextRenderer.DrawText(graphics, cellValueAsText,
                  fnt, cellBounds, cellStyle.ForeColor, SpreadsheetGridViewExtensions.ConvertToTextFormatFlags(cellStyle.Alignment));

      graphics.Clip = saveClip;
    }

    protected override void OnMouseClick(DataGridViewCellMouseEventArgs e)
    {
      if ((base.DataGridView != null) && (!this.ReadOnly))
      {
        Point point1 = base.DataGridView.CurrentCellAddress;
        if (point1.X == e.ColumnIndex &&
            point1.Y == e.RowIndex &&
            e.Button == MouseButtons.Left &&
            base.DataGridView.EditMode !=
            DataGridViewEditMode.EditProgrammatically)
        {
          this.ReadOnly = false;
          bool inEdit = base.DataGridView.BeginEdit(true);
        }
      }
      base.OnMouseClick(e);
    }

    protected async override void OnLeave(int rowIndex, bool throughMouseClick)
    {
      try
      {
        if (_inSight.Connected)
          await _inSight.SetCellValue(_cellLocation, this.Value.ToString());
      }
      catch (Exception)
      {
        SpreadsheetGridViewExtensions.HandleError("Error", "Unable to set value.");
      }
      base.OnLeave(rowIndex, throughMouseClick);
    }
  }

  public class NumericUpDownCell : DataGridViewTextBoxCell
  {
    private decimal _min = decimal.MinValue;
    private decimal _max = decimal.MaxValue;
    private bool _showDecimalPlaces;

    public NumericUpDownCell(bool showDecimalPlaces)
        : base()
    {
      Style.Format = showDecimalPlaces ? "F3" : "F0";
      _showDecimalPlaces = showDecimalPlaces;
    }

    public void SetMinMax(decimal min, decimal max)
    {
      this._min = min;
      this._max = max;
    }

    public override void InitializeEditingControl(int rowIndex, object initialFormattedValue, DataGridViewCellStyle dataGridViewCellStyle)
    {
      base.InitializeEditingControl(rowIndex, initialFormattedValue, dataGridViewCellStyle);
      NumericUpDownEditingControl ctl = DataGridView.EditingControl as NumericUpDownEditingControl;
      ctl.Minimum = this._min;
      ctl.Maximum = this._max;
      ctl.Value = Convert.ToDecimal(this.Value);
      ctl.DecimalPlaces = _showDecimalPlaces ? 3 : 0;
    }

    public override Type EditType
    {
      get { return typeof(NumericUpDownEditingControl); }
    }

    public override Type ValueType
    {
      get { return typeof(Decimal); }
    }

    public override object DefaultNewRowValue
    {
      get { return null; }
    }
  }

  public class NumericUpDownEditingControl : NumericUpDown, IDataGridViewEditingControl
  {
    private DataGridView dataGridViewControl;
    private bool valueIsChanged = false;
    private int rowIndexNum;

    public NumericUpDownEditingControl()
        : base()
    {
      this.DecimalPlaces = 3;
    }

    public DataGridView EditingControlDataGridView
    {
      get { return dataGridViewControl; }
      set { dataGridViewControl = value; }
    }

    public object EditingControlFormattedValue
    {
      get { return this.Value.ToString("F3"); }
      set { this.Value = Decimal.Parse(value.ToString()); }
    }
    public int EditingControlRowIndex
    {
      get { return rowIndexNum; }
      set { rowIndexNum = value; }
    }
    public bool EditingControlValueChanged
    {
      get { return valueIsChanged; }
      set { valueIsChanged = value; }
    }

    public Cursor EditingPanelCursor
    {
      get { return base.Cursor; }
    }

    public bool RepositionEditingControlOnValueChange
    {
      get { return false; }
    }

    public void ApplyCellStyleToEditingControl(DataGridViewCellStyle dataGridViewCellStyle)
    {
      this.Font = dataGridViewCellStyle.Font;
      this.ForeColor = dataGridViewCellStyle.ForeColor;
      this.BackColor = dataGridViewCellStyle.BackColor;
    }

    public bool EditingControlWantsInputKey(Keys keyData, bool dataGridViewWantsInputKey)
    {
      return (keyData == Keys.Left || keyData == Keys.Right ||
          keyData == Keys.Up || keyData == Keys.Down ||
          keyData == Keys.Home || keyData == Keys.End ||
          keyData == Keys.PageDown || keyData == Keys.PageUp);
    }

    public object GetEditingControlFormattedValue(DataGridViewDataErrorContexts context)
    {
      return this.Value.ToString();
    }

    public void PrepareEditingControlForEdit(bool selectAll)
    {
    }

    protected override void OnValueChanged(EventArgs e)
    {
      valueIsChanged = true;
      this.EditingControlDataGridView.NotifyCurrentCellDirty(true);
      base.OnValueChanged(e);
    }
  }

  // Custom InSightStatusCell
  public class InSightStatusCell : DataGridViewCell
  {
    int _cellValue;
    string _caption;
    Color _color;
    bool _useValueForColor;

    public InSightStatusCell()
    {
    }

    public void SetCell(int cellValue, string caption, Color color, bool useValueForColor)
    {
      _cellValue = cellValue;
      _caption = caption;
      _color = color;
      _useValueForColor = useValueForColor;
    }

    protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex, DataGridViewElementStates elementState, object value, object formattedValue, string errorText, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
    {
      base.Paint(graphics, clipBounds, cellBounds, rowIndex, elementState, value, formattedValue, errorText, cellStyle, advancedBorderStyle, paintParts);
      Rectangle rect = new Rectangle(cellBounds.X + 2, cellBounds.Y + 2, cellBounds.Height - 6, cellBounds.Height - 6);

      Region saveClip = graphics.Clip;
      graphics.SetClip(cellBounds);
      graphics.FillRectangle(new SolidBrush(cellStyle.BackColor), cellBounds);
      PaintBorder(graphics, clipBounds, cellBounds, cellStyle, advancedBorderStyle);

      Color color = _color;
      if (_useValueForColor)
      {
        color = Color.Yellow;
        if (_cellValue > 0)
          color = Color.LightGreen;
        else if (_cellValue < 0)
          color = Color.Red;
      }
     
      SolidBrush brush = new SolidBrush(color);
      graphics.FillEllipse(brush, rect);
      graphics.DrawEllipse(Pens.Black, rect);

      int statusCircleWidth = Math.Max(0, cellBounds.Height - 4);
      Rectangle textBounds = new Rectangle(cellBounds.X + statusCircleWidth, cellBounds.Y + 1, cellBounds.Width - statusCircleWidth, cellBounds.Height - 2);
      TextRenderer.DrawText(graphics, _caption, cellStyle.Font, textBounds, cellStyle.ForeColor, SpreadsheetGridViewExtensions.ConvertToTextFormatFlags(cellStyle.Alignment));

      graphics.Clip = saveClip;
    }
  }

  // Custom InSightMultiStatusCell
  public class InSightMultiStatusCell : DataGridViewCell
  {
    HmiMultiStatusResult _cell;

    public InSightMultiStatusCell()
    {
    }

    public void SetCell(HmiMultiStatusResult value)
    {
      _cell = value;
    }

    protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex, DataGridViewElementStates elementState, object value, object formattedValue, string errorText, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
    {
      base.Paint(graphics, clipBounds, cellBounds, rowIndex, elementState, value, formattedValue, errorText, cellStyle, advancedBorderStyle, paintParts);
      int circleWidth = cellBounds.Height - 6;
      Rectangle rect = new Rectangle(cellBounds.X + 2, cellBounds.Y + 2, circleWidth, circleWidth);

      Region saveClip = graphics.Clip;
      graphics.SetClip(cellBounds);
      graphics.FillRectangle(new SolidBrush(cellStyle.BackColor), cellBounds);
      PaintBorder(graphics, clipBounds, cellBounds, cellStyle, advancedBorderStyle);

      SolidBrush brush0 = new SolidBrush(Color.FromArgb((int)Convert.ToInt64(_cell.Color0)));
      SolidBrush brush1 = new SolidBrush(Color.FromArgb((int)Convert.ToInt64(_cell.Color1)));

      int val = Convert.ToInt32(_cell.Value);

      for (int i = 0; i < _cell.NumBits; i++)
      {
        int bitShift = (_cell.Reverse != 0) ? (_cell.StartBit + +i) : (_cell.StartBit + _cell.NumBits - i - 1);
        SolidBrush brush = ((val & (1 << (bitShift))) != 0) ? brush1 : brush0; ;
        graphics.FillEllipse(brush, rect);
        graphics.DrawEllipse(Pens.Black, rect);
        rect.X += circleWidth + 2;
      }

      graphics.Clip = saveClip;
    }
  }

  public abstract class SerializationHelper {
    /// <summary>
    /// Helper function to remove fields.
    /// </summary>
    /// <param name="token">Object to remove fields from.</param>
    /// <param name="fields">The names of the fields to remove.</param>
    public static void RemoveFields(JToken token, string[] fields)
    {
      JContainer container = token as JContainer;
      if (container == null) 
        return;

      List<JToken> removeList = new List<JToken>();
      foreach (JToken el in container.Children())
      {
        JProperty p = el as JProperty;
        if (p != null && fields.Contains(p.Name))
        {
          removeList.Add(el);
        }
        RemoveFields(el, fields);
      }

      foreach (JToken el in removeList)
      {
        el.Remove();
      }
    }
  }

  public class InSightBitmapCell : DataGridViewCell
  {
    Bitmap _bitmap;
    
    public InSightBitmapCell()
    {
    }

    public void SetBitmap(Bitmap bmp)
    {
      _bitmap = bmp;
    }

    protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex, DataGridViewElementStates elementState, object value, object formattedValue, string errorText, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
    {
      base.Paint(graphics, clipBounds, cellBounds, rowIndex, elementState, value, formattedValue, errorText, cellStyle, advancedBorderStyle, paintParts);
      Rectangle rect = new Rectangle(cellBounds.X + 2, cellBounds.Y + 2, cellBounds.Height - 6, cellBounds.Height - 6);

      Region saveClip = graphics.Clip;
      graphics.SetClip(cellBounds);
      graphics.FillRectangle(new SolidBrush(cellStyle.BackColor), cellBounds);
      PaintBorder(graphics, clipBounds, cellBounds, cellStyle, advancedBorderStyle);
      graphics.DrawImageUnscaled(_bitmap, cellBounds);
      graphics.Clip = saveClip;
    }
  }

  public class HmiDialogButtonCell : DataGridViewButtonCell
  {
    private CvsInSight _inSight;
    private HmiDialogCell _cell;
    private bool _isCustomView;

    public HmiDialogButtonCell(CvsInSight inSight, HmiDialogCell cell, bool isCustomView)
    {
      _inSight = inSight;
      _cell = cell;
      _isCustomView = isCustomView;
    }

    public void SetCell(HmiDialogCell value)
    {
      _cell = value;
    }

    protected override void OnClick(DataGridViewCellEventArgs e)
    {
      try
      {
        using (InSightDialog dlg = new InSightDialog(_inSight, _cell, _isCustomView))
        {
          DialogResult result = dlg.ShowDialog(this.DataGridView.FindForm());
        }
      }
      catch (Exception)
      {
        SpreadsheetGridViewExtensions.HandleError("Error", "Unable to show dialog.");
      }

      base.OnClick(e);
    }
  }

  public class HmiWizardButtonCell : DataGridViewButtonCell
  {
    private CvsInSight _inSight;
    private HmiWizardCell _cell;
    private bool _isCustomView;
    private HmiSpreadsheetCells _hmiCells;

    public HmiWizardButtonCell(CvsInSight inSight, HmiWizardCell cell, bool isCustomView, HmiSpreadsheetCells hmiCells)
    {
      _inSight = inSight;
      _cell = cell;
      _isCustomView = isCustomView;
      _hmiCells = hmiCells;
    }

    public void SetCell(HmiWizardCell value)
    {
      _cell = value;
    }

    protected override void OnClick(DataGridViewCellEventArgs e)
    {
      try
      {
        for (int index = 0; index < _cell.Dialogs.Length; index++)
        {
          string dlgLocation = _cell.Dialogs[index];
          foreach (HmiSpreadsheetCell cell in _hmiCells.Cells)
          {
            if (cell.Location == dlgLocation)
            {
              using (InSightDialog dlg = new InSightDialog(_inSight, cell as HmiDialogCell, _isCustomView))
              {
                DialogResult result = dlg.ShowDialog(this.DataGridView.FindForm());
                // Note: No support for cancel yet
              }
            }
          }
        }
      }
      catch (Exception)
      {
        SpreadsheetGridViewExtensions.HandleError("Error", "Unable to show dialog.");
      }

      base.OnClick(e);
    }
  }
}
