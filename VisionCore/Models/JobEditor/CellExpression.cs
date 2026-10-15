using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace VisionCore.Models.JobEditor
{
    public enum CellArgKind
    {
        Empty,
        Number,
        String,
        Reference,
        Formula
    }

    /// <summary>
    /// 함수 식 안의 인자 1개 (원본 식에서의 위치 포함 - 이 위치만 바꿔서 식을 다시 씀)
    /// </summary>
    public class CellArg
    {
        public int Index { get; set; }
        public string Text { get; set; }
        public int Start { get; set; }
        public int Length { get; set; }
        public CellArgKind Kind { get; set; }
        public double Number { get; set; }

        public bool IsLiteral
        {
            get { return Kind == CellArgKind.Number || Kind == CellArgKind.String; }
        }
    }

    /// <summary>
    /// "FindLine($A$0,0,0,0,3112.9,...)" 형태의 셀 식 파서.
    /// 최상위 함수 1개만 다룸. 인자 안의 중첩 함수/참조는 Formula/Reference로 그대로 보존.
    /// </summary>
    public class CellExpression
    {
        private static readonly Regex _head = new Regex(@"^\s*=?\s*([A-Za-z_][A-Za-z0-9_]*)\s*\(");
        private static readonly Regex _number = new Regex(@"^[-+]?(\d+\.?\d*|\.\d+)([eE][-+]?\d+)?$");
        private static readonly Regex _hex = new Regex(@"^0[xX][0-9A-Fa-f]+$");
        private static readonly Regex _reference = new Regex(@"^\$?[A-Za-z]{1,2}\$?\d+(:\$?[A-Za-z]{1,2}\$?\d+)?$");

        public string Source { get; private set; }
        public string FunctionName { get; private set; }
        public List<CellArg> Args { get; private set; }

        private CellExpression() { }

        /// <summary>
        /// 함수 호출 식이 아니면 null (예: 'Label, 0, If(...)+1)
        /// </summary>
        public static CellExpression Parse(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression)) return null;
            Match m = _head.Match(expression);
            if (!m.Success) return null;

            var args = new List<CellArg>();
            int depth = 0;
            bool inString = false;
            int argStart = m.Index + m.Length;
            int close = -1;

            for (int i = argStart; i < expression.Length; i++)
            {
                char c = expression[i];
                if (inString)
                {
                    if (c == '\\' && i + 1 < expression.Length) { i++; continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; continue; }
                if (c == '(' || c == '{' || c == '[') { depth++; continue; }
                if ((c == ')' || c == '}' || c == ']') && depth > 0) { depth--; continue; }
                if (c == ',' && depth == 0)
                {
                    args.Add(MakeArg(expression, args.Count, argStart, i));
                    argStart = i + 1;
                    continue;
                }
                if (c == ')' && depth == 0)
                {
                    close = i;
                    break;
                }
            }
            if (close < 0) return null;
            // 닫는 괄호 뒤에 다른 연산이 붙어 있으면 툴 셀이 아님
            if (expression.Substring(close + 1).Trim().Length > 0) return null;

            CellArg last = MakeArg(expression, args.Count, argStart, close);
            if (args.Count > 0 || last.Kind != CellArgKind.Empty) args.Add(last);

            return new CellExpression { Source = expression, FunctionName = m.Groups[1].Value, Args = args };
        }

        private static CellArg MakeArg(string expression, int index, int from, int to)
        {
            int s = from, e = to;
            while (s < e && char.IsWhiteSpace(expression[s])) s++;
            while (e > s && char.IsWhiteSpace(expression[e - 1])) e--;
            string text = expression.Substring(s, e - s);

            var arg = new CellArg { Index = index, Text = text, Start = s, Length = e - s };
            double v;
            if (text.Length == 0)
                arg.Kind = CellArgKind.Empty;
            else if (_number.IsMatch(text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            {
                arg.Kind = CellArgKind.Number;
                arg.Number = v;
            }
            else if (_hex.IsMatch(text))
            {
                arg.Kind = CellArgKind.Number;
                arg.Number = Convert.ToInt64(text.Substring(2), 16);
            }
            else if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
                arg.Kind = CellArgKind.String;
            else if (_reference.IsMatch(text))
                arg.Kind = CellArgKind.Reference;
            else
                arg.Kind = CellArgKind.Formula;
            return arg;
        }

        /// <summary>
        /// index 번째 인자만 newText로 바꾼 새 식 문자열 (나머지 글자는 원본 그대로)
        /// </summary>
        public string WithArg(int index, string newText)
        {
            CellArg a = Args[index];
            return Source.Substring(0, a.Start) + newText + Source.Substring(a.Start + a.Length);
        }

        public static string FormatNumber(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
