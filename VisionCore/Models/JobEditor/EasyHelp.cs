using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace VisionCore.Models.JobEditor
{
    /// <summary>
    /// 초보자용 쉬운 이름 + 한 줄 설명 (Resources\ToolEasy.ko.json 내장 리소스, 값 형식 "이름 | 설명").
    /// 인자: 함수별 → '*' 공통, 전체 이름(Region.X) → 이름만(X) 순서로 찾음. 없으면 null (원래 이름 표시)
    /// </summary>
    public class EasyHelp
    {
        private readonly Dictionary<string, string[]> _functions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, string[]>> _params = new Dictionary<string, Dictionary<string, string[]>>(StringComparer.OrdinalIgnoreCase);

        public static EasyHelp Default { get; } = Load();

        /// <summary>[0] = 쉬운 이름, [1] = 한 줄 설명 (없으면 null)</summary>
        public string[] Function(string functionName)
        {
            string[] v;
            return functionName != null && _functions.TryGetValue(functionName, out v) ? v : null;
        }

        public string[] Param(string functionName, string fullName, string shortName)
        {
            return Find(functionName, fullName) ?? Find("*", fullName) ?? Find(functionName, shortName) ?? Find("*", shortName);
        }

        private string[] Find(string scope, string name)
        {
            Dictionary<string, string[]> map;
            string[] v;
            return scope != null && name != null && _params.TryGetValue(scope, out map) && map.TryGetValue(name, out v) ? v : null;
        }

        private static string[] Split(string text)
        {
            int bar = text.IndexOf('|');
            return bar < 0
                ? new[] { text.Trim(), null }
                : new[] { text.Substring(0, bar).Trim(), text.Substring(bar + 1).Trim() };
        }

        private static EasyHelp Load()
        {
            var help = new EasyHelp();
            try
            {
                Assembly asm = typeof(EasyHelp).Assembly;
                string res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("ToolEasy.ko.json", StringComparison.OrdinalIgnoreCase));
                if (res == null) return help;
                string json;
                using (Stream s = asm.GetManifestResourceStream(res))
                using (var reader = new StreamReader(s))
                    json = reader.ReadToEnd();

                JObject root = JObject.Parse(json);
                if (root["functions"] is JObject funcs)
                    foreach (JProperty p in funcs.Properties())
                        help._functions[p.Name] = Split((string)p.Value);
                if (root["params"] is JObject scopes)
                    foreach (JProperty scope in scopes.Properties())
                    {
                        var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                        foreach (JProperty p in ((JObject)scope.Value).Properties())
                            map[p.Name] = Split((string)p.Value);
                        help._params[scope.Name] = map;
                    }
            }
            catch (Exception ex)
            {
                Logger.Error("ToolEasy.ko.json 읽기 실패: " + ex.Message);
            }
            return help;
        }
    }
}
