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

        public CameraControlModel()
        {
        }

        public async Task<bool> ConnectAsync(CvsInSight InSightSensor, string ip, string user, string password)
        {
            try
            {
                var sessionInfo = new HmiSessionInfo { SheetName = "Inspection" };

                await InSightSensor.Connect(ip, user, password, sessionInfo);

                return InSightSensor.Connected;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Message: {ex.Message}");
                if (ex.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Inner Message: {ex.InnerException.Message}");
                    System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.InnerException.StackTrace}");
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
    }
}
