using Cognex.InSight.Web;
using Cognex.InSight.Web.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms.Integration;

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

                if (host.Child is CvsDisplay display)
                {

                    display.SetInSight(sensor);

                    if (sensor != null)
                    {

                        sensor.ResultsChanged += async (s, ev) =>
                        {

                            display.InitDisplay();

                            await display.OnConnected();
                        };


                        if (sensor.Connected)
                        {
                            display.InitDisplay();
                            _ = sensor.SendReady();
                        }
                    }
                }

                else if (host.Child is CvsSpreadsheet spreadsheet)
                {
                    spreadsheet.SetInSight(sensor);

                    if (sensor != null)
                    {
                        sensor.ConnectedChanged += (s, ev) =>
                        {
                            if (sensor.Connected)
                            {
                                spreadsheet.BeginInvoke((Action)(() => spreadsheet.InitSpreadsheet()));
                            }
                        };

                        if (sensor.Connected)
                        {
                            spreadsheet.BeginInvoke((Action)(() => spreadsheet.InitSpreadsheet()));
                        }
                    }
                }
            }
        }
    }
}
