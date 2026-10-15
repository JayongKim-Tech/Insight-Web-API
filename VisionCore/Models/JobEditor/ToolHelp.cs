using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace VisionCore.Models.JobEditor
{
    /// <summary>
    /// 파라미터 한글 설명 (Resources\ToolHelp.ko.json 내장 리소스).
    /// 함수별 → 공통('*') 순서로 찾고, 없으면 null (화면에서는 도움말 영어 원문 표시)
    /// </summary>
    public class ToolHelp
    {
        private readonly Dictionary<string, Dictionary<string, string>> _functions =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private static ToolHelp _default;

        public static ToolHelp Default
        {
            get { return _default ?? (_default = LoadEmbedded()); }
        }

        public static ToolHelp FromJson(string json)
        {
            var help = new ToolHelp();
            JObject root = JObject.Parse(json);
            var aliases = new Dictionary<string, string>();

            foreach (JProperty f in root.Properties())
            {
                if (f.Name.StartsWith("_")) continue;
                if (f.Value.Type == JTokenType.String)
                {
                    // "ViDiELSegment": "@EdgeAISegment" - 이름만 바뀐 함수
                    string target = ((string)f.Value).TrimStart('@');
                    aliases[f.Name] = target;
                    continue;
                }
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (JProperty p in ((JObject)f.Value).Properties())
                    map[p.Name] = (string)p.Value;
                help._functions[f.Name] = map;
            }

            foreach (KeyValuePair<string, string> a in aliases)
            {
                Dictionary<string, string> map;
                if (help._functions.TryGetValue(a.Value, out map)) help._functions[a.Key] = map;
            }
            return help;
        }

        // 도움말 영어 원문 → 한국어 번역 (Resources\ToolDesc.ko.json, Tools\ToolCatalogGen\BuildToolDesc.ps1 로 생성)
        private Dictionary<string, string> _byEnglish = new Dictionary<string, string>();

        /// <param name="functionName">FindLine</param>
        /// <param name="paramName">Accept Thresh, Region.X ...</param>
        /// <param name="englishDescription">ToolCatalog 의 영어 설명 (직접 쓴 한글 설명이 없을 때 번역본을 찾는 키)</param>
        public string Find(string functionName, string paramName, string englishDescription = null)
        {
            Dictionary<string, string> map;
            string text;
            if (_functions.TryGetValue(functionName, out map) && map.TryGetValue(paramName, out text)) return text;
            if (_functions.TryGetValue("*", out map) && map.TryGetValue(paramName, out text)) return text;
            if (englishDescription != null && _byEnglish.TryGetValue(englishDescription, out text)) return text;
            return null;
        }

        private static string ReadResource(string fileName)
        {
            Assembly asm = typeof(ToolHelp).Assembly;
            string name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
            if (name == null) return null;
            using (Stream s = asm.GetManifestResourceStream(name))
            using (var reader = new StreamReader(s))
                return reader.ReadToEnd();
        }

        private static ToolHelp LoadEmbedded()
        {
            try
            {
                string json = ReadResource("ToolHelp.ko.json");
                ToolHelp help = json != null ? FromJson(json) : new ToolHelp();

                string desc = ReadResource("ToolDesc.ko.json");
                if (desc != null)
                    help._byEnglish = JObject.Parse(desc).Properties().ToDictionary(p => p.Name, p => (string)p.Value);
                return help;
            }
            catch (Exception ex)
            {
                Logger.Error("ToolHelp.ko.json 읽기 실패: " + ex.Message);
                return new ToolHelp();
            }
        }
    }
}
