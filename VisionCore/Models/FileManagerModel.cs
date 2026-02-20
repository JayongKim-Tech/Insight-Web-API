using Cognex.InSight.Web;
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
                // ※ 주의: 실제 Cognex SDK 버전에 따라 파일 목록을 가져오는 API가 다를 수 있습니다.
                // 보통은 FTP나 특정 Web API 경로(/system/files 등)를 쿼리해야 합니다.
                // 일단 구조를 위해 샘플 데이터를 넣거나, 센서의 GetJobName 같은 정보를 활용합니다.

                // (예시 코드 - 실제 센서 통신 로직이 들어갈 자리)
                // var files = await sensor.GetFileListAsync(); ...

                jobList.Add(new FileItem { Name = "Sensor_Job_1.job", IsSensorFile = true, Date = "2024-05-22" });
            }
            catch (Exception ex)
            {
                Logger.Error("센서 파일 목록 읽기 실패: " + ex.Message);
            }
            return jobList;
        }

        public async Task<List<FileItem>> GetFileListAsync(int locationIndex, CvsInSight sensor)
        {
            List<FileItem> jobList = new List<FileItem>();

            if (locationIndex == 0) // PC
            {
                string startPath = AppDomain.CurrentDomain.BaseDirectory;
                // PC 파일 읽기 로직 (기존 GetPcFileList 호출)
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
