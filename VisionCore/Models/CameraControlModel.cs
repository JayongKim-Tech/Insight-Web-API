using Cognex.InSight.Remoting.Serialization;
using Cognex.InSight.Web;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionCore.Models
{
    public class CameraControlModel
    {

        private static CameraControlModel _instance;
        public static CameraControlModel Instance => _instance ?? (_instance = new CameraControlModel());

        public CvsInSight IsInSightSensor { get; } = new CvsInSight();
        private CameraControlModel() { }

        #region 카메라 통신
        public async Task<bool> ConnectAsync(CvsInSight InSightSensor, string ip, string user, string password)
        {
            try
            {
                var sessionInfo = new HmiSessionInfo
                {
                    SheetName = "Inspection",
                    CellNames = new string[1] { "A0:Z599" }, // Designating a cell range requires 6.3 or newer firmware
                };

                await InSightSensor.Connect(ip, user, password, sessionInfo);

                return InSightSensor.Connected;
            }
            catch (Exception ex)
            {

                Logger.Error($"연결 실패: {ex.Message}"); // 에러 로그
                System.Windows.MessageBox.Show($"연결 실패: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Logger.Error($"Inner Message: {ex.InnerException.Message}");
                    Logger.Error($"Stack Trace: {ex.InnerException.StackTrace}");
                }
                return false;
            }
        }

        public async Task DisconnectAsync(CvsInSight InSightSensor)
        {
            if (InSightSensor.Connected)
            {
                await InSightSensor.Disconnect();
            }
        }

        #endregion

    }
}
