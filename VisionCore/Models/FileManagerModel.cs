using Cognex.InSight.Web;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Remoting.Channels;
using System.Security.Policy;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace VisionCore.Models
{
    public class FileItem
    {
        public string Name { get; set; }
        public string Date { get; set; }
        public string Size { get; set; }
        public string FullPath { get; set; }
        public bool IsSensorFile { get; set; }

    }

    public class FileManagerModel
    {

        public class SaveInfo
        {
            public string ImgUri { get; set; }
            public string Path { get; set; }
            public string FileName { get; set; }
        }

        // queue 생성
        private ConcurrentQueue<SaveInfo> _saveQueue = new ConcurrentQueue<SaveInfo>();

        private static FileManagerModel _instance;
        public static FileManagerModel Instance => _instance ?? (_instance = new FileManagerModel());
        public CameraControlModel controlModel => CameraControlModel.Instance;
        public ConfigModel configModel => ConfigModel.Instance;

        private static readonly HttpClient _httpClient = new HttpClient();
        private FileManagerModel()
        {
            Thread thread = new Thread(new ThreadStart(SaveCurrentSensorImage));
            thread.IsBackground = true;
            thread.Start();

        }

        #region 파일 List 불러오기 창
        public List<FileItem> GetPcFileList(string folderPath)
        {
            List<FileItem> jobList = new List<FileItem>();
            try
            {
                if (Directory.Exists(folderPath))
                {
                    DirectoryInfo d = new DirectoryInfo(folderPath);
                    // .job 확장자만 골라냅니다.
                    foreach (var file in d.GetFiles("*.job"))
                    {
                        jobList.Add(new FileItem
                        {
                            Name = file.Name,
                            FullPath = file.FullName,
                            Date = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                            Size = (file.Length / 1024).ToString() + " KB",
                            IsSensorFile = false
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("PC 파일 목록 읽기 실패: " + ex.Message);
            }
            return jobList;
        }

        public async Task<List<FileItem>> GetSensorFileListAsync(CvsInSight sensor)
        {
            List<FileItem> jobList = new List<FileItem>();
            try
            {
                await Task.Delay(100);

                string jsonResponse = await sensor.GetJobName();
                if (!string.IsNullOrEmpty(jsonResponse))
                {
                    var data = JObject.Parse(jsonResponse);
                    var items = data["items"];

                    foreach (var item in items)
                    {
                        string fileName = item["name"]?.ToString();

                        if (fileName != null && fileName.EndsWith(".job"))
                        {
                            jobList.Add(new FileItem
                            {
                                Name = fileName,
                                Date = item["modified"]?.ToString() ?? "-",
                                Size = (Convert.ToInt64(item["size"]) / 1024).ToString() + " KB",
                                IsSensorFile = true,
                                FullPath = fileName
                            });
                        }
                    }


                }
            }
            catch (Exception ex)
            {
                Logger.Error("센서 파일 목록 읽기 실패: " + ex.Message);
            }
            return jobList;
        }

        public async Task<List<FileItem>> GetFileListAsync(int locationIndex, CvsInSight sensor, string startPath)
        {
            List<FileItem> jobList = new List<FileItem>();

            if (locationIndex == 0) // PC
            {

                return GetPcFileList(startPath);
            }
            else // Sensor
            {
                if (sensor.Connected)
                {
                    // 센서 파일 읽기 로직
                    return await GetSensorFileListAsync(sensor);
                }
            }
            return jobList;
        }

        #endregion

        #region 결과 이미지 저장
        public async void SaveCurrentSensorImage()
        {

            while (true)
            {
                try
                {
                    //선입선출
                    if (_saveQueue.TryDequeue(out var item))
                    {

                        string dirPath = Path.GetDirectoryName(item.Path);

                        if (!Directory.Exists(dirPath))
                        {
                            Directory.CreateDirectory(dirPath);
                        }

                        string filename = Path.Combine(dirPath, item.FileName);

                        byte[] bytes = await _httpClient.GetByteArrayAsync(item.ImgUri);

                        using (MemoryStream ms = new MemoryStream(bytes))
                        {
                            using (Bitmap bitmap = new Bitmap(ms))
                            {
                                bitmap.Save(filename, ImageFormat.Jpeg);
                            }
                        }

                        Logger.Info("Image 저장 완료");
                    }
                    else
                    {
                        //과부하 방지..
                        await Task.Delay(100);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex.Message);
                    await Task.Delay(1000);
                }
            }
        }

        #endregion


        #region Json 데이터 전처리


        public async Task GetCellValueAsync(string imageUri)
        {
            //Result값 업데이트
            await controlModel.IsInSightSensor.GetLatestResult();

            try
            {
                JToken results = controlModel.IsInSightSensor.Results;

                var cellList = results["cells"] as JArray;

                // 현재 모델
                string model = cellList.FirstOrDefault(c => c["location"]?.ToString() == configModel.CellModelName)?["data"]?.ToString() ?? "DefaultModel";

                // 현재 포지션
                string position = cellList.FirstOrDefault(c => c["location"]?.ToString() == configModel.CellPosition)?["data"]?.ToString() ?? "0";

                // 현재 판정 결과
                string result = cellList.FirstOrDefault(c => c["location"]?.ToString() == configModel.CellResult)?["data"]?.ToString() ?? "NG";

                // 실행파일 경로
                string exePath = AppDomain.CurrentDomain.BaseDirectory;

                // 년도
                string year = DateTime.Now.ToString("yyyy");

                // 월
                string month = DateTime.Now.ToString("MM");

                // 일
                string day = DateTime.Now.ToString("dd");

                // 저장 경로 조합
                string directoryPath = Path.Combine(exePath, "VisionImage", year, month, day, model, position, result);

                // 파일명
                string fileName = $"{DateTime.Now:HH-mm-ss-fff}.jpg";

                var info = new SaveInfo
                {
                    ImgUri = imageUri,
                    Path = directoryPath,
                    FileName = fileName
                };

                _saveQueue.Enqueue(info);
            }

            catch (Exception ex)
            {
                Logger.Error("Cell 데이터 변환 실패 " + ex.Message);
            }

        }
        #endregion
    }
}
