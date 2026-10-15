using System.Collections.Generic;
using System.Linq;

namespace VisionCore.Models.JobEditor
{
    /// <summary>
    /// 툴 셀의 파라미터 1개 = 식의 인자 + Catalog 정의
    /// </summary>
    public class ToolParam
    {
        public CellArg Arg { get; set; }
        public ToolArgDef Def { get; set; }     // Catalog에 없는 인자면 null

        public int Index
        {
            get { return Arg.Index; }
        }

        public string Name
        {
            get { return Def != null ? Def.DisplayName : "인자 " + Arg.Index; }
        }

        public string Group
        {
            get { return Def != null ? Def.Group : null; }
        }

        /// <summary>
        /// 숫자/문자 리터럴만 편집 가능. 참조($C$49)나 수식은 연결된 값이라 잠금.
        /// </summary>
        public bool IsEditable
        {
            get { return Arg.IsLiteral && (Def == null || !Def.IsReference); }
        }

        public string OptionLabel
        {
            get
            {
                if (Def == null || Def.Options == null || Arg.Kind != CellArgKind.Number) return null;
                string key = CellExpression.FormatNumber(Arg.Number);
                string[] o = Def.Options.FirstOrDefault(x => x[0] == key);
                return o != null ? o[1] : null;
            }
        }
    }

    /// <summary>
    /// 스프레드시트의 툴 셀 1개 (예: A49 FindLine). 파라미터 수정 시 새 식을 만들어 줌.
    /// </summary>
    public class ToolCell
    {
        public string Location { get; private set; }
        public CellExpression Expression { get; private set; }
        public List<ToolParam> Params { get; private set; }

        /// <summary>
        /// 식의 인자 개수 == Catalog 인자 개수. 다르면 이름 매칭이 틀릴 수 있음 → UI에서 경고 표시.
        /// </summary>
        public bool ArgCountMatches { get; private set; }
        public int CatalogArgCount { get; private set; }

        public string FunctionName
        {
            get { return Expression.FunctionName; }
        }

        /// <summary>
        /// Catalog에 있는 함수 호출 셀이면 ToolCell, 아니면 null.
        /// allowUnknown: Catalog에 없어도 "첫 인자가 셀 참조 + 인자 5개 이상"이면 이미지 툴로 보고 인자 번호로 표시
        /// </summary>
        public static ToolCell TryCreate(string location, string expression, ToolCatalog catalog, bool allowUnknown = false)
        {
            CellExpression expr = CellExpression.Parse(expression);
            if (expr == null) return null;
            List<ToolArgDef> defs;
            if (!catalog.TryGet(expr.FunctionName, out defs))
            {
                if (!allowUnknown || expr.Args.Count < 5 || expr.Args[0].Kind != CellArgKind.Reference) return null;
                defs = new List<ToolArgDef>();
            }

            var cell = new ToolCell { Location = location };
            cell.Load(expr, defs);
            return cell;
        }

        /// <summary>포인트 판정/결과 셀에서 참조를 따라가 찾은 툴 (= 검사 결과에 영향)</summary>
        public bool AffectsResult { get; set; }

        public bool IsUnknownFunction
        {
            get { return CatalogArgCount == 0; }
        }

        private static readonly string[] _helperFunctions = { "Status", "Button", "CheckBox", "ColorLabel", "ListBox", "MultiStatus", "StatusLight", "Count" };

        /// <summary>
        /// 보조 툴: 그래픽/컨트롤 함수, 또는 원본 이미지(A0)가 아닌 다른 툴의 출력(마스크 등)을 입력으로 받는 후처리 툴
        /// (예: ViDiELSegment 마스크 → DetectBlobs)
        /// </summary>
        public bool IsHelper
        {
            get
            {
                string f = FunctionName;
                if (f.StartsWith("Plot", System.StringComparison.OrdinalIgnoreCase) || f.StartsWith("Edit", System.StringComparison.OrdinalIgnoreCase)) return true;
                if (_helperFunctions.Any(h => string.Equals(h, f, System.StringComparison.OrdinalIgnoreCase))) return true;
                if (Params.Count == 0) return true;
                // 판정에 영향을 주는 툴은 다른 툴 출력(마스크 등)을 입력으로 받아도 검사 툴로 표시
                if (AffectsResult) return false;

                ToolParam first = Params[0];
                bool isImageArg = first.Def == null || first.Def.Name == "Image";
                if (!isImageArg) return true;
                string img = first.Arg.Text.Replace("$", "");
                return !string.Equals(img, "A0", System.StringComparison.OrdinalIgnoreCase);
            }
        }

        private List<ToolArgDef> _defs;

        private void Load(CellExpression expr, List<ToolArgDef> defs)
        {
            _defs = defs;
            Expression = expr;
            CatalogArgCount = defs.Count;
            // 뒤쪽 인자는 생략 가능 (In-Sight가 기본값 사용) → 식 인자가 도움말보다 적은 건 정상.
            // 많으면 펌웨어에 새 인자가 추가됐거나 카탈로그가 틀린 것 → 경고
            ArgCountMatches = expr.Args.Count <= defs.Count;
            Params = expr.Args.Select(a => new ToolParam { Arg = a, Def = a.Index < defs.Count ? defs[a.Index] : null }).ToList();
        }

        public IEnumerable<ToolParam> GetGroup(string group)
        {
            return Params.Where(p => p.Group == group);
        }

        /// <summary>
        /// 파라미터 값 변경 → 새 식 반환 (SetCellExpression 으로 보낼 문자열). 내부 상태도 새 식으로 갱신.
        /// </summary>
        public string SetNumber(int index, double value)
        {
            return SetText(index, CellExpression.FormatNumber(value));
        }

        /// <summary>
        /// 여러 인자를 한 번에 변경 (ROI 드래그: X/Y/Width/Height/Angle) → 새 식 반환
        /// </summary>
        public string SetTexts(IDictionary<int, string> changes)
        {
            string expr = Expression.Source;
            foreach (KeyValuePair<int, string> c in changes)
                expr = SetText(c.Key, c.Value);
            return expr;
        }

        /// <summary>
        /// 센서 적용 실패 시 이전 식으로 되돌림
        /// </summary>
        public void Restore(string expression)
        {
            CellExpression expr = CellExpression.Parse(expression);
            if (expr != null) Load(expr, _defs);
        }

        public string SetText(int index, string text)
        {
            string newExpr = Expression.WithArg(index, text);
            Load(CellExpression.Parse(newExpr), _defs);
            return newExpr;
        }
    }
}
