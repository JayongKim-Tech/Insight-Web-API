using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace VisionCore.Models.JobEditor
{
    /// <summary>
    /// 사용자 모드에서 보여 줄 "주요 파라미터" (함수 이름 → 인자 이름 목록).
    /// 개발자 모드에서 ★ 로 지정, 실행 폴더의 keyparams.json 에 저장 (센서/잡과 무관하게 함수 단위로 적용).
    /// 함수에 ★ 가 하나도 없으면 사용자 모드에서도 전부 표시.
    /// </summary>
    public class KeyParamStore
    {
        private static readonly string _path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "keyparams.json");
        private readonly Dictionary<string, HashSet<string>> _map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        public static KeyParamStore Default { get; } = Load();

        public bool HasAny(string function)
        {
            HashSet<string> set;
            return function != null && _map.TryGetValue(function, out set) && set.Count > 0;
        }

        public bool IsKey(string function, string param)
        {
            HashSet<string> set;
            return function != null && _map.TryGetValue(function, out set) && set.Contains(param);
        }

        public void Set(string function, string param, bool isKey)
        {
            HashSet<string> set;
            if (!_map.TryGetValue(function, out set)) _map[function] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (isKey) set.Add(param); else set.Remove(param);
            Save();
        }

        private static KeyParamStore Load()
        {
            var store = new KeyParamStore();
            try
            {
                if (!File.Exists(_path)) return store;
                var data = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(File.ReadAllText(_path));
                if (data != null)
                    foreach (KeyValuePair<string, List<string>> kv in data)
                        store._map[kv.Key] = new HashSet<string>(kv.Value ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Logger.Error("[KeyParams] 읽기 실패: " + ex.Message);
            }
            return store;
        }

        private void Save()
        {
            try
            {
                var data = _map.Where(kv => kv.Value.Count > 0).OrderBy(kv => kv.Key)
                    .ToDictionary(kv => kv.Key, kv => kv.Value.OrderBy(v => v).ToList());
                File.WriteAllText(_path, JsonConvert.SerializeObject(data, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Logger.Error("[KeyParams] 저장 실패: " + ex.Message);
            }
        }
    }
}
