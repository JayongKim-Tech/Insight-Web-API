using Cognex.InSight.Remoting.Serialization;
using Cognex.InSight.Web;
using Cognex.InSight.Web.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VisionCore.Models
{
    /// <summary>
    /// 탐색된 Cognex 비전 장치 정보
    /// </summary>
    public class DiscoveredDevice
    {
        public string DisplayName { get; set; } // UI 표시용 이름
        public string IpAddress { get; set; }   // IP 주소
        public int Port { get; set; }           // 포트 번호
        public bool IsEmulator { get; set; }    // 에뮬레이터 여부

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// 카메라 통신 및 장치 스캔 통합 컨트롤 모델
    /// </summary>
    public class CameraControlModel
    {
        private static CameraControlModel _instance;
        public static CameraControlModel Instance => _instance ?? (_instance = new CameraControlModel());

        // Cognex Web API SDK 객체
        public CvsInSight IsInSightSensor { get; } = new CvsInSight();
        public CvsDisplay CvsDisplay { get; } = new CvsDisplay();

        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
        private const int DefaultCameraPort = 51150;

        private CameraControlModel() { }

        #region 카메라 연결 / 해제

        /// <summary>
        /// 지정된 IP 및 Port로 연결을 시도합니다.
        /// </summary>
        public async Task<bool> ConnectAsync(string ip, string user, string password)
        {
            try
            {
                var sessionInfo = new HmiSessionInfo
                {
                    SheetName = "Inspection",
                    CellNames = new string[1] { "A0:Z599" }
                };

                await IsInSightSensor.Connect(ip, user, password, sessionInfo);
                return IsInSightSensor.Connected;
            }
            catch (Exception ex)
            {
                Logger.Error($"연결 실패: {ex.Message}");
                System.Windows.MessageBox.Show($"연결 실패: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Logger.Error($"Inner Message: {ex.InnerException.Message}");
                    Logger.Error($"Stack Trace: {ex.InnerException.StackTrace}");
                }
                return false;
            }
        }

        /// <summary>
        /// 연결을 해제합니다.
        /// </summary>
        public async Task DisconnectAsync(CvsInSight InSightSensor)
        {
            if (IsInSightSensor.Connected)
            {
                await IsInSightSensor.Disconnect();
            }
        }

        #endregion

        #region 장치 스캔 (Scanner 통합)

        /// <summary>
        /// 실행 중인 로컬 에뮬레이터 및 네트워크 내 실제 카메라 목록을 통합 반환합니다.
        /// </summary>
        public async Task<List<DiscoveredDevice>> ScanDevicesAsync()
        {
            var deviceList = new List<DiscoveredDevice>();

            // 1. 로컬 에뮬레이터 스캔
            var emulator = await ScanLocalEmulatorAsync();
            if (emulator != null)
            {
                deviceList.Add(emulator);
            }

            // 2. 네트워크 상의 실제 카메라 스캔
            var networkCameras = await ScanNetworkCamerasAsync();
            deviceList.AddRange(networkCameras);

            return deviceList;
        }

        private async Task<DiscoveredDevice> ScanLocalEmulatorAsync()
        {
            string[] targetProcessPrefixes = new string[]
            {
                "Cognex.Explorer",
                "InSight.Simulator",
                "InSightEmulator",
                "InSight"
            };

            var candidateProcesses = Process.GetProcesses()
                .Where(p => targetProcessPrefixes.Any(prefix => p.ProcessName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (!candidateProcesses.Any()) return null;

            foreach (var proc in candidateProcesses)
            {
                var ports = GetPortsByPid(proc.Id);
                foreach (int port in ports)
                {
                    if (await IsValidHmiPortAsync("127.0.0.1", port))
                    {
                        return new DiscoveredDevice
                        {
                            DisplayName = $"[Emulator] 127.0.0.1:{port}",
                            IpAddress = "127.0.0.1",
                            Port = port,
                            IsEmulator = true
                        };
                    }
                }
            }

            return null;
        }

        private async Task<List<DiscoveredDevice>> ScanNetworkCamerasAsync()
        {
            string subnet = GetLocalSubnet();
            if (string.IsNullOrEmpty(subnet)) return new List<DiscoveredDevice>();

            var tasks = new List<Task<DiscoveredDevice>>();
            for (int i = 1; i <= 254; i++)
            {
                string targetIp = subnet + i;
                tasks.Add(CheckCameraAsync(targetIp, DefaultCameraPort));
            }

            var results = await Task.WhenAll(tasks);
            return results.Where(d => d != null).ToList();
        }

        private async Task<DiscoveredDevice> CheckCameraAsync(string ip, int port)
        {
            if (await IsValidHmiPortAsync(ip, port))
            {
                return new DiscoveredDevice
                {
                    DisplayName = $"[Camera] {ip}:{port}",
                    IpAddress = ip,
                    Port = port,
                    IsEmulator = false
                };
            }
            return null;
        }

        private async Task<bool> IsValidHmiPortAsync(string ip, int port)
        {
            try
            {
                var response = await _httpClient.GetAsync($"http://{ip}:{port}");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private List<int> GetPortsByPid(int pid)
        {
            var ports = new List<int>();
            using (Process p = new Process())
            {
                p.StartInfo = new ProcessStartInfo("netstat.exe", "-ano")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                p.Start();
                string output = p.StandardOutput.ReadToEnd();

                foreach (string line in output.Split('\n'))
                {
                    if (line.Contains("LISTENING") && line.Contains(pid.ToString()))
                    {
                        var match = Regex.Match(line, @":(\d+)\s+");
                        if (match.Success)
                        {
                            ports.Add(int.Parse(match.Groups[1].Value));
                        }
                    }
                }
            }
            return ports.Distinct().ToList();
        }

        private string GetLocalSubnet()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            var ip = host.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ip == null) return string.Empty;

            string ipStr = ip.ToString();
            return ipStr.Substring(0, ipStr.LastIndexOf('.') + 1);
        }

        #endregion
    }
}