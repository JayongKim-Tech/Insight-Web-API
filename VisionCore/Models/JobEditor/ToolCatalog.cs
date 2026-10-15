using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace VisionCore.Models.JobEditor
{
    /// <summary>
    /// 함수 인자 1개의 정의 (In-Sight 도움말 Inputs 표에서 자동 생성)
    /// </summary>
    public class ToolArgDef
    {
        [JsonProperty("n")] public string Name { get; set; }
        [JsonProperty("k")] public string Kind { get; set; }      // ref | number | enum
        [JsonProperty("g")] public string Group { get; set; }     // Fixture, Region, ... (복합 인자일 때만)
        [JsonProperty("min")] public double? Min { get; set; }
        [JsonProperty("max")] public double? Max { get; set; }
        [JsonProperty("def")] public string Default { get; set; }
        [JsonProperty("opt")] public List<string[]> Options { get; set; }   // [값, 이름]
        [JsonProperty("d")] public string Description { get; set; }

        public bool IsReference
        {
            get { return Kind == "ref"; }
        }

        public string DisplayName
        {
            get { return string.IsNullOrEmpty(Group) ? Name : Group + "." + Name; }
        }
    }

    /// <summary>
    /// 함수명 → 인자 순서 정의. Resources\ToolCatalog.json (Tools\ToolCatalogGen 으로 생성) 내장 리소스를 읽음.
    /// </summary>
    public class ToolCatalog
    {
        private readonly Dictionary<string, List<ToolArgDef>> _functions;

        private static ToolCatalog _default;

        public static ToolCatalog Default
        {
            get { return _default ?? (_default = LoadEmbedded()); }
        }

        public ToolCatalog(Dictionary<string, List<ToolArgDef>> functions)
        {
            // In-Sight 함수명은 대소문자 구분 없음 (If / if)
            _functions = new Dictionary<string, List<ToolArgDef>>(functions, StringComparer.OrdinalIgnoreCase);
            ApplyMissingOptions();
        }

        // 도움말 표에 선택 항목이 안 적혀 있어서 자동 생성 시 빠진 인자들 (예: ViDiELSegment/Classify 의 Show).
        // 코드에서 보정하므로 ToolCatalog.json을 다시 생성해도 유지됨. 이미 Options가 있으면 건드리지 않음.
        private static readonly string[][] ShowOptions =
        {
            new[] { "0", "hide all" }, new[] { "1", "result graphics only" }, new[] { "2", "input and result graphics" }
        };
        private static readonly string[][] OnOffOptions = { new[] { "0", "off" }, new[] { "1", "on" } };
        private static readonly string[][] FeatureSizeOptions =
        {
            new[] { "16", "Very Small" }, new[] { "32", "Small" }, new[] { "64", "Medium" }, new[] { "128", "Large" }
        };

        private void ApplyMissingOptions()
        {
            foreach (KeyValuePair<string, List<ToolArgDef>> f in _functions)
            {
                if (f.Value == null) continue;
                bool edgeAi = f.Key.StartsWith("ViDiEL", StringComparison.OrdinalIgnoreCase) || f.Key.StartsWith("EdgeAI", StringComparison.OrdinalIgnoreCase);
                foreach (ToolArgDef a in f.Value)
                {
                    if (a == null || a.Options != null || a.Name == null) continue;
                    string[][] opt = null;
                    string def = null;
                    if (a.Name == "Show") { opt = ShowOptions; def = "0"; }
                    else if (edgeAi && (a.Name == "Collect Samples" || a.Name == "Unclassified Detection")) { opt = OnOffOptions; def = "0"; }
                    else if (edgeAi && a.Name == "Feature Size") { opt = FeatureSizeOptions; def = "64"; }
                    if (opt == null) continue;

                    a.Options = opt.Select(o => new[] { o[0], o[1] }).ToList();
                    a.Kind = "enum";
                    if (string.IsNullOrEmpty(a.Default)) a.Default = def;
                }
            }
        }

        public int Count
        {
            get { return _functions.Count; }
        }

        /// <summary>함수명 → 인자 표시 이름 (스프레드시트 자동완성/힌트용)</summary>
        public Dictionary<string, string[]> GetFunctionArgs()
        {
            return _functions.ToDictionary(f => f.Key, f => f.Value.Select(a => a.DisplayName).ToArray(), StringComparer.OrdinalIgnoreCase);
        }

        public bool TryGet(string functionName, out List<ToolArgDef> args)
        {
            return _functions.TryGetValue(functionName, out args);
        }

        public static ToolCatalog FromJson(string json)
        {
            return new ToolCatalog(JsonConvert.DeserializeObject<Dictionary<string, List<ToolArgDef>>>(json));
        }

        private static ToolCatalog LoadEmbedded()
        {
            Assembly asm = typeof(ToolCatalog).Assembly;
            string name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("ToolCatalog.json", StringComparison.OrdinalIgnoreCase));
            if (name == null) return new ToolCatalog(new Dictionary<string, List<ToolArgDef>>());
            using (Stream s = asm.GetManifestResourceStream(name))
            using (var reader = new StreamReader(s))
            {
                return FromJson(reader.ReadToEnd());
            }
        }
    }
}
