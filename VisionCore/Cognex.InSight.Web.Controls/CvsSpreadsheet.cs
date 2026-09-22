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

    // 셀 식(expression) 인라인 편집 - 그리드 기본 편집 컨트롤을 그대로 사용해서 별도 창 없이 그리드 스타일 그대로 편집됨
    private static readonly List<string> _allCellAddresses = BuildCellAddressList();
    private string _editingExpressionLocation;
    private TextBox _activeEditingTextBox;
    private ToolStripDropDown _suggestDropDown;
    private ListBox _suggestList;

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

      // 셀 식 인라인 편집 커밋 처리 (중복 구독 방지를 위해 먼저 해제 후 구독)
      gridView.CellEndEdit -= gridView_CellEndEdit;
      gridView.CellEndEdit += gridView_CellEndEdit;

      // 연관 셀 화살표 (중복 구독 방지를 위해 먼저 해제 후 구독)
      gridView.SelectionChanged -= gridView_SelectionChanged;
      gridView.SelectionChanged += gridView_SelectionChanged;
      gridView.Paint -= gridView_Paint;
      gridView.Paint += gridView_Paint;
      gridView.Scroll -= gridView_Scroll;
      gridView.Scroll += gridView_Scroll;

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

      // 편집 컨트롤은 재사용되므로 매번 이전 핸들러를 먼저 떼어냄
      if (_activeEditingTextBox != null)
      {
        _activeEditingTextBox.TextChanged -= EditingTextBox_TextChanged;
        _activeEditingTextBox.KeyDown -= EditingTextBox_KeyDown;
        _activeEditingTextBox = null;
      }

      if (_editingExpressionLocation != null && e.Control is TextBox textBox)
      {
        _activeEditingTextBox = textBox;
        textBox.TextChanged += EditingTextBox_TextChanged;
        textBox.KeyDown += EditingTextBox_KeyDown;
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
      if (e.RowIndex < 0 || e.ColumnIndex <= 0)
        return;

      string cellLocation = string.Format("{0}{1}", (char)('A' + (e.ColumnIndex - 1)), e.RowIndex);

      if (!_inSight.Connected || _inSight.Online || _inSight.EditorAttached || IsCustomView)
        return;

      // Only admins should be able to set an expression.
      if (_inSight.AccessLevel.ToLower() != "full")
        return;

      try
      {
        // Get the current expression...
        string value = await _inSight.GetCellExpression(cellLocation);

        // 별도 창을 띄우지 않고, 이 셀을 그리드 기본 편집 컨트롤(DataGridViewTextBoxCell)로 바꿔치기해서
        // 그리드 스타일 그대로 셀 안에서 바로 입력하도록 함. 편집이 끝나면(gridView_CellEndEdit) 다음
        // 결과 갱신 때 원래 셀 타입으로 자동 복원됨.
        var currentCell = gridView[e.ColumnIndex, e.RowIndex];
        var editCell = new DataGridViewTextBoxCell();
        SpreadsheetGridViewExtensions.CopyGridViewStyle(currentCell, editCell);
        gridView[e.ColumnIndex, e.RowIndex] = editCell;
        gridView[e.ColumnIndex, e.RowIndex].Value = value;

        _editingExpressionLocation = cellLocation;
        gridView.CurrentCell = gridView[e.ColumnIndex, e.RowIndex];
        gridView.BeginEdit(true);
      }
      catch (Exception ex)
      {
        Debug.WriteLine("gridView_CellDoubleClick:" + ex.Message);
        MessageBox.Show("Unable to set Expression.", "Error");
      }
    }

    private async void gridView_CellEndEdit(object sender, DataGridViewCellEventArgs e)
    {
      if (_editingExpressionLocation == null || e.RowIndex < 0 || e.ColumnIndex <= 0)
        return;

      string cellLocation = string.Format("{0}{1}", (char)('A' + (e.ColumnIndex - 1)), e.RowIndex);
      if (!string.Equals(cellLocation, _editingExpressionLocation, StringComparison.OrdinalIgnoreCase))
        return;

      _editingExpressionLocation = null;
      HideSuggestions();

      string newExpression = Convert.ToString(gridView[e.ColumnIndex, e.RowIndex].Value);

      try
      {
        if (_inSight.Connected)
        {
          await _inSight.SetCellExpression(cellLocation, newExpression);
          _expressionCache[cellLocation] = newExpression;
          UpdateArrows(cellLocation);

          // 결과가 새로 들어올 때까지 기다리지 않고 바로 갱신 (그리드에 변경값이 즉시 보이도록)
          await _inSight.GetLatestResult();
          if (_inSight.Results != null)
            UpdateResults(_inSight.Results);
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine("gridView_CellEndEdit:" + ex.Message);
        MessageBox.Show("Unable to set Expression.", "Error");
      }
    }

    #region 셀 주소 자동완성 (인라인 편집 중)

    private void EditingTextBox_TextChanged(object sender, EventArgs e)
    {
      var textBox = sender as TextBox;
      if (textBox == null)
        return;

      string token = GetToken(textBox.Text, textBox.SelectionStart, out int start, out int end);
      if (string.IsNullOrEmpty(token) || !char.IsLetter(token[0]))
      {
        HideSuggestions();
        return;
      }

      var matches = _allCellAddresses
        .Where(c => c.StartsWith(token, StringComparison.OrdinalIgnoreCase))
        .Take(30)
        .ToList();

      if (matches.Count == 0)
      {
        HideSuggestions();
        return;
      }

      EnsureSuggestDropDown();
      _suggestList.BeginUpdate();
      _suggestList.Items.Clear();
      _suggestList.Items.AddRange(matches.Cast<object>().ToArray());
      _suggestList.EndUpdate();
      _suggestList.SelectedIndex = 0;

      if (!_suggestDropDown.Visible)
      {
        _suggestDropDown.Show(textBox, new Point(0, textBox.Height));
        textBox.Focus(); // 드롭다운이 떠도 입력 포커스는 계속 텍스트박스에 유지
      }
    }

    private void EditingTextBox_KeyDown(object sender, KeyEventArgs e)
    {
      if (_suggestDropDown == null || !_suggestDropDown.Visible)
        return;

      if (e.KeyCode == Keys.Down)
      {
        if (_suggestList.SelectedIndex < _suggestList.Items.Count - 1)
          _suggestList.SelectedIndex++;
        e.Handled = true;
        e.SuppressKeyPress = true;
      }
      else if (e.KeyCode == Keys.Up)
      {
        if (_suggestList.SelectedIndex > 0)
          _suggestList.SelectedIndex--;
        e.Handled = true;
        e.SuppressKeyPress = true;
      }
      else if (e.KeyCode == Keys.Tab)
      {
        AcceptSuggestion(sender as TextBox);
        e.Handled = true;
        e.SuppressKeyPress = true;
      }
      else if (e.KeyCode == Keys.Escape)
      {
        HideSuggestions();
        e.Handled = true;
      }
      // Enter는 그대로 둬서 그리드가 평소처럼 셀 편집을 커밋하도록 함
    }

    private void EnsureSuggestDropDown()
    {
      if (_suggestDropDown != null)
        return;

      _suggestList = new ListBox
      {
        BorderStyle = BorderStyle.None,
        IntegralHeight = false,
        Size = new Size(140, 90)
      };
      _suggestList.MouseMove += (s, e) =>
      {
        int index = _suggestList.IndexFromPoint(e.Location);
        if (index >= 0) _suggestList.SelectedIndex = index;
      };
      _suggestList.MouseUp += (s, e) => AcceptSuggestion(_activeEditingTextBox);

      var host = new ToolStripControlHost(_suggestList)
      {
        Margin = Padding.Empty,
        Padding = Padding.Empty,
        AutoSize = false,
        Size = _suggestList.Size
      };

      _suggestDropDown = new ToolStripDropDown { Padding = Padding.Empty, AutoClose = true };
      _suggestDropDown.Items.Add(host);
    }

    private void HideSuggestions()
    {
      _suggestDropDown?.Close();
    }

    private void AcceptSuggestion(TextBox textBox)
    {
      if (textBox == null || _suggestList == null || _suggestList.SelectedItem == null)
        return;

      string selected = _suggestList.SelectedItem.ToString();
      GetToken(textBox.Text, textBox.SelectionStart, out int start, out int end);

      textBox.Text = textBox.Text.Substring(0, start) + selected + textBox.Text.Substring(end);
      textBox.SelectionStart = start + selected.Length;

      HideSuggestions();
      textBox.Focus();
    }

    private static string GetToken(string text, int caret, out int start, out int end)
    {
      caret = Math.Min(caret, text.Length);

      start = caret;
      while (start > 0 && char.IsLetterOrDigit(text[start - 1])) start--;

      end = caret;
      while (end < text.Length && char.IsLetterOrDigit(text[end])) end++;

      return text.Substring(start, end - start);
    }

    #endregion

    #region 선택한 셀 기준 연관 셀 화살표

    private async void gridView_SelectionChanged(object sender, EventArgs e)
    {
      try
      {
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

    private void gridView_Paint(object sender, PaintEventArgs e)
    {
      if (_arrows.Count == 0)
        return;

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

    #endregion
  }
}
