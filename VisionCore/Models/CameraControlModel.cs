using Cognex.InSight.Remoting.Serialization;
using Cognex.InSight.Web;
using Cognex.InSight.Web.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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
        // Cognex Web API SDK 객체 (센서 1개 = 인스턴스 1개)
        public CvsInSight IsInSightSensor { get; } = new CvsInSight();

        private const int DefaultCameraPort = 80;

        public CameraControlModel() { }

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

        // TCP 포트 열림 확인 대기 시간 (같은 LAN 장비는 수 ms 안에 응답)
        private const int TcpConnectTimeoutMs = 300;

        /// <summary>
        /// 실행 중인 로컬 에뮬레이터 및 네트워크 내 실제 카메라 목록을 통합 반환합니다.
        /// (에뮬레이터/네트워크 스캔을 동시에 진행)
        /// </summary>
        public static async Task<List<DiscoveredDevice>> ScanDevicesAsync()
        {
            var emulatorTask = Task.Run(() => ScanLocalEmulatorAsync());
            var networkTask = Task.Run(() => ScanNetworkCamerasAsync());

            await Task.WhenAll(emulatorTask, networkTask);

            var deviceList = new List<DiscoveredDevice>();
            if (emulatorTask.Result != null) deviceList.Add(emulatorTask.Result);
            deviceList.AddRange(networkTask.Result);
            return deviceList;
        }

        private static async Task<DiscoveredDevice> ScanLocalEmulatorAsync()
        {
            string[] targetProcessPrefixes = new string[]
            {
                "Cognex.Explorer",
                "InSight.Simulator",
                "InSightEmulator",
                "InSight"
            };

            var pids = new HashSet<int>();
            foreach (var p in Process.GetProcesses())
            {
                using (p) // Process 핸들 누수 방지
                {
                    if (targetProcessPrefixes.Any(prefix => p.ProcessName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                        pids.Add(p.Id);
                }
            }

            if (pids.Count == 0) return null;

            // netstat 실행 대신 OS TCP 테이블을 직접 1회 조회 (PID별 LISTEN 포트)
            var ports = GetListeningPortsByPids(pids);
            if (ports.Count == 0) return null;

            // 후보 포트를 동시에 확인하고, 낮은 포트 번호 우선으로 선택 (기존 순차 확인과 같은 결과)
            var checks = ports.Select(async port => new { port, ok = await IsValidHmiPortAsync("127.0.0.1", port) }).ToList();
            var results = await Task.WhenAll(checks);
            var found = results.Where(r => r.ok).OrderBy(r => r.port).FirstOrDefault();
            if (found == null) return null;

            return new DiscoveredDevice
            {
                DisplayName = $"[Emulator] 127.0.0.1:{found.port}",
                IpAddress = "127.0.0.1",
                Port = found.port,
                IsEmulator = true
            };
        }

        private static async Task<List<DiscoveredDevice>> ScanNetworkCamerasAsync()
        {
            var subnets = GetLocalSubnets();
            if (subnets.Count == 0) return new List<DiscoveredDevice>();

            var tasks = new List<Task<DiscoveredDevice>>();
            foreach (string subnet in subnets)
            {
                for (int i = 1; i <= 254; i++)
                {
                    tasks.Add(CheckCameraAsync(subnet + i, DefaultCameraPort));
                }
            }

            var results = await Task.WhenAll(tasks);
            return results.Where(d => d != null).ToList();
        }

        private static async Task<DiscoveredDevice> CheckCameraAsync(string ip, int port)
        {
            // 1단계: TCP 포트가 열린 장비만 골라냄 (없는 IP는 300ms 후 바로 포기)
            if (!await IsTcpPortOpenAsync(ip, port)) return null;

            // 2단계: 열린 장비만 HTTP로 HMI 응답 확인
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

        private static async Task<bool> IsTcpPortOpenAsync(string ip, int port)
        {
            using (var client = new TcpClient())
            {
                try
                {
                    var connectTask = client.ConnectAsync(ip, port);
                    var finished = await Task.WhenAny(connectTask, Task.Delay(TcpConnectTimeoutMs));
                    if (finished != connectTask)
                    {
                        // 시간 초과: client Dispose 시 연결 시도 중단. 남은 예외는 관찰 처리
                        _ = connectTask.ContinueWith(t => { var _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                        return false;
                    }

                    await connectTask;
                    return client.Connected;
                }
                catch
                {
                    return false;
                }
            }
        }

        // Cognex HMI 웹 페이지인지 확인 (카메라/에뮬레이터 모두 <title>Cognex HMI</title> 응답)
        // 공유기·프린터·사내 웹서버 등 80 포트가 열린 다른 장비는 제외
        private static async Task<bool> IsValidHmiPortAsync(string ip, int port)
        {
            try
            {
                using (var response = await _scanHttpClient.GetAsync($"http://{ip}:{port}"))
                {
                    if (!response.IsSuccessStatusCode) return false;

                    string body = await response.Content.ReadAsStringAsync();
                    return body.IndexOf("Cognex", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
                return false;
            }
        }

        // 스캔 전용 HttpClient: 프록시 자동 검색(WPAD) 비활성화 - 첫 요청이 수 초씩 지연되는 주원인
        // Cognex HMI는 요청 헤더와 무관하게 gzip으로 응답하므로 자동 압축 해제 필요
        private static readonly HttpClient _scanHttpClient = new HttpClient(new HttpClientHandler
        {
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromMilliseconds(1500)
        };

        /// <summary>
        /// OS TCP 테이블(GetExtendedTcpTable)에서 지정한 PID들이 LISTEN 중인 IPv4 포트 목록을 가져옴
        /// </summary>
        private static List<int> GetListeningPortsByPids(HashSet<int> pids)
        {
            var ports = new List<int>();
            int size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0);

            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(buffer, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0) != 0) return ports;

                int count = Marshal.ReadInt32(buffer);
                IntPtr row = buffer + 4;
                const int rowSize = 24; // MIB_TCPROW_OWNER_PID: state, localAddr, localPort, remoteAddr, remotePort, owningPid

                for (int i = 0; i < count; i++, row += rowSize)
                {
                    int pid = Marshal.ReadInt32(row, 20);
                    if (!pids.Contains(pid)) continue;

                    int rawPort = Marshal.ReadInt32(row, 8);
                    int port = ((rawPort & 0xFF) << 8) | ((rawPort >> 8) & 0xFF); // 네트워크 바이트 순서 -> 호스트
                    ports.Add(port);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return ports.Distinct().ToList();
        }

        private const int AF_INET = 2;
        private const int TCP_TABLE_OWNER_PID_LISTENER = 3;

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, uint reserved);

        /// <summary>
        /// 연결된 모든 네트워크 어댑터의 IPv4 /24 대역 (예: "192.168.0.") - 카메라가 다른 랜카드 쪽에 있어도 탐색
        /// </summary>
        private static List<string> GetLocalSubnets()
        {
            var subnets = new List<string>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                    foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;

                        string ipStr = addr.Address.ToString();
                        if (ipStr.StartsWith("169.254.")) continue; // DHCP 실패 시 자동 할당 주소

                        string subnet = ipStr.Substring(0, ipStr.LastIndexOf('.') + 1);
                        if (!subnets.Contains(subnet)) subnets.Add(subnet);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"네트워크 어댑터 조회 실패: {ex.Message}");
            }
            return subnets;
        }

        #endregion
    }
}