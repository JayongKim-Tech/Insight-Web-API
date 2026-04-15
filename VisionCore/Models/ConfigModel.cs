using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace VisionCore.Models
{
    public class ConfigModel : INotifyPropertyChanged
    {
        private static ConfigModel _instance;
        public static ConfigModel Instance => _instance ?? (_instance = new ConfigModel());

        private string _ip = "127.0.0.1";
        private string _port = "80";
        private string _user = "admin";
        private string _password = "";

        private string _cellModelName = "A0";
        private string _cellPosition = "B0";
        private string _cellResult = "C0";

        private readonly string _iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");

        // --- Camera연결 ---
        public string IP { get => _ip; set { _ip = value; OnPropertyChanged(); } }
        public string Port { get => _port; set { _port = value; OnPropertyChanged(); } }
        public string User { get => _user; set { _user = value; OnPropertyChanged(); } }
        public string Password { get => _password; set { _password = value; OnPropertyChanged(); } }


        // --- Cell위치 ---

        public string CellModelName { get => _cellModelName; set { _cellModelName = value; OnPropertyChanged(); } }
        public string CellPosition { get => _cellPosition; set { _cellPosition = value; OnPropertyChanged(); } }
        public string CellResult { get => _cellResult; set { _cellResult = value; OnPropertyChanged(); } }


        private ConfigModel() { Load(); }

        // --- 로직 (Model의 본분) ---
        public void Load()
        {
            if (!File.Exists(_iniPath)) return;
            var ini = new ini(_iniPath);
            IP = ini.Read("Camera", "IP", "127.0.0.1");
            Port = ini.Read("Camera", "Port", "58640");
            User = ini.Read("Camera", "User", "admin");
            Password = ini.Read("Camera", "Password", "");

            CellModelName = ini.Read("Cell", "ModelName", "A0");
            CellPosition = ini.Read("Cell", "Position", "B0");
            CellResult = ini.Read("Cell", "Result", "B0");
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

                ini.Write("Cell", "ModelName", CellModelName);
                ini.Write("Cell", "Position", CellPosition);
                ini.Write("Cell", "Result", CellResult);

                Load();
                Logger.Success("설정이 config.ini에 저장되었습니다.");
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
