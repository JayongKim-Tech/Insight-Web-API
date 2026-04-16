using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
        public CameraControlModel controlModel => CameraControlModel.Instance;

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = new MainViewModel();
        }

        private void Display_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private async void Display_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {

                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

                if (files != null && files.Length > 0)
                {
                    string filePath = files[0]; // 첫 번째 파일 경로

                    // 확장자가 .jobx 인지 체크 (선택 사항)
                    if (string.Equals(System.IO.Path.GetExtension(filePath), ".jobx", StringComparison.OrdinalIgnoreCase))
                    {
                        await controlModel.IsInSightSensor.LoadJob(filePath);
                        logger.Info($"Job파일 Load: {filePath}");
                    }
                    else
                    {
                        System.Windows.MessageBox.Show(".jobx 확장자 확인 필요..");
                    }
                }
            }
        }
    }


}
