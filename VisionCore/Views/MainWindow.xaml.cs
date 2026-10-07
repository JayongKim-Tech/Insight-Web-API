using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using VisionCore.Models;
using VisionCore.ViewModels;

namespace VisionCore.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public LoggerService logger => LoggerService.Instance;

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = new MainViewModel();

            // 시작 시 이미 최대화 상태라 StateChanged가 안 오므로 한 번 적용
            SourceInitialized += (s, e) => Window_StateChanged(this, EventArgs.Empty);
        }

        // 대시보드 크기를 ViewModel에 전달 (Arrange Tiles 시 분할 기준)
        private void SensorScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!(DataContext is MainViewModel vm)) return;

            vm.ViewportWidth = e.NewSize.Width;
            vm.ViewportHeight = e.NewSize.Height;

            // 전체화면 타일이 있으면 그 타일만 새 크기에 맞춤, 아니면 정렬 상태일 때 자동 재정렬 (로그 탭 열기/창 크기 변경)
            var maximized = vm.Sensors.FirstOrDefault(s => s.IsMaximized);
            if (maximized != null)
            {
                maximized.Width = Math.Max(e.NewSize.Width, 400);
                maximized.Height = Math.Max(e.NewSize.Height, 300);
            }
            else if (vm.IsAutoArranged)
            {
                vm.ArrangeTiles();
            }
        }

        // 사용자가 직접 타일을 옮기거나 크기를 바꾸면 자동 재정렬 중지 (Arrange 버튼으로 다시 켜짐)
        private void StopAutoArrange()
        {
            if (DataContext is MainViewModel vm) vm.IsAutoArranged = false;
        }

        // 창 최소화 (검사/결과 기록은 백그라운드에서 계속 진행)
        private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        // 최대화 <-> 창 모드 전환 (헤더 더블클릭과 같은 동작)
        private void MaximizeWindow_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        // WindowChrome 창은 최대화 시 테두리 두께만큼 화면 밖으로 넘어가므로, 그만큼 안쪽 여백을 줘서 헤더/하단이 잘리지 않게 함
        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                double frame = (GetSystemMetrics(SM_CXFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER)) / scale;
                RootPanel.Margin = new Thickness(frame);
            }
            else
            {
                RootPanel.Margin = new Thickness(0);
            }
        }

        private const int SM_CXFRAME = 32;
        private const int SM_CXPADDEDBORDER = 92;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        // 타일 헤더를 드래그하면 이동
        private void TileHeader_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is SensorSessionViewModel vm)
            {
                if (vm.IsMaximized) return;
                StopAutoArrange();
                vm.X = Math.Max(0, vm.X + e.HorizontalChange);
                vm.Y = Math.Max(0, vm.Y + e.VerticalChange);
            }
        }

        // 타일 모서리 그립을 드래그하면 크기 조절
        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is SensorSessionViewModel vm)
            {
                if (vm.IsMaximized) return;
                StopAutoArrange();
                vm.Width = Math.Max(280, vm.Width + e.HorizontalChange);
                vm.Height = Math.Max(220, vm.Height + e.VerticalChange);
            }
        }

        // 타일을 대시보드 전체 크기로 전체화면 토글
        private void MaximizeTile_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || !(fe.DataContext is SensorSessionViewModel vm)) return;

            if (!vm.IsMaximized)
            {
                vm.PrevX = vm.X;
                vm.PrevY = vm.Y;
                vm.PrevWidth = vm.Width;
                vm.PrevHeight = vm.Height;

                vm.X = 0;
                vm.Y = 0;
                vm.Width = Math.Max(SensorScrollViewer.ActualWidth, 400);
                vm.Height = Math.Max(SensorScrollViewer.ActualHeight, 300);
                vm.ZIndex = 100;
                vm.IsMaximized = true;
            }
            else if (DataContext is MainViewModel main && main.IsAutoArranged)
            {
                // 정렬 상태였으면 이전 좌표 대신 현재 대시보드 크기 기준으로 다시 정렬 (전체화면 중 크기가 바뀌었을 수 있음)
                main.ArrangeTiles();
            }
            else
            {
                vm.X = vm.PrevX;
                vm.Y = vm.PrevY;
                vm.Width = vm.PrevWidth;
                vm.Height = vm.PrevHeight;
                vm.ZIndex = 0;
                vm.IsMaximized = false;
            }
        }
    }


}
