using Cognex.InSight.Remoting.Serialization;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
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

    // 선택한 셀 기준 연관 셀 화살표 (셀 1개씩만 조회 가능해서 조회한 적 있는 셀만 dependents로 잡힘)
    private static readonly Regex _cellRefRegex = new Regex(@"\b[A-Za-z]{1,2}[0-9]{1,3}\b", RegexOptions.Compiled);
    private readonly Dictionary<string, string> _expressionCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly List<ArrowInfo> _arrows = new List<ArrowInfo>();
    private string _selectedCellLocation;

    private class ArrowInfo
    {
      public string From;
      public string To;
      public Color Color;
    }

    // 셀 주소 자동완성 후보 (편집 UI는 CvsSpreadsheet.Editor.cs)
    private static readonly List<string> _allCellAddresses = BuildCellAddressList();

    private static List<string> BuildCellAddressList()
    {
      var list = new List<string>(26 * 600);
      for (int col = 0; col < 26; col++)
      {
        char letter = (char)('A' + col);
        for (int row = 0; row < 600; row++)
        {
          list.Add(letter.ToString() + row);
        }
      }
      return list;
    }

    public CvsSpreadsheet()
    {
      InitializeComponent();
      BuildEditorUi();
    }

    /// <summary>
    /// Creates a new instance to display the spreadsheet.
    /// </summary>
    /// <param name="isCustomView">A flag to display the sheet using custom view styling</param>
    public CvsSpreadsheet(bool isCustomView)
    {
      _isCustomView = isCustomView;
      InitializeComponent();
      BuildEditorUi();
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

      // 셀 편집 (수식 입력줄 / 셀 위 편집창 / 단축키) 이벤트
      HookEditorEvents();

      // 연관 셀 화살표 (중복 구독 방지를 위해 먼저 해제 후 구독)
      gridView.SelectionChanged -= gridView_SelectionChanged;
      gridView.SelectionChanged += gridView_SelectionChanged;
      gridView.Paint -= gridView_Paint;
      gridView.Paint += gridView_Paint;
      gridView.Scroll -= gridView_Scroll;
      gridView.Scroll += gridView_Scroll;
      gridView.CellPainting -= gridView_CellPainting;
      gridView.CellPainting += gridView_CellPainting;

      // Display the current results, if any
      if (_inSight?.Results != null)
      {
        UpdateResults(_inSight.Results);
      }
    }

    /// <summary>
    /// 새 잡 로드 후: 이전 잡의 셀(값/식 캐시/되돌리기/화살표)을 모두 버리고 새 시트 서식으로 그리드를 다시 만듦.
    /// UpdateResults는 결과에 있는 셀만 갱신해서, 그리드를 다시 만들지 않으면 새 잡에서 빈 셀에 이전 잡 내용이 남음
    /// </summary>
    public void ResetForNewJob()
    {
      if (_inlineHost != null) { _inlineHost.Visible = false; _inlineLocation = null; }
      HideSuggestions();
      _expressionCache.Clear();
      _undo.Clear();
      _copySourceLocation = null;
      _arrows.Clear();
      _selectedCellLocation = null;
      _exprDirty = false;
      if (_exprBox != null) _exprBox.Text = "";
      InitSpreadsheet();
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
    

    #region 선택한 셀 기준 연관 셀 화살표

    private async void gridView_SelectionChanged(object sender, EventArgs e)
    {
      try
      {
        // 선택 테두리는 Paint에서 그림 → 이전/새 선택 셀 모두 다시 그리기
        gridView.Invalidate();

        if (_inSight == null || !_inSight.Connected || gridView.CurrentCell == null || IsCustomView)
        {
          ClearArrows();
          return;
        }

        int colIndex = gridView.CurrentCell.ColumnIndex - 1;
        int rowIndex = gridView.CurrentCell.RowIndex;
        if (colIndex < 0)
        {
          ClearArrows();
          return;
        }

        string cellLocation = string.Format("{0}{1}", (char)('A' + colIndex), rowIndex);
        _selectedCellLocation = cellLocation;
        ShowSelectedAddress(cellLocation);

        if (!_expressionCache.ContainsKey(cellLocation))
        {
          try
          {
            string expr = await _inSight.GetCellExpression(cellLocation);
            _expressionCache[cellLocation] = expr ?? "";
          }
          catch
          {
            _expressionCache[cellLocation] = "";
          }
        }

        // 조회하는 사이 선택이 다른 셀로 바뀌었으면 무시 (늦게 도착한 응답)
        if (_selectedCellLocation != cellLocation)
          return;

        ShowSelectedExpression(cellLocation);
        UpdateArrows(cellLocation);
      }
      catch (Exception ex)
      {
        Debug.WriteLine("gridView_SelectionChanged: " + ex.Message);
      }
    }

    private void gridView_Scroll(object sender, ScrollEventArgs e)
    {
      // 화살표 좌표는 Paint에서 매번 다시 계산하므로 스크롤 시 다시 그리기만 하면 됨
      gridView.Invalidate();
    }

    private void ClearArrows()
    {
      if (_arrows.Count == 0)
        return;

      _arrows.Clear();
      gridView.Invalidate();
    }

    private void UpdateArrows(string selectedLocation)
    {
      _arrows.Clear();

      string expression;
      _expressionCache.TryGetValue(selectedLocation, out expression);

      // Precedents: 선택한 셀의 식이 참조하는 셀들
      if (!string.IsNullOrEmpty(expression))
      {
        var precedents = _cellRefRegex.Matches(expression)
          .Cast<Match>()
          .Select(m => m.Value.ToUpperInvariant())
          .Distinct()
          .Where(c => !string.Equals(c, selectedLocation, StringComparison.OrdinalIgnoreCase));

        foreach (var p in precedents)
        {
          _arrows.Add(new ArrowInfo { From = p, To = selectedLocation, Color = Color.DodgerBlue });
        }
      }

      // Dependents: 이미 조회했던 셀 중 선택한 셀을 참조하는 것들 (전체 스캔이 아니라 캐시 범위 한정)
      foreach (var kvp in _expressionCache)
      {
        if (string.Equals(kvp.Key, selectedLocation, StringComparison.OrdinalIgnoreCase))
          continue;
        if (string.IsNullOrEmpty(kvp.Value))
          continue;

        bool references = _cellRefRegex.Matches(kvp.Value)
          .Cast<Match>()
          .Any(m => string.Equals(m.Value, selectedLocation, StringComparison.OrdinalIgnoreCase));

        if (references)
        {
          _arrows.Add(new ArrowInfo { From = selectedLocation, To = kvp.Key, Color = Color.Orange });
        }
      }

      gridView.Invalidate();
    }

    private bool TryGetCellDisplayCenter(string location, out Point center)
    {
      center = Point.Empty;

      int rowIndex, colIndex;
      if (!HmiCellResult.LocationParse(location, out rowIndex, out colIndex))
        return false;

      int gridCol = colIndex + 1; // 0번 열은 행 번호 표시용
      if (gridCol < 0 || gridCol >= gridView.ColumnCount)
        return false;
      if (rowIndex < 0 || rowIndex >= gridView.RowCount)
        return false;

      Rectangle rect = gridView.GetCellDisplayRectangle(gridCol, rowIndex, false);
      if (rect.IsEmpty || !gridView.ClientRectangle.IntersectsWith(rect))
        return false; // 스크롤 밖이라 화면에 안 보임

      center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
      return true;
    }

    private static readonly Color SelectionFill = Color.FromArgb(45, 0, 120, 215);
    private static readonly Color SelectionBorder = Color.FromArgb(0, 100, 200);

    /// <summary>
    /// 가로 스크롤로 고정 열(행 번호) 밑에 반쯤 걸린 셀은 DataGridView 가 클립 없이 그려서 행 번호를 덮음.
    /// 셀 종류/색과 무관하게 모든 셀을 "고정 열 오른쪽" 영역으로 잘라서 직접 그림.
    /// </summary>
    private void gridView_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
    {
      if (e.ColumnIndex < 0 || gridView.Columns[e.ColumnIndex].Frozen)
        return;

      int frozenRight = (gridView.RowHeadersVisible ? gridView.RowHeadersWidth : 0)
        + gridView.Columns.GetColumnsWidth(DataGridViewElementStates.Frozen | DataGridViewElementStates.Visible);
      if (frozenRight <= 0 || e.CellBounds.Left >= frozenRight)
        return;

      Rectangle visible = Rectangle.FromLTRB(frozenRight, e.CellBounds.Top, Math.Max(frozenRight, e.CellBounds.Right), e.CellBounds.Bottom);
      System.Drawing.Drawing2D.GraphicsState state = e.Graphics.Save();
      try
      {
        e.Graphics.SetClip(visible, System.Drawing.Drawing2D.CombineMode.Intersect);
        if (!visible.IsEmpty && visible.Width > 0)
          e.Paint(visible, e.PaintParts);
      }
      finally
      {
        e.Graphics.Restore(state);
      }
      e.Handled = true;
    }

    private void gridView_Paint(object sender, PaintEventArgs e)
    {
      // 고정된 행 번호 열 위로는 선택 테두리/화살표를 그리지 않음
      if (gridView.Columns.Count > 0 && gridView.Columns[0].Visible)
      {
        int left = gridView.Columns[0].Width;
        e.Graphics.SetClip(new Rectangle(left, 0, Math.Max(0, gridView.Width - left), gridView.Height), System.Drawing.Drawing2D.CombineMode.Intersect);
      }

      PaintCurrentCellFrame(e.Graphics);

      foreach (var arrow in _arrows)
      {
        Point from, to;
        if (!TryGetCellDisplayCenter(arrow.From, out from))
          continue;
        if (!TryGetCellDisplayCenter(arrow.To, out to))
          continue;

        using (var pen = new Pen(arrow.Color, 2f) { EndCap = System.Drawing.Drawing2D.LineCap.ArrowAnchor })
        {
          e.Graphics.DrawLine(pen, from, to);
        }
      }
    }

    /// <summary>
    /// 선택 셀 표시 (Suite/Excel처럼 굵은 테두리 + 옅은 파란 바탕).
    /// 결과 표시용 커스텀 셀(InSightTextCell 등)은 자체 Paint라 그리드 기본 선택색이 안 보여서 위에 덧그림
    /// </summary>
    private void PaintCurrentCellFrame(Graphics g)
    {
      if (IsCustomView || gridView.CurrentCell == null || gridView.CurrentCell.ColumnIndex < 1)
        return;

      Rectangle r = gridView.GetCellDisplayRectangle(gridView.CurrentCell.ColumnIndex, gridView.CurrentCell.RowIndex, true);
      if (r.IsEmpty)
        return;

      using (var fill = new SolidBrush(SelectionFill))
        g.FillRectangle(fill, r);
      using (var pen = new Pen(SelectionBorder, 2f))
        g.DrawRectangle(pen, r.X, r.Y, r.Width - 2, r.Height - 2);
    }

    #endregion
  }
}
