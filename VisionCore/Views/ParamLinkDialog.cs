using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using VisionCore.ViewModels;

namespace VisionCore.Views
{
    /// <summary>
    /// 공용 파라미터 연결: 같은 함수를 쓰는 툴 중 같이 묶을 툴 선택 + 공용 이름
    /// </summary>
    public class ParamLinkDialog : Window
    {
        private readonly TextBox _name = new TextBox { Height = 24, VerticalContentAlignment = VerticalAlignment.Center };
        private readonly List<KeyValuePair<CheckBox, ToolItemViewModel>> _checks = new List<KeyValuePair<CheckBox, ToolItemViewModel>>();

        public string SharedName => _name.Text;
        public List<ToolItemViewModel> Selected => _checks.Where(c => c.Key.IsChecked == true).Select(c => c.Value).ToList();

        public ParamLinkDialog(ToolParamViewModel param, ToolItemViewModel source, IList<ToolItemViewModel> candidates, int paramIndex)
        {
            Title = "공용 파라미터 연결 - " + source.Cell.FunctionName + " · " + param.Label;
            Width = 520;
            Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;

            var root = new DockPanel { Margin = new Thickness(12) };

            var top = new StackPanel();
            DockPanel.SetDock(top, Dock.Top);
            top.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                Text = param.IsShared
                    ? $"이미 공용 파라미터 '{param.SharedName}' 에 연결되어 있습니다. 체크한 툴을 같은 값에 추가로 연결합니다."
                    : $"기준: {source.Point.Info.Code} {source.Cell.Location} (현재 값 {param.Text})\n" +
                      "스프레드시트 'SHARED PARAM' 아래에 공용 셀을 만들고, 체크한 툴의 이 인자가 그 셀을 참조하게 바꿉니다."
            });
            if (!param.IsShared)
            {
                top.Children.Add(new TextBlock { Text = "공용 이름", Margin = new Thickness(0, 0, 0, 2) });
                _name.Text = source.Cell.FunctionName + "." + param.Label;
                top.Children.Add(_name);
            }

            var allRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 4) };
            var all = new CheckBox { Content = "전체 선택", VerticalAlignment = VerticalAlignment.Center };
            all.Click += (s, e) => { foreach (KeyValuePair<CheckBox, ToolItemViewModel> c in _checks) c.Key.IsChecked = all.IsChecked; };
            allRow.Children.Add(all);
            top.Children.Add(allRow);
            root.Children.Add(top);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            var ok = new Button { Content = "연결", Width = 90, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            ok.Click += (s, e) =>
            {
                if (Selected.Count == 0 && param.IsShared) { MessageBox.Show(this, "연결할 툴을 선택하세요."); return; }
                DialogResult = true;
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(new Button { Content = "취소", Width = 90, Height = 28, IsCancel = true });
            root.Children.Add(buttons);

            var list = new StackPanel();
            if (candidates.Count == 0)
                list.Children.Add(new TextBlock { Text = "같은 함수를 쓰는 다른 툴이 없습니다.", Foreground = System.Windows.Media.Brushes.Gray });
            foreach (ToolItemViewModel t in candidates)
            {
                string text = t.Cell.Params[paramIndex].Arg.Text;
                string value = t.ResolveCellText(t.Cell.Params[paramIndex].Arg);
                var cb = new CheckBox
                {
                    Margin = new Thickness(0, 2, 0, 2),
                    Content = $"{t.Point.Info.Code}  {t.Point.Info.Name}   {t.Cell.Location}   현재: {text}" + (value != null ? " (= " + value + ")" : "") + (t.IsHelper ? "   [보조]" : "")
                };
                _checks.Add(new KeyValuePair<CheckBox, ToolItemViewModel>(cb, t));
                list.Children.Add(cb);
            }
            root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            Content = root;
        }
    }
}
