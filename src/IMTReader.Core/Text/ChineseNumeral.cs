namespace IMTReader.Core.Text;

/// <summary>中文数字解析：支持 一二三…、〇零、壹贰叁…、十百千、廿卅卌、元（=1）以及阿拉伯数字。</summary>
public static class ChineseNumeral
{
    private static readonly Dictionary<char, int> Digits = new()
    {
        ['〇'] = 0, ['零'] = 0, ['０'] = 0,
        ['一'] = 1, ['壹'] = 1, ['１'] = 1,
        ['二'] = 2, ['贰'] = 2, ['貳'] = 2, ['两'] = 2, ['兩'] = 2, ['２'] = 2,
        ['三'] = 3, ['叁'] = 3, ['參'] = 3, ['参'] = 3, ['３'] = 3,
        ['四'] = 4, ['肆'] = 4, ['４'] = 4,
        ['五'] = 5, ['伍'] = 5, ['５'] = 5,
        ['六'] = 6, ['陆'] = 6, ['陸'] = 6, ['６'] = 6,
        ['七'] = 7, ['柒'] = 7, ['７'] = 7,
        ['八'] = 8, ['捌'] = 8, ['８'] = 8,
        ['九'] = 9, ['玖'] = 9, ['９'] = 9
    };

    private static readonly Dictionary<char, int> Units = new()
    {
        ['十'] = 10, ['拾'] = 10, ['百'] = 100, ['佰'] = 100, ['千'] = 1000, ['仟'] = 1000
    };

    public static bool IsNumeralChar(char c) =>
        Digits.ContainsKey(c) || Units.ContainsKey(c) || c is '廿' or '卅' or '卌' or '元' || char.IsDigit(c);

    public static bool TryParse(string? s, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        if (s == "元" || s == "首") { value = 1; return true; }
        if (s.All(char.IsDigit))
        {
            var half = new string(s.Select(c => c >= '０' && c <= '９' ? (char)(c - 0xFEE0) : c).ToArray());
            return int.TryParse(half, out value);
        }

        int total = 0, number = 0;
        bool any = false;
        foreach (char c in s)
        {
            if (c == '廿') { total += 20; number = 0; any = true; continue; }
            if (c == '卅') { total += 30; number = 0; any = true; continue; }
            if (c == '卌') { total += 40; number = 0; any = true; continue; }
            if (Digits.TryGetValue(c, out int d))
            {
                number = number * 10 + d;
                any = true;
                continue;
            }
            if (Units.TryGetValue(c, out int u))
            {
                if (number == 0) number = 1;
                total += number * u;
                number = 0;
                any = true;
                continue;
            }
            if (char.IsDigit(c))
            {
                number = number * 10 + (c - '0');
                any = true;
                continue;
            }
            return false;
        }
        value = total + number;
        return any;
    }

    /// <summary>阿拉伯数字 → 中文数字（1..9999）。</summary>
    public static string ToChinese(int n)
    {
        if (n <= 0) return n.ToString();
        if (n == 1) return "元";
        string digits = "零一二三四五六七八九";
        var sb = new System.Text.StringBuilder();
        int thousands = n / 1000, hundreds = n % 1000 / 100, tens = n % 100 / 10, ones = n % 10;
        if (thousands > 0) sb.Append(digits[thousands]).Append('千');
        if (hundreds > 0) sb.Append(digits[hundreds]).Append('百');
        else if (thousands > 0 && (tens > 0 || ones > 0)) sb.Append('零');
        if (tens > 0)
        {
            if (!(tens == 1 && sb.Length == 0)) sb.Append(digits[tens]);
            sb.Append('十');
        }
        else if (hundreds > 0 && ones > 0) sb.Append('零');
        if (ones > 0) sb.Append(digits[ones]);
        return sb.ToString();
    }
}
