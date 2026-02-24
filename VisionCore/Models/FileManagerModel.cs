using Cognex.InSight.Web;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VisionCore.ViewModels;
using VisionCore.Views;
using static System.Net.WebRequestMethods;

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

        private FileManagerModel()
        {

        }

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

    }
}
