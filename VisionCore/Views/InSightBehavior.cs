using Cognex.InSight.Web;
using Cognex.InSight.Web.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms.Integration;
using VisionCore.Models;

namespace VisionCore.Views
{
    public static class InSightBehavior
    {
        public static CvsInSight GetSensorSource(DependencyObject obj) => (CvsInSight)obj.GetValue(SensorSourceProperty);
        public static void SetSensorSource(DependencyObject obj, CvsInSight value) => obj.SetValue(SensorSourceProperty, value);

        public static readonly DependencyProperty SensorSourceProperty =
            DependencyProperty.RegisterAttached("SensorSource", typeof(CvsInSight), typeof(InSightBehavior), new PropertyMetadata(null, OnSensorSourceChanged));

        private static void OnSensorSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WindowsFormsHost host)
            {
                var sensor = e.NewValue as CvsInSight;
                if (sensor == null) return;

                // 타일이 동적으로 생성되는 경우, WindowsFormsHost의 네이티브 창이 아직
                // 만들어지기 전에 바인딩이 먼저 적용될 수 있어 초기화가 씹힐 수 있음.
                // Loaded 이후(핸들 생성 보장)로 초기화를 미룸.
                void Init()
                {
                    if (host.Child is CvsDisplay display)
                    {
                        display.SetInSight(sensor);

                        sensor.ResultsChanged += async (s, ev) =>
                        {
                            display.InitDisplay();
                            await display.OnConnected();
                        };

                        display.InitDisplay();
                        _ = sensor.SendReady();

                        // SendReady는 "다음 결과부터 받겠다"는 신호일 뿐, 현재 이미지를 바로 가져오지
                        // 않음. 새 트리거가 올 때까지 화면이 비어있게 되므로 연결 직후 현재 이미지를
                        // 직접 한 번 가져와서 그려줌.
                        _ = display.OnConnected();

                        // 결과 목록에서 이미지를 클릭하면 이 센서의 디스플레이에 미리보기로 띄워줌
                        if (host.DataContext is VisionCore.ViewModels.SensorSessionViewModel session)
                        {
                            session.ShowImage = path => display.LoadLocalImage(path);
                        }
                    }
                    else if (host.Child is CvsSpreadsheet spreadsheet)
                    {
                        spreadsheet.SetInSight(sensor);

                        sensor.ConnectedChanged += (s, ev) =>
                        {
                            if (sensor.Connected)
                            {
                                spreadsheet.BeginInvoke((Action)(() => spreadsheet.InitSpreadsheet()));
                            }
                        };

                        // InitSpreadsheet은 연결 시점에 한 번만 그려주고, 이후 새 결과가 와도
                        // 그리드가 갱신되지 않던 문제 -> 결과가 바뀔 때마다 그리드도 새로고침
                        sensor.ResultsChanged += (s, ev) =>
                        {
                            if (sensor.Results != null)
                            {
                                spreadsheet.UpdateResults(sensor.Results);
                            }
                        };

                        if (sensor.Connected)
                        {
                            spreadsheet.BeginInvoke((Action)(() => spreadsheet.InitSpreadsheet()));
                        }
                    }
                }

                if (host.IsLoaded) Init();
                else host.Loaded += (s, e2) => Init();
            }
        }
        public static bool GetIsOverlayVisible(DependencyObject obj) => (bool)obj.GetValue(IsOverlayVisibleProperty);
        public static void SetIsOverlayVisible(DependencyObject obj, bool value) => obj.SetValue(IsOverlayVisibleProperty, value);
        public static readonly DependencyProperty IsOverlayVisibleProperty =
            DependencyProperty.RegisterAttached(
                "IsOverlayVisible",
                typeof(bool),
                typeof(InSightBehavior),
                new PropertyMetadata(true, OnIsOverlayVisibleChanged));

        private static void OnIsOverlayVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WindowsFormsHost host && host.Child is CvsDisplay display)
            {
                display.ToggleOverlay((bool)e.NewValue);
            }
        }

    }
}
