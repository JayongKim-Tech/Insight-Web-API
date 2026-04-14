using Cognex.InSight.Web;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public string ModelName;
        public string Position;
        public string Result;

        private static FileManagerModel _instance;
        public static FileManagerModel Instance => _instance ?? (_instance = new FileManagerModel());
        public CameraControlModel controlModel => CameraControlModel.Instance;
        public ConfigModel configModel => ConfigModel.Instance;
        private FileManagerModel()
        {

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
        public async Task SaveCurrentSensorImage(string imageUri)
        {
            try
            {
                await GetCellValueAsync();

                string exePath = AppDomain.CurrentDomain.BaseDirectory; // 실행파일 위치
                string year = DateTime.Now.ToString("yyyy");
                string monthDay = DateTime.Now.ToString("MMdd");

                // 경로 합치기: 실행파일 - VisionImage - 년도 - 월일 - Model - Position - OK/NG
                string directoryPath = Path.Combine(exePath, "VisionImage", year, monthDay, ModelName, Position, Result);

                // 폴더가 없으면 생성
                if (!Directory.Exists(directoryPath))
                    Directory.CreateDirectory(directoryPath);

                // 3. 파일명 설정 (임시 image1, 나중에 Cell 값으로 변경 가능)
                string fileName = $"{DateTime.Now.ToString("yyyy-mm-dd-ss")}";
                // string fileName = results.GetCellValue("A13")?.ToString() + ".jpg";

                string fullPath = Path.Combine(directoryPath, fileName);

                // 4. imageUri를 Bitmap으로 변환하여 저장
                //using (WebClient client = new WebClient())
                //{
                //    byte[] imageData = await client.DownloadDataTaskAsync(imageUri);
                //    using (MemoryStream ms = new MemoryStream(imageData))
                //    {
                //        using (Bitmap bitmap = new Bitmap(ms))
                //        {
                //             5. 이미지 저장 (품질 설정을 더할 수도 있음)
                //            bitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                //        }
                //    }
                //}
            }
            catch (Exception ex)
            {
                Logger.Error($"이미지 저장 중 오류 발생: {ex.Message}");
            }
        }

        #endregion


        #region Json 데이터 전처리


        public async Task GetCellValueAsync()
        {

            //Result값 업데이트
            await controlModel.IsInSightSensor.GetLatestResult();

            JToken results = controlModel.IsInSightSensor.Results;

            //Cell값만 가져오기
            var cellList = results["cells"] as JArray;

            try
            {
                ModelName = cellList.FirstOrDefault(c => c["location"]?.ToString() == configModel.CellModelName)?["data"]?.ToString() ?? "DefaultModel";

                // 현재 포지션 (E9)
                Position = cellList.FirstOrDefault(c => c["location"]?.ToString() == configModel.CellPosition)?["data"]?.ToString() ?? "0";

                // 현재 판정 결과 (I11)
                Result = cellList.FirstOrDefault(c => c["location"]?.ToString() == configModel.CellResult)?["data"]?.ToString() ?? "NG";
            }
            catch (Exception ex)
            {
                Logger.Error("Cell 데이터 변환 실패 " + ex.Message);
            }



        }
        #endregion
    }
}
