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
        private static FileManagerModel _instance;
        public static FileManagerModel Instance => _instance ?? (_instance = new FileManagerModel());
        public ConfigModel configModel => ConfigModel.Instance;

        private FileManagerModel() { }

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

        #region Json 데이터 전처리

        /// <summary>
        /// Result/PointNumber 셀과 사용자 정의 필드를 읽어 현재 트리거가 어느 Point의 결과인지 판단합니다.
        /// (셀 위치는 항상 동일하며, PLC가 CellPointNumber 셀에 현재 Point 번호를 씁니다.)
        /// </summary>
        public InspectionResultItem BuildPointResult(CvsInSight sensor, string sensorName)
        {
            try
            {
                JToken results = sensor.Results;
                var cellList = results["cells"] as JArray;

                // location -> data 한 번에 인덱싱 (셀마다 개별 FirstOrDefault로 전체 배열을 훑지 않도록)
                var cellMap = cellList
                    .Where(c => c["location"] != null)
                    .GroupBy(c => c["location"].ToString())
                    .ToDictionary(g => g.Key, g => g.First()["data"]?.ToString());

                string result = cellMap.TryGetValue(configModel.CellResult, out var r) ? r : "NG";
                string pointNumber = cellMap.TryGetValue(configModel.CellPointNumber, out var pn) ? pn : "1";

                var item = new InspectionResultItem
                {
                    Timestamp = DateTime.Now,
                    SensorName = sensorName,
                    PointName = $"Point{pointNumber}",
                    Result = result,
                    IsOk = string.Equals(result, "OK", StringComparison.OrdinalIgnoreCase)
                };

                foreach (var field in configModel.CustomFields)
                {
                    item.CustomValues.Add(new CustomFieldValue
                    {
                        Name = field.Name,
                        Value = cellMap.TryGetValue(field.CellLocation, out var v) ? v : ""
                    });
                }

                return item;
            }
            catch (Exception ex)
            {
                Logger.Error("Cell 데이터 변환 실패 " + ex.Message);
                return null;
            }
        }
        #endregion
    }
}
