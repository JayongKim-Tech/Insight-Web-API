using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Cognex.InSight.Web.Controls
{
  /// <summary>
  /// In-Sight Suite 스타일 셀 편집:
  ///  - 위쪽 수식 입력줄: [셀 주소] fx [식] [함수]  (주소 입력 + Enter = 해당 셀로 이동)
  ///  - 셀 위에 겹쳐 뜨는 편집창 (결과 갱신으로 그리드 셀이 바뀌어도 편집 내용 유지)
  ///  - 함수/셀 주소 자동완성, 현재 인자 힌트, 함수 패널(검색 + 더블클릭 삽입)
  ///  - 단축키: 바로 타이핑 / F2 / 더블클릭 = 편집, Enter = 적용 (같은 셀 유지), Esc = 취소,
  ///            Delete = 비우기, Ctrl+Z = 되돌리기, Ctrl+C/X/V = 식 복사/잘라내기/붙여넣기(상대 참조 보정)
  /// </summary>
  /// <summary>
  /// Enter/Esc/Tab/↑↓ 를 자기 KeyDown으로 받는 TextBox.
  /// 기본 TextBox는 이 키들을 부모(DataGridView, WindowsFormsHost)에 먼저 넘겨서 그리드가 가로채 버림 → 적용이 안 되던 원인
  /// </summary>
  internal class EditorTextBox : TextBox
  {
    protected override bool IsInputKey(Keys keyData)
    {
      switch (keyData & Keys.KeyCode)
      {
        case Keys.Enter:
        case Keys.Escape:
        case Keys.Tab:
        case Keys.Up:
        case Keys.Down:
          return true;
      }
      return base.IsInputKey(keyData);
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
      // 혹시 대화상자 키로 넘어와도 부모에 넘기지 않고 KeyDown에서 처리
      switch (keyData & Keys.KeyCode)
      {
        case Keys.Enter:
        case Keys.Escape:
        case Keys.Tab:
          return false;
      }
      return base.ProcessDialogKey(keyData);
    }
  }

  public partial class CvsSpreadsheet
  {
    /// <summary>
    /// 함수명 → 인자 이름 (자동완성/힌트/함수 패널용). 호스트 앱이 채움 (VisionCore: ToolCatalog)
    /// </summary>
    public static IDictionary<string, string[]> FunctionArgs { get; set; } =
      new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    private class SuggestItem
    {
      public string Text;
      public bool IsFunction;
      public override string ToString() => IsFunction ? "ƒ  " + Text : "    " + Text;
    }

    private class UndoItem
    {
      public string Location;
      public string Expression;
    }

    private static readonly Color HintColor = Color.FromArgb(70, 70, 70);
    private static readonly Color ErrorColor = Color.FromArgb(200, 40, 40);
    private static readonly Color OkColor = Color.FromArgb(30, 120, 50);
    private static readonly Regex _relativeRefRegex =
      new Regex(@"(?<![A-Za-z0-9_.$])(\$?)([A-Za-z])(\$?)(\d{1,3})(?![A-Za-z0-9_(])", RegexOptions.Compiled);

    private Panel _formulaBar;
    private TextBox _addressBox;
    private TextBox _exprBox;
    private Button _funcButton;
    private Label _hint;
    private Panel _funcPanel;
    private TextBox _funcFilter;
    private ListBox _funcList;
    private Label _funcSignature;

    private Panel _inlineHost;
    private TextBox _inlineBox;
    private string _inlineLocation;
    private bool _committingInline;

    private string _exprLocation;
    private bool _exprDirty;

    private ToolStripDropDown _suggestDropDown;
    private ListBox _suggestList;
    private TextBox _suggestTarget;

    private readonly Stack<UndoItem> _undo = new Stack<UndoItem>();
    private string _copySourceLocation;
    private bool _editorHooked;

    #region UI 구성

    private void BuildEditorUi()
    {
      if (_isCustomView)
        return;

      Font codeFont = new Font("Consolas", 10F);

      // 수식 입력줄
      _formulaBar = new Panel { Dock = DockStyle.Top, Height = 28, Padding = new Padding(3), BackColor = SystemColors.Control };
      _addressBox = new EditorTextBox { Dock = DockStyle.Left, Width = 64, Font = codeFont, TextAlign = HorizontalAlignment.Center };
      var fx = new Label { Dock = DockStyle.Left, Width = 26, Text = "fx", TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Times New Roman", 11F, FontStyle.Italic), ForeColor = Color.DimGray };
      _funcButton = new Button { Dock = DockStyle.Right, Width = 56, Text = "함수 ▸", FlatStyle = FlatStyle.System };
      _exprBox = new EditorTextBox { Dock = DockStyle.Fill, Font = codeFont };
      _formulaBar.Controls.Add(_exprBox);
      _formulaBar.Controls.Add(_funcButton);
      _formulaBar.Controls.Add(fx);
      _formulaBar.Controls.Add(_addressBox);

      // 힌트/상태 줄
      _hint = new Label { Dock = DockStyle.Top, Height = 20, Padding = new Padding(6, 3, 6, 0), AutoEllipsis = true, ForeColor = HintColor, BackColor = Color.FromArgb(245, 245, 240), Font = new Font("Malgun Gothic", 8.5F) };

      // 함수 패널
      _funcPanel = new Panel { Dock = DockStyle.Right, Width = 230, Visible = false, BackColor = SystemColors.Control, Padding = new Padding(4) };
      var funcTitle = new Label { Dock = DockStyle.Top, Height = 20, Text = "함수 (더블클릭 = 삽입)", Font = new Font("Malgun Gothic", 9F, FontStyle.Bold) };
      _funcFilter = new TextBox { Dock = DockStyle.Top };
      _funcSignature = new Label { Dock = DockStyle.Bottom, Height = 110, Padding = new Padding(2, 6, 2, 2), Font = new Font("Consolas", 8.5F), BackColor = Color.White };
      _funcList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font("Consolas", 9.5F) };
      _funcPanel.Controls.Add(_funcList);
      _funcPanel.Controls.Add(_funcSignature);
      _funcPanel.Controls.Add(_funcFilter);
      _funcPanel.Controls.Add(funcTitle);

      // 셀 위 편집창 (Suite처럼 노란 테두리 + 흰 바탕, 오른쪽으로 넓게)
      _inlineHost = new Panel { Visible = false, BackColor = Color.FromArgb(235, 190, 0), Padding = new Padding(2) };
      _inlineBox = new EditorTextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Color.White, Font = codeFont };
      _inlineHost.Controls.Add(_inlineBox);
      gridView.Controls.Add(_inlineHost);

      // 도킹 순서: 나중에 추가한 것이 먼저 가장자리를 차지 (수식 입력줄이 맨 위)
      Controls.Add(_funcPanel);
      Controls.Add(_hint);
      Controls.Add(_formulaBar);

      _addressBox.KeyDown += AddressBox_KeyDown;
      _exprBox.TextChanged += (s, e) => { if (_exprBox.Focused) { _exprDirty = true; OnEditorTextChanged(_exprBox); } };
      _exprBox.KeyDown += ExprBox_KeyDown;
      _exprBox.KeyUp += (s, e) => UpdateArgumentHint(_exprBox);
      _exprBox.MouseUp += (s, e) => UpdateArgumentHint(_exprBox);
      _exprBox.Enter += (s, e) => { string reason; if (!CanEdit(out reason)) ShowHint(reason, true); };
      _exprBox.Leave += (s, e) => { if (_suggestList == null || !_suggestList.Focused) HideSuggestions(); };

      _inlineBox.TextChanged += (s, e) => OnEditorTextChanged(_inlineBox);
      _inlineBox.KeyDown += InlineBox_KeyDown;
      _inlineBox.KeyUp += (s, e) => UpdateArgumentHint(_inlineBox);
      _inlineBox.Leave += async (s, e) =>
      {
        // 자동완성 목록을 클릭하는 중이면 커밋하지 않음
        if (_suggestDropDown != null && _suggestDropDown.Visible && _suggestDropDown.Bounds.Contains(Cursor.Position)) return;
        await CommitInlineAsync(false);
      };

      _funcButton.Click += (s, e) => ToggleFunctionPanel();
      _funcFilter.TextChanged += (s, e) => FillFunctionList();
      _funcList.SelectedIndexChanged += (s, e) => ShowFunctionSignature();
      _funcList.DoubleClick += (s, e) => InsertSelectedFunction();
      _funcList.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { InsertSelectedFunction(); e.Handled = true; } };

      ShowHint("클릭 = 선택 · Enter/더블클릭/F2/바로 타이핑 = 편집 · Enter 적용 · Esc 취소 · Delete 비우기 · Ctrl+Z 되돌리기", false);
    }

    /// <summary>InitSpreadsheet에서 호출 (그리드 이벤트는 여러 번 연결되지 않게)</summary>
    private void HookEditorEvents()
    {
      if (_isCustomView || _editorHooked)
        return;
      _editorHooked = true;

      gridView.KeyDown += Grid_KeyDown;
      gridView.KeyPress += Grid_KeyPress;
      gridView.CellBeginEdit += Grid_CellBeginEdit;
      gridView.Scroll += async (s, e) => await CommitInlineAsync(false);
      gridView.ColumnWidthChanged += async (s, e) => await CommitInlineAsync(false);
      gridView.Resize += (s, e) => PositionInlineBox();
    }

    #endregion

    #region 선택 셀 표시 (SelectionChanged에서 호출)

    private void ShowSelectedAddress(string location)
    {
      if (_addressBox == null) return;
      _addressBox.Text = location;
      if (!_exprDirty || !_exprBox.Focused)
      {
        string expr;
        _exprBox.Text = _expressionCache.TryGetValue(location, out expr) ? expr : "";
        _exprLocation = location;
        _exprDirty = false;
      }
    }

    private void ShowSelectedExpression(string location)
    {
      if (_exprBox == null) return;
      if (_exprDirty && _exprBox.Focused) return;   // 사용자가 입력줄을 편집 중이면 덮어쓰지 않음

      string expr;
      _expressionCache.TryGetValue(location, out expr);
      _exprBox.Text = expr ?? "";
      _exprLocation = location;
      _exprDirty = false;

      string reason;
      if (!CanEdit(out reason))
        ShowHint(reason, true);
      else
        ShowSignatureFor(expr, -1);
    }

    #endregion

    #region 편집 가능 여부 / 힌트

    private bool CanEdit(out string reason)
    {
      reason = null;
      if (_inSight == null || !_inSight.Connected) { reason = "센서가 연결되어 있지 않습니다."; return false; }
      if (_inSight.Online) { reason = "Online 상태에서는 셀을 수정할 수 없습니다. Offline으로 바꾼 뒤 수정하세요."; return false; }
      if (_inSight.EditorAttached) { reason = "In-Sight Suite/Explorer가 이 센서를 편집 중이라 수정할 수 없습니다."; return false; }
      if (_inSight.AccessLevel == null || _inSight.AccessLevel.ToLower() != "full") { reason = "Full(관리자) 권한으로 로그인해야 셀을 수정할 수 있습니다."; return false; }
      return true;
    }

    private void ShowHint(string text, bool isError)
    {
      if (_hint == null) return;
      _hint.ForeColor = isError ? ErrorColor : HintColor;
      _hint.Text = text;
    }

    private void ShowOk(string text)
    {
      if (_hint == null) return;
      _hint.ForeColor = OkColor;
      _hint.Text = text;
    }

    /// <summary>
    /// 커서가 함수 괄호 안에 있으면 "FindLine( Image, …, [Accept Thresh], … )" 처럼 현재 인자를 표시
    /// </summary>
    private void UpdateArgumentHint(TextBox box)
    {
      if (box == null || !box.Visible) return;
      ShowSignatureFor(box.Text, box.SelectionStart);
    }

    private void ShowSignatureFor(string text, int caret)
    {
      if (string.IsNullOrEmpty(text)) return;
      string func;
      int argIndex;
      if (!FindCallAt(text, caret < 0 ? text.Length : caret, out func, out argIndex))
      {
        // 커서 위치에 열린 괄호가 없으면 식의 첫 함수 서명
        Match m = Regex.Match(text, @"^\s*=?\s*([A-Za-z_][A-Za-z0-9_]*)\s*\(");
        if (!m.Success) return;
        func = m.Groups[1].Value;
        argIndex = -1;
      }

      string[] args;
      if (!FunctionArgs.TryGetValue(func, out args)) return;
      _hint.ForeColor = HintColor;
      _hint.Text = FormatSignature(func, args, argIndex);
    }

    private static string FormatSignature(string func, string[] args, int current)
    {
      var sb = new StringBuilder(func).Append("( ");
      for (int i = 0; i < args.Length; i++)
      {
        if (i > 0) sb.Append(", ");
        sb.Append(i == current ? "【" + args[i] + "】" : args[i]);
      }
      return sb.Append(" )").ToString();
    }

    /// <summary>caret 위치를 감싸는 가장 안쪽 함수 호출과 인자 번호</summary>
    private static bool FindCallAt(string text, int caret, out string func, out int argIndex)
    {
      func = null;
      argIndex = -1;
      var stack = new Stack<KeyValuePair<string, int>>();
      bool inString = false;
      int end = Math.Min(caret, text.Length);

      for (int i = 0; i < end; i++)
      {
        char c = text[i];
        if (inString) { if (c == '"') inString = false; continue; }
        if (c == '"') { inString = true; continue; }
        if (c == '(')
        {
          int j = i - 1;
          while (j >= 0 && char.IsWhiteSpace(text[j])) j--;
          int nameEnd = j;
          while (j >= 0 && (char.IsLetterOrDigit(text[j]) || text[j] == '_')) j--;
          string name = nameEnd > j ? text.Substring(j + 1, nameEnd - j) : "";
          stack.Push(new KeyValuePair<string, int>(name, 0));
        }
        else if (c == ')' && stack.Count > 0) stack.Pop();
        else if (c == ',' && stack.Count > 0)
        {
          var top = stack.Pop();
          stack.Push(new KeyValuePair<string, int>(top.Key, top.Value + 1));
        }
      }

      while (stack.Count > 0)
      {
        var top = stack.Pop();
        if (!string.IsNullOrEmpty(top.Key)) { func = top.Key; argIndex = top.Value; return true; }
      }
      return false;
    }

    #endregion

    #region 셀 위 편집창

    private bool TryParseLocation(string location, out int gridCol, out int row)
    {
      gridCol = row = -1;
      Match m = Regex.Match(location ?? "", @"^\s*\$?([A-Za-z])\$?(\d{1,3})\s*$");
      if (!m.Success) return false;
      gridCol = char.ToUpperInvariant(m.Groups[1].Value[0]) - 'A' + 1;
      row = int.Parse(m.Groups[2].Value);
      return gridCol >= 1 && gridCol < gridView.ColumnCount && row >= 0 && row < gridView.RowCount;
    }

    private static string LocationOf(int gridCol, int row)
    {
      return ((char)('A' + gridCol - 1)).ToString() + row;
    }

    private string CurrentLocation
    {
      get
      {
        if (gridView.CurrentCell == null || gridView.CurrentCell.ColumnIndex < 1) return null;
        return LocationOf(gridView.CurrentCell.ColumnIndex, gridView.CurrentCell.RowIndex);
      }
    }

    private void SelectCell(string location)
    {
      int col, row;
      if (!TryParseLocation(location, out col, out row))
      {
        ShowHint("셀 주소가 올바르지 않습니다: " + location, true);
        return;
      }
      gridView.CurrentCell = gridView[col, row];
      gridView.Focus();
    }

    /// <param name="initialText">null = 기존 식으로 시작, 그 외 = 이 글자로 새로 입력 (바로 타이핑)</param>
    private async void BeginInlineEdit(string location, string initialText)
    {
      if (_inlineBox == null || location == null) return;
      string reason;
      if (!CanEdit(out reason)) { ShowHint(reason, true); return; }

      await CommitInlineAsync(false);

      string text = initialText;
      if (text == null)
      {
        if (!_expressionCache.TryGetValue(location, out text))
        {
          try { text = await _inSight.GetCellExpression(location) ?? ""; }
          catch { text = ""; }
          _expressionCache[location] = text;
        }
      }

      _inlineLocation = location;
      int c, r;
      if (TryParseLocation(location, out c, out r))
        _inlineBox.Font = gridView[c, r].InheritedStyle.Font ?? gridView.Font;   // 셀 글꼴 그대로
      PositionInlineBox();
      _inlineBox.Text = text;
      _inlineHost.Visible = true;
      _inlineHost.BringToFront();
      _inlineBox.Focus();
      _inlineBox.SelectionStart = _inlineBox.Text.Length;
      UpdateArgumentHint(_inlineBox);
    }

    private void PositionInlineBox()
    {
      int col, row;
      if (_inlineHost == null || _inlineLocation == null || !TryParseLocation(_inlineLocation, out col, out row)) return;
      Rectangle r = gridView.GetCellDisplayRectangle(col, row, false);
      if (r.IsEmpty) return;
      int width = Math.Max(r.Width + 4, Math.Min(gridView.ClientSize.Width - r.X, 900));
      int height = Math.Max(r.Height + 4, _inlineBox.PreferredHeight + 4);
      _inlineHost.Bounds = new Rectangle(r.X - 2, r.Y - 2 + (r.Height + 4 - height) / 2, width, height);
    }

    private async System.Threading.Tasks.Task CommitInlineAsync(bool cancel)
    {
      if (_inlineHost == null || !_inlineHost.Visible || _committingInline) return;
      _committingInline = true;
      try
      {
        string location = _inlineLocation;
        string text = _inlineBox.Text;
        _inlineHost.Visible = false;
        _inlineLocation = null;
        HideSuggestions();
        if (!cancel && location != null)
          await CommitAsync(location, text, true);
      }
      finally
      {
        _committingInline = false;
      }
    }

    private async void InlineBox_KeyDown(object sender, KeyEventArgs e)
    {
      if (HandleSuggestionKeys(e)) return;

      if (e.KeyCode == Keys.Escape)
      {
        e.SuppressKeyPress = true;
        await CommitInlineAsync(true);
        gridView.Focus();
      }
      else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Tab || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
      {
        // 편집창은 바로 닫고 선택 이동 → 센서 적용은 뒤에서 (기다리지 않아서 끊김 없음)
        // Enter = 같은 셀에 그대로 머무름 (적용 결과를 바로 확인), Tab/↑↓ = 옆/위/아래 셀로 이동
        e.SuppressKeyPress = true;
        string location = _inlineLocation;
        var commit = CommitInlineAsync(false);
        int dRow = e.KeyCode == Keys.Down ? 1 : e.KeyCode == Keys.Up ? -1 : 0;
        MoveFrom(location, dRow, e.KeyCode == Keys.Tab ? 1 : 0);
        await commit;
      }
    }

    private void MoveFrom(string location, int dRow, int dCol)
    {
      int col, row;
      if (!TryParseLocation(location, out col, out row)) return;
      row = Math.Max(0, Math.Min(gridView.RowCount - 1, row + dRow));
      col = Math.Max(1, Math.Min(gridView.ColumnCount - 1, col + dCol));
      gridView.CurrentCell = gridView[col, row];
      gridView.Focus();
    }

    // 한 번 클릭 = 선택만 (키보드로 이동 가능), 더블클릭 = 식 편집 (Designer에서 연결됨)
    private void gridView_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
    {
      if (e.RowIndex < 0 || e.ColumnIndex <= 0 || IsCustomView) return;
      string loc = LocationOf(e.ColumnIndex, e.RowIndex);
      // 클릭 처리가 끝난 뒤 열어야 그리드가 포커스를 다시 가져가서 편집창이 바로 닫히는 일이 없음
      BeginInvoke((Action)(() => BeginInlineEdit(loc, null)));
    }

    /// <summary>
    /// 그리드 자체 편집(파란 테두리 작은 칸)은 값 입력 셀에서만 허용.
    /// 나머지 셀에서 그리드가 직접 편집하면 화면에만 들어가고 센서로는 안 가서 "수정이 안 되는" 것처럼 보였음
    /// </summary>
    private void Grid_CellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
    {
      if (e.ColumnIndex <= 0 || e.RowIndex < 0) return;
      if (!IsValueEditCell(gridView[e.ColumnIndex, e.RowIndex])) e.Cancel = true;
    }

    #endregion

    #region 그리드 단축키

    private void Grid_KeyPress(object sender, KeyPressEventArgs e)
    {
      // 셀을 선택한 상태에서 바로 타이핑 = 새 식 입력 (Suite/Excel과 같음)
      if (char.IsControl(e.KeyChar) || (ModifierKeys & (Keys.Control | Keys.Alt)) != 0) return;
      string loc = CurrentLocation;
      if (loc == null || IsValueEditCell()) return;
      e.Handled = true;
      BeginInlineEdit(loc, e.KeyChar.ToString());
    }

    /// <summary>
    /// 셀 선택 상태에서 Enter = 편집창 열기 (Suite와 같음). 그리드 기본 동작(아래 행으로 이동)보다 먼저 가로챔
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
      if (keyData == Keys.Enter && TryBeginEditFromGrid())
        return true;
      return base.ProcessCmdKey(ref msg, keyData);
    }

    private bool TryBeginEditFromGrid()
    {
      if (_isCustomView || !gridView.Focused || gridView.IsCurrentCellInEditMode) return false;
      string loc = CurrentLocation;
      if (loc == null) return false;
      BeginInlineEdit(loc, null);
      return true;
    }

    private async void Grid_KeyDown(object sender, KeyEventArgs e)
    {
      string loc = CurrentLocation;
      if (loc == null || gridView.IsCurrentCellInEditMode) return;

      if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.None)
      {
        // ProcessCmdKey를 못 거친 경우 대비
        e.Handled = e.SuppressKeyPress = TryBeginEditFromGrid();
      }
      else if (e.KeyCode == Keys.F2)
      {
        if (IsValueEditCell()) return;   // EditInt 등 값 입력 셀은 그리드 기본 편집(값 변경)
        e.Handled = true;
        BeginInlineEdit(loc, null);
      }
      else if (e.KeyCode == Keys.Delete)
      {
        e.Handled = true;
        await CommitAsync(loc, "", true);
      }
      else if (e.Control && e.KeyCode == Keys.Z)
      {
        e.Handled = true;
        await UndoAsync();
      }
      else if (e.Control && (e.KeyCode == Keys.C || e.KeyCode == Keys.X))
      {
        e.Handled = true;
        string expr = await GetExpressionAsync(loc);
        if (string.IsNullOrEmpty(expr)) return;
        Clipboard.SetText(expr);
        _copySourceLocation = loc;
        if (e.KeyCode == Keys.X)
          await CommitAsync(loc, "", true);
        else
          ShowOk(loc + " 식 복사됨 - 붙여넣을 셀에서 Ctrl+V");
      }
      else if (e.Control && e.KeyCode == Keys.V)
      {
        e.Handled = true;
        if (!Clipboard.ContainsText()) return;
        string text = Clipboard.GetText();
        // 이 시트에서 복사한 식이면 상대 참조를 옮긴 거리만큼 보정 ($ 붙은 절대 참조는 그대로)
        int sc, sr, dc, dr;
        if (_copySourceLocation != null && TryParseLocation(_copySourceLocation, out sc, out sr) && TryParseLocation(loc, out dc, out dr))
          text = AdjustReferences(text, dr - sr, dc - sc);
        await CommitAsync(loc, text, true);
      }
    }

    /// <summary>
    /// EditInt/EditFloat/ListBox 같은 값 입력 셀 (그리드가 직접 값 편집) - 타이핑/F2는 그쪽에 맡김. 식 수정은 더블클릭/입력줄
    /// </summary>
    private bool IsValueEditCell()
    {
      return IsValueEditCell(gridView.CurrentCell);
    }

    private static bool IsValueEditCell(DataGridViewCell c)
    {
      return c is InSightEditNumberCell || c is InSightEditStringCell || c is InSightListBoxCell
          || c is InSightCheckBoxCell || c is InSightButtonCell;
    }

    private static string AdjustReferences(string expr, int dRow, int dCol)
    {
      if (dRow == 0 && dCol == 0) return expr;
      var sb = new StringBuilder();
      int i = 0;
      while (i < expr.Length)
      {
        // 문자열 리터럴은 그대로
        if (expr[i] == '"')
        {
          int close = expr.IndexOf('"', i + 1);
          if (close < 0) close = expr.Length - 1;
          sb.Append(expr, i, close - i + 1);
          i = close + 1;
          continue;
        }
        int next = expr.IndexOf('"', i);
        if (next < 0) next = expr.Length;
        string part = expr.Substring(i, next - i);
        sb.Append(_relativeRefRegex.Replace(part, m =>
        {
          char col = char.ToUpperInvariant(m.Groups[2].Value[0]);
          int row = int.Parse(m.Groups[4].Value);
          if (m.Groups[1].Value.Length == 0) col = (char)Math.Max('A', Math.Min('Z', col + dCol));
          if (m.Groups[3].Value.Length == 0) row = Math.Max(0, row + dRow);
          return m.Groups[1].Value + col + m.Groups[3].Value + row;
        }));
        i = next;
      }
      return sb.ToString();
    }

    #endregion

    #region 수식 입력줄 / 주소 상자

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
      if (e.KeyCode != Keys.Enter) return;
      e.SuppressKeyPress = true;
      SelectCell(_addressBox.Text);
    }

    private async void ExprBox_KeyDown(object sender, KeyEventArgs e)
    {
      if (HandleSuggestionKeys(e)) return;

      if (e.KeyCode == Keys.Escape)
      {
        e.SuppressKeyPress = true;
        _exprDirty = false;
        if (_exprLocation != null) ShowSelectedExpression(_exprLocation);
        gridView.Focus();
      }
      else if (e.KeyCode == Keys.Enter)
      {
        e.SuppressKeyPress = true;
        if (_exprLocation == null) return;
        string reason;
        if (!CanEdit(out reason)) { ShowHint(reason, true); return; }
        _exprDirty = false;
        await CommitAsync(_exprLocation, _exprBox.Text, true);
        gridView.Focus();
      }
    }

    #endregion

    #region 적용 / 되돌리기

    private async System.Threading.Tasks.Task<string> GetExpressionAsync(string location)
    {
      string expr;
      if (_expressionCache.TryGetValue(location, out expr)) return expr;
      try { expr = await _inSight.GetCellExpression(location) ?? ""; }
      catch { expr = ""; }
      _expressionCache[location] = expr;
      return expr;
    }

    private async System.Threading.Tasks.Task CommitAsync(string location, string newExpression, bool recordUndo)
    {
      string reason;
      if (!CanEdit(out reason)) { ShowHint(reason, true); return; }

      newExpression = newExpression ?? "";
      string oldExpression = await GetExpressionAsync(location);
      if (oldExpression == newExpression) return;

      try
      {
        await _inSight.SetCellExpression(location, newExpression);
        _expressionCache[location] = newExpression;
        if (recordUndo)
        {
          _undo.Push(new UndoItem { Location = location, Expression = oldExpression });
          if (_undo.Count > 100) TrimUndo();
        }
        ShowOk(location + (newExpression.Length == 0 ? " 비움" : " 적용") + "  (Ctrl+Z = 되돌리기)");
        // 그리드 갱신은 SetCellExpression이 최신 결과를 받아 ResultsChanged로 알려줌 → 여기서 또 받으면 2번 그려서 느려짐
        // 단, 빈 셀은 결과에 아예 안 실려 와서 UpdateGrid가 지우지 못함 → 화면에서 직접 비움
        if (newExpression.Length == 0)
          ClearGridCell(location);

        if (location == _selectedCellLocation)
        {
          ShowSelectedExpression(location);
          UpdateArrows(location);
        }
      }
      catch (Exception ex)
      {
        // 문법 오류 등 센서가 거부하면 원래 식 유지
        ShowHint(location + " 적용 실패: " + ex.Message + "  (식 문법을 확인하세요)", true);
      }
    }

    /// <summary>결과 셀(InSightTextCell/EditInt 등)을 일반 빈 셀로 되돌림 (서식은 유지)</summary>
    private void ClearGridCell(string location)
    {
      int col, row;
      if (!TryParseLocation(location, out col, out row)) return;
      DataGridViewCell old = gridView[col, row];
      if ((old.Tag as string) == "HMI") return;

      var empty = new DataGridViewTextBoxCell();
      SpreadsheetGridViewExtensions.CopyGridViewStyle(old, empty);
      if (old.Tag is Color) empty.Style.ForeColor = (Color)old.Tag;   // 비활성(회색) 표시 해제
      gridView[col, row] = empty;
      empty.Value = "";
      gridView.InvalidateCell(empty);
    }

    private void TrimUndo()
    {
      var keep = _undo.Take(100).Reverse().ToList();
      _undo.Clear();
      foreach (UndoItem u in keep) _undo.Push(u);
    }

    private async System.Threading.Tasks.Task UndoAsync()
    {
      if (_undo.Count == 0) { ShowHint("되돌릴 변경이 없습니다.", false); return; }
      UndoItem u = _undo.Pop();
      await CommitAsync(u.Location, u.Expression, false);
      SelectCell(u.Location);
      ShowOk(u.Location + " 되돌림 (남은 되돌리기 " + _undo.Count + "개)");
    }

    #endregion

    #region 자동완성

    private void OnEditorTextChanged(TextBox box)
    {
      UpdateArgumentHint(box);

      // "..." 문자열 안은 텍스트 → 함수/셀 주소 자동완성 안 띄움 (Enter가 함수명으로 바뀌던 문제)
      if (IsInsideString(box.Text, box.SelectionStart))
      {
        HideSuggestions();
        return;
      }

      string token = GetToken(box.Text, box.SelectionStart, out int start, out int end);
      if (string.IsNullOrEmpty(token) || !char.IsLetter(token[0]))
      {
        HideSuggestions();
        return;
      }

      var items = new List<SuggestItem>();
      items.AddRange(FunctionArgs.Keys
        .Where(f => f.StartsWith(token, StringComparison.OrdinalIgnoreCase))
        .OrderBy(f => f.Length).ThenBy(f => f)
        .Take(15)
        .Select(f => new SuggestItem { Text = f, IsFunction = true }));

      // 셀 주소: "C" 한 글자이거나 "C8" 처럼 글자+숫자일 때
      if (Regex.IsMatch(token, @"^[A-Za-z]\d*$"))
        items.AddRange(_allCellAddresses
          .Where(c => c.StartsWith(token, StringComparison.OrdinalIgnoreCase))
          .Take(token.Length == 1 ? 0 : 10)
          .Select(c => new SuggestItem { Text = c }));

      if (items.Count == 0 || (items.Count == 1 && string.Equals(items[0].Text, token, StringComparison.OrdinalIgnoreCase) && !items[0].IsFunction))
      {
        HideSuggestions();
        return;
      }

      EnsureSuggestDropDown();
      _suggestTarget = box;
      _suggestList.BeginUpdate();
      _suggestList.Items.Clear();
      _suggestList.Items.AddRange(items.Cast<object>().ToArray());
      _suggestList.EndUpdate();
      _suggestList.SelectedIndex = 0;

      if (!_suggestDropDown.Visible)
      {
        _suggestDropDown.Show(box, new Point(0, box.Height));
        box.Focus(); // 드롭다운이 떠도 입력 포커스는 계속 입력칸에
      }
    }

    /// <summary>자동완성이 떠 있을 때 ↑↓ / Enter / Tab / Esc 처리. 처리했으면 true</summary>
    private bool HandleSuggestionKeys(KeyEventArgs e)
    {
      if (_suggestDropDown == null || !_suggestDropDown.Visible) return false;

      switch (e.KeyCode)
      {
        case Keys.Down:
          if (_suggestList.SelectedIndex < _suggestList.Items.Count - 1) _suggestList.SelectedIndex++;
          break;
        case Keys.Up:
          if (_suggestList.SelectedIndex > 0) _suggestList.SelectedIndex--;
          break;
        case Keys.Enter:
        case Keys.Tab:
          AcceptSuggestion();
          break;
        case Keys.Escape:
          HideSuggestions();
          break;
        default:
          return false;
      }
      e.Handled = true;
      e.SuppressKeyPress = true;
      return true;
    }

    private void EnsureSuggestDropDown()
    {
      if (_suggestDropDown != null)
        return;

      _suggestList = new ListBox
      {
        BorderStyle = BorderStyle.None,
        IntegralHeight = false,
        Size = new Size(220, 180),
        Font = new Font("Consolas", 9.5F)
      };
      _suggestList.MouseMove += (s, e) =>
      {
        int index = _suggestList.IndexFromPoint(e.Location);
        if (index >= 0) _suggestList.SelectedIndex = index;
      };
      _suggestList.MouseUp += (s, e) => AcceptSuggestion();

      var host = new ToolStripControlHost(_suggestList)
      {
        Margin = Padding.Empty,
        Padding = Padding.Empty,
        AutoSize = false,
        Size = _suggestList.Size
      };

      _suggestDropDown = new ToolStripDropDown { Padding = Padding.Empty, AutoClose = false };
      _suggestDropDown.Items.Add(host);
    }

    private void HideSuggestions()
    {
      _suggestDropDown?.Close();
    }

    private void AcceptSuggestion()
    {
      TextBox box = _suggestTarget;
      var item = _suggestList?.SelectedItem as SuggestItem;
      if (box == null || item == null)
        return;

      GetToken(box.Text, box.SelectionStart, out int start, out int end);
      string insert = item.Text + (item.IsFunction && (end >= box.Text.Length || box.Text[end] != '(') ? "(" : "");
      box.Text = box.Text.Substring(0, start) + insert + box.Text.Substring(end);
      box.SelectionStart = start + insert.Length;

      HideSuggestions();
      box.Focus();
      UpdateArgumentHint(box);
    }

    /// <summary>caret 앞의 따옴표 개수가 홀수면 문자열 리터럴 안</summary>
    private static bool IsInsideString(string text, int caret)
    {
      int end = Math.Min(caret, text.Length);
      bool inString = false;
      for (int i = 0; i < end; i++)
        if (text[i] == '"') inString = !inString;
      return inString;
    }

    private static string GetToken(string text, int caret, out int start, out int end)
    {
      caret = Math.Min(caret, text.Length);

      start = caret;
      while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_')) start--;

      end = caret;
      while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;

      return text.Substring(start, end - start);
    }

    #endregion

    #region 함수 패널

    private void ToggleFunctionPanel()
    {
      _funcPanel.Visible = !_funcPanel.Visible;
      _funcButton.Text = _funcPanel.Visible ? "함수 ◂" : "함수 ▸";
      if (_funcPanel.Visible)
      {
        FillFunctionList();
        _funcFilter.Focus();
      }
      PositionInlineBox();
    }

    private void FillFunctionList()
    {
      string filter = _funcFilter.Text.Trim();
      _funcList.BeginUpdate();
      _funcList.Items.Clear();
      foreach (string f in FunctionArgs.Keys.OrderBy(k => k))
        if (filter.Length == 0 || f.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
          _funcList.Items.Add(f);
      _funcList.EndUpdate();
      if (_funcList.Items.Count > 0) _funcList.SelectedIndex = 0;
    }

    private void ShowFunctionSignature()
    {
      string f = _funcList.SelectedItem as string;
      string[] args;
      if (f == null || !FunctionArgs.TryGetValue(f, out args)) { _funcSignature.Text = ""; return; }
      _funcSignature.Text = f + "(\n  " + string.Join(",\n  ", args) + "\n)";
    }

    private void InsertSelectedFunction()
    {
      string f = _funcList.SelectedItem as string;
      if (f == null) return;
      string insert = f + "(";

      TextBox target = _inlineHost.Visible ? _inlineBox : (_exprDirty ? _exprBox : null);
      if (target != null)
      {
        int pos = target.SelectionStart;
        target.Text = target.Text.Substring(0, pos) + insert + target.Text.Substring(pos);
        target.SelectionStart = pos + insert.Length;
        target.Focus();
        UpdateArgumentHint(target);
        return;
      }

      // 편집 중이 아니면 선택 셀에서 새로 입력 시작
      BeginInlineEdit(CurrentLocation, insert);
    }

    #endregion
  }
}
