using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using VisionCore.Models;
using VisionCore.ViewModels;

namespace VisionCore.Views
{
    /// <summary>
    /// CSV 헤더(ResultColumn[])를 받아 DataGrid 컬럼을 동적으로 생성하는 첨부 속성
    /// (검사 공정마다 결과 컬럼이 달라도 XAML 수정 없이 그대로 표시)
    /// 헤더 모양은 DataGrid 리소스의 "ResultHeaderTemplate" (필터 ▼ 버튼 포함)
    /// </summary>
    public static class ResultGridColumns
    {
        public static readonly DependencyProperty HeadersProperty =
            DependencyProperty.RegisterAttached("Headers", typeof(ResultColumn[]), typeof(ResultGridColumns),
                new PropertyMetadata(null, OnHeadersChanged));

        public static ResultColumn[] GetHeaders(DependencyObject d) => (ResultColumn[])d.GetValue(HeadersProperty);
        public static void SetHeaders(DependencyObject d, ResultColumn[] value) => d.SetValue(HeadersProperty, value);

        // 스타일은 한 번만 만들어 공유 (컬럼 재생성마다 새로 만들지 않음)
        private static readonly Style ResultCellStyle = CreateResultStyle();
        private static readonly Style DimCellStyle = CreateDimStyle();

        private static void OnHeadersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is DataGrid grid)) return;

            var headers = e.NewValue as ResultColumn[];
            grid.Columns.Clear();
            if (headers == null) return;

            var headerTemplate = grid.TryFindResource("ResultHeaderTemplate") as DataTemplate;

            foreach (var header in headers)
            {
                string name = header.Name;
                int i = header.Index;

                // 이미지 경로는 화면에 표시하지 않음 (우클릭 폴더 열기/더블클릭 이미지 열기에서만 사용)
                if (string.Equals(name, ResultCsvService.ImagePathHeader, StringComparison.OrdinalIgnoreCase)) continue;

                var column = new DataGridTextColumn
                {
                    Header = header,
                    HeaderTemplate = headerTemplate,
                    Binding = new Binding($"Values[{i}]") { Mode = BindingMode.OneTime },
                    IsReadOnly = true
                };

                if (string.Equals(name, ResultCsvService.ResultHeader, StringComparison.OrdinalIgnoreCase))
                {
                    column.ElementStyle = ResultCellStyle;
                    column.Width = 70;
                }
                else if (string.Equals(name, ResultCsvService.TimeHeader, StringComparison.OrdinalIgnoreCase))
                {
                    column.ElementStyle = DimCellStyle;
                }

                grid.Columns.Add(column);
            }
        }

        private static Style CreateResultStyle()
        {
            var okBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0x5A));
            var ngBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x5A));
            okBrush.Freeze();
            ngBrush.Freeze();

            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Bold));
            style.Setters.Add(new Setter(TextBlock.ForegroundProperty, okBrush));

            var ng = new DataTrigger { Binding = new Binding(nameof(ResultRow.IsNg)), Value = true };
            ng.Setters.Add(new Setter(TextBlock.ForegroundProperty, ngBrush));
            style.Triggers.Add(ng);

            style.Seal();
            return style;
        }

        private static Style CreateDimStyle()
        {
            var brush = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A));
            brush.Freeze();

            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.ForegroundProperty, brush));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Seal();
            return style;
        }
    }
}
