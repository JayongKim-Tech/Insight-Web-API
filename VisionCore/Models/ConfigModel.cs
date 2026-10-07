using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace VisionCore.Models
{
    /// <summary>
    /// 사용자가 자유롭게 추가/삭제하는 셀 매핑 (이름도 자유롭게 지정)
    /// </summary>
    public class CustomFieldDefinition : INotifyPropertyChanged
    {
        private string _name;
        private string _cellLocation;

        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
        public string CellLocation { get => _cellLocation; set { _cellLocation = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class ConfigModel : INotifyPropertyChanged
    {
        private static ConfigModel _instance;
        public static ConfigModel Instance => _instance ?? (_instance = new ConfigModel());

        private string _ip = "127.0.0.1";
        private string _port = "80";
        private string _user = "admin";
        private string _password = "";

        // 필수 3개: Point 개수 / 판정 결과 셀 / 현재 Point 번호 셀 (PLC가 씀). 나머지는 CustomFields로 자유 추가.
        private int _pointCount = 1;
        private string _cellResult = "C0";
        private string _cellPointNumber = "D0";

        private string _saveRootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VisionImage");

        private readonly string _iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
        private readonly string _fieldsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "customfields.json");

        // --- Camera연결 ---
        public string IP { get => _ip; set { _ip = value; OnPropertyChanged(); } }
        public string Port { get => _port; set { _port = value; OnPropertyChanged(); } }
        public string User { get => _user; set { _user = value; OnPropertyChanged(); } }
        public string Password { get => _password; set { _password = value; OnPropertyChanged(); } }

        // --- 검사 Point (모든 센서 공통 적용) ---
        public int PointCount { get => _pointCount; set { _pointCount = Math.Max(1, value); OnPropertyChanged(); } }
        public string CellResult { get => _cellResult; set { _cellResult = value; OnPropertyChanged(); } }
        public string CellPointNumber { get => _cellPointNumber; set { _cellPointNumber = value; OnPropertyChanged(); } }

        // 사용자가 이름/셀 위치를 자유롭게 추가·삭제하는 필드 목록 (예: Model, Width 등)
        public ObservableCollection<CustomFieldDefinition> CustomFields { get; } = new ObservableCollection<CustomFieldDefinition>();

        // --- 결과 이미지 저장 경로 ---
        public string SaveRootPath { get => _saveRootPath; set { _saveRootPath = value; OnPropertyChanged(); } }

        // 디스크 사용률이 이 % 이상이면 오래된 날짜 이미지부터 자동 삭제 (0 = 사용 안 함, 50~95)
        private int _diskLimitPercent = 85;
        public int DiskLimitPercent
        {
            get => _diskLimitPercent;
            set { _diskLimitPercent = value <= 0 ? 0 : Math.Min(95, Math.Max(50, value)); OnPropertyChanged(); }
        }

        // Config 저장 시 발생 (재시작 없이 화면에 즉시 반영하기 위함)
        public event EventHandler ConfigChanged;

        private ConfigModel() { Load(); }

        public void Load()
        {
            if (File.Exists(_iniPath))
            {
                var ini = new ini(_iniPath);
                IP = ini.Read("Camera", "IP", "127.0.0.1");
                Port = ini.Read("Camera", "Port", "58640");
                User = ini.Read("Camera", "User", "admin");
                Password = ini.Read("Camera", "Password", "");

                int.TryParse(ini.Read("Point", "Count", "1"), out _pointCount);
                CellResult = ini.Read("Point", "ResultCell", "C0");
                CellPointNumber = ini.Read("Point", "CurrentPointNumberCell", "D0");

                SaveRootPath = ini.Read("Save", "RootPath", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VisionImage"));

                int limit;
                DiskLimitPercent = int.TryParse(ini.Read("Save", "DiskLimitPercent", "85"), out limit) ? limit : 85;
            }

            CustomFields.Clear();
            if (File.Exists(_fieldsPath))
            {
                try
                {
                    var list = JsonConvert.DeserializeObject<List<CustomFieldDefinition>>(File.ReadAllText(_fieldsPath));
                    if (list != null)
                    {
                        foreach (var f in list) CustomFields.Add(f);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("사용자 정의 필드 로드 실패: " + ex.Message);
                }
            }
        }

        public void Save()
        {
            try
            {
                var ini = new ini(_iniPath);
                ini.Write("Camera", "IP", IP);
                ini.Write("Camera", "Port", Port);
                ini.Write("Camera", "User", User);
                ini.Write("Camera", "Password", Password);

                ini.Write("Point", "Count", PointCount.ToString());
                ini.Write("Point", "ResultCell", CellResult);
                ini.Write("Point", "CurrentPointNumberCell", CellPointNumber);

                ini.Write("Save", "RootPath", SaveRootPath);
                ini.Write("Save", "DiskLimitPercent", DiskLimitPercent.ToString());

                File.WriteAllText(_fieldsPath, JsonConvert.SerializeObject(CustomFields, Formatting.Indented));

                Load();
                Logger.Success("설정이 저장되었습니다.");
                ConfigChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Logger.Error("저장 중 오류 발생: " + ex.Message);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
