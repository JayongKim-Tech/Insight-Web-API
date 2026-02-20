using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace VisionCore.Models
{

    public enum LogLevel { Info, Success, Warning, Error }

    public static class Logger
    {
        public static void Info(string msg) => LoggerService.Instance.Info(msg);
        public static void Success(string msg) => LoggerService.Instance.Success(msg);
        public static void Warning(string msg) => LoggerService.Instance.Warning(msg);
        public static void Error(string msg) => LoggerService.Instance.Error(msg);
    }

    public class LogEntry
    {
        public string Timestamp { get; set; }
        public string Message { get; set; }
        public SolidColorBrush LogColor { get; set; }
    }

    public class LoggerService
    {
        private static LoggerService _instance;
        public static LoggerService Instance => _instance ?? (_instance = new LoggerService());

        public ObservableCollection<LogEntry> LogMessages { get; } = new ObservableCollection<LogEntry>();

        private LoggerService() { }

        public void Info(string msg) => Log(msg, LogLevel.Info);
        public void Error(string msg) => Log(msg, LogLevel.Error);
        public void Success(string msg) => Log(msg, LogLevel.Success);
        public void Warning(string msg) => Log(msg, LogLevel.Warning);

        private void Log(string message, LogLevel level)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var newEntry = new LogEntry
                {
                    Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                    Message = message,
                    LogColor = GetColor(level)
                };

                LogMessages.Add(newEntry);

                if (LogMessages.Count > 1000) LogMessages.RemoveAt(0);
            });
        }

        private SolidColorBrush GetColor(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Error: return Brushes.IndianRed;
                case LogLevel.Warning: return Brushes.Orange;
                case LogLevel.Success: return Brushes.LimeGreen;
                default: return Brushes.LightGray;
            }
        }
    }
    }
