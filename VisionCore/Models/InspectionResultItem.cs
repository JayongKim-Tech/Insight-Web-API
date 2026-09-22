using System;
using System.Collections.ObjectModel;
using System.Windows.Media;

namespace VisionCore.Models
{
    /// <summary>
    /// Config에서 사용자가 자유롭게 추가한 필드 1개의 값
    /// </summary>
    public class CustomFieldValue
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    /// <summary>
    /// 검사 Point 1개의 최신 결과 (PLC가 알려준 Point 번호에 해당하는 슬롯 1개)
    /// </summary>
    public class InspectionResultItem
    {
        public DateTime Timestamp { get; set; }
        public string SensorName { get; set; }
        public string PointName { get; set; }
        public string Result { get; set; }

        // null = 아직 검사 이력 없음(대기), true = OK, false = NG
        public bool? IsOk { get; set; }

        public string ImagePath { get; set; }
        public ImageSource Thumbnail { get; set; }

        // 사용자가 Config에서 추가한 필드들의 값 (예: Model, Width 등)
        public ObservableCollection<CustomFieldValue> CustomValues { get; set; } = new ObservableCollection<CustomFieldValue>();

        public string TimestampText => Timestamp == default ? "--" : Timestamp.ToString("HH:mm:ss");
    }
}
