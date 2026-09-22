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
        }

        // 타일 헤더를 드래그하면 이동
        private void TileHeader_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is SensorSessionViewModel vm)
            {
                vm.X = Math.Max(0, vm.X + e.HorizontalChange);
                vm.Y = Math.Max(0, vm.Y + e.VerticalChange);
            }
        }

        // 타일 모서리 그립을 드래그하면 크기 조절
        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is SensorSessionViewModel vm)
            {
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
