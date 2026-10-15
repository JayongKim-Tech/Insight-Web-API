using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Cognex.InSight.Web.Controls;
using VisionCore.ViewModels;

namespace VisionCore.Views
{
    public partial class ToolSetupWindow : Window
    {
        // 오버레이 탭의 이미지/도형 편집 화면 (WinForms 컨트롤 - 팝업 편집기와 같은 것)
        private readonly ShapeEditorControl _overlayEditor = new ShapeEditorControl { LiveCommit = true };
        private OverlayViewModel _overlay;

        public ToolSetupWindow()
        {
            InitializeComponent();
            OverlayHost.Child = _overlayEditor;
            _overlayEditor.Committed += async shape =>
            {
                if (_overlay != null) await _overlay.CommitAsync(_overlay.SelectedItem, shape);
            };
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ToolSetupViewModel vm)
            {
                HookOverlay(vm);
                await vm.InitializeAsync();
            }
        }

        private void HookOverlay(ToolSetupViewModel vm)
        {
            _overlay = vm.Overlay;
            _overlayEditor.SetSensor(vm.Sensor);
            _overlay.GraphicsChanged += () => _overlayEditor.SetContext(_overlay.Graphics);
            _overlay.ImageReloadRequested += async () => await _overlayEditor.ReloadAsync();
            _overlay.RedrawRequested += () => _overlayEditor.RedrawAll();
            _overlay.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(OverlayViewModel.SelectedItem)) return;
                OverlayItemViewModel item = _overlay.SelectedItem;
                // 편집용 복사본 (센서 값은 놓을 때 CommitAsync로만 바뀜)
                _overlayEditor.SetShape(item?.Location, item != null ? ShapeEditorControl.ToEditable(item.Shape) : null);
                _overlayEditor.SetLinkedLocations(item?.Linked.Select(l => l.Location));
            };
        }

        // 오버레이 탭을 처음 열 때 Edit 셀 읽기
        private async void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.OriginalSource != MainTabs || _overlay == null) return;
            if (MainTabs.SelectedIndex == 1) await _overlay.LoadAsync(false);
            else if (MainTabs.SelectedIndex >= 2 && DataContext is ToolSetupViewModel vm) await vm.Align.LoadAsync(false);
        }

        private async void OverlayReloadImage_Click(object sender, RoutedEventArgs e)
        {
            await _overlayEditor.ReloadAsync();
        }

        // 저장 안 된 변경이 있으면 닫기 전에 확인 (센서에는 이미 반영돼 있지만 재부팅/잡 재로드 시 사라짐)
        private bool _closeConfirmed;

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_closeConfirmed || !(DataContext is ToolSetupViewModel vm) || vm.UnsavedCount == 0) return;

            MessageBoxResult r = MessageBox.Show(this,
                $"저장 안 된 변경이 {vm.UnsavedCount}건 있습니다.\n센서의 잡에 저장할까요?\n\n(아니요: 센서에는 반영된 상태로 남지만, 재부팅하거나 잡을 다시 열면 사라집니다)",
                "저장 확인", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (r == MessageBoxResult.Cancel) { e.Cancel = true; return; }
            if (r == MessageBoxResult.No) return;

            e.Cancel = true;
            if (await vm.SaveAsync())
            {
                _closeConfirmed = true;
                Close();
            }
        }

        private void FitImage_Click(object sender, RoutedEventArgs e)
        {
            RoiView.FitToView();
        }

        // 카드 클릭 → 그 툴 ROI 선택 (이미지에서 핸들 표시)
        private void ToolCard_Select(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ToolSetupViewModel vm && ((FrameworkElement)sender).DataContext is ToolItemViewModel t)
                vm.SelectedTool = t;
        }

        // 파라미터에 마우스를 올리거나 입력칸 포커스 → 설명 패널 갱신
        private void Param_ShowHelp(object sender, RoutedEventArgs e)
        {
            if (DataContext is ToolSetupViewModel vm && ((FrameworkElement)sender).DataContext is ToolParamViewModel p)
                vm.HelpParam = p;
        }

        // 🔗 공용 파라미터 연결: 같은 함수 툴 목록에서 묶을 툴 선택
        private async void ParamLink_Click(object sender, RoutedEventArgs e)
        {
            if (!(DataContext is ToolSetupViewModel vm) || !(((FrameworkElement)sender).DataContext is ToolParamViewModel p)) return;
            ToolItemViewModel tool = p.Tool;
            vm.IsBusy = true;
            System.Collections.Generic.List<ToolItemViewModel> candidates;
            try { candidates = await vm.GetSameFunctionToolsAsync(tool, p.Index); }
            finally { vm.IsBusy = false; }

            var dlg = new ParamLinkDialog(p, tool, candidates, p.Index) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            await vm.LinkParamAsync(tool, p, dlg.Selected, dlg.SharedName);
        }

        // 이 툴만 공용 연결 해제 (현재 값을 상수로)
        private void ParamUnlink_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is ToolParamViewModel p) p.Unlink();
        }

        // 참조한 Button 셀 누르기 (Train 등)
        private void ParamPress_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is ToolParamViewModel p) p.PressButton();
        }

        private void ParamCreateButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(((FrameworkElement)sender).DataContext is ToolParamViewModel p)) return;
            if (MessageBox.Show(this, $"{p.Tool.Cell.Location} 오른쪽 빈 셀에 Button 을 만들고 '{p.FullName}' 을(를) 연결할까요?",
                    "버튼 만들기", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
                p.CreateButton();
        }

        // 입력칸에서 Enter = 바로 적용
        private void ValueBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            var box = (TextBox)sender;
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            box.SelectAll();
        }
    }
}
