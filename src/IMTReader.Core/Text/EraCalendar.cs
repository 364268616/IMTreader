using System.Text;

namespace IMTReader.Core.Text;

public sealed record EraInfo(string Dynasty, string Name, int StartYear, int EndYear)
{
    public int Length => EndYear - StartYear + 1;

    public override string ToString() => $"{Dynasty}·{Name}（{StartYear}–{EndYear}）";
}

public sealed record EraYear(EraInfo Era, int YearInEra)
{
    public int Year => Era.StartYear + YearInEra - 1;
    public bool InRange => YearInEra >= 1 && Year <= Era.EndYear;
    public string Label => $"{Era.Dynasty}{Era.Name}{ChineseNumeral.ToChinese(YearInEra)}年";
}

/// <summary>
/// 纪年换算：年号纪年、干支纪年与公元纪年互转。内置宋、元、明、清、民国年号表；
/// 用户可在数据目录放置 eras.user.txt（每行“朝代 年号 起始年 结束年”）扩展其它朝代。
/// </summary>
public static class EraCalendar
{
    private static readonly string[] Gan = { "甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸" };
    private static readonly string[] Zhi = { "子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥" };
    private static readonly string[] Animals = { "鼠", "牛", "虎", "兔", "龙", "蛇", "马", "羊", "猴", "鸡", "狗", "猪" };

    private const string BuiltInTable = """
        北宋|建隆|960|963
        北宋|乾德|963|968
        北宋|开宝|968|976
        北宋|太平兴国|976|984
        北宋|雍熙|984|987
        北宋|端拱|988|989
        北宋|淳化|990|994
        北宋|至道|995|997
        北宋|咸平|998|1003
        北宋|景德|1004|1007
        北宋|大中祥符|1008|1016
        北宋|天禧|1017|1021
        北宋|乾兴|1022|1022
        北宋|天圣|1023|1032
        北宋|明道|1032|1033
        北宋|景祐|1034|1038
        北宋|宝元|1038|1040
        北宋|康定|1040|1041
        北宋|庆历|1041|1048
        北宋|皇祐|1049|1054
        北宋|至和|1054|1056
        北宋|嘉祐|1056|1063
        北宋|治平|1064|1067
        北宋|熙宁|1068|1077
        北宋|元丰|1078|1085
        北宋|元祐|1086|1094
        北宋|绍圣|1094|1098
        北宋|元符|1098|1100
        北宋|建中靖国|1101|1101
        北宋|崇宁|1102|1106
        北宋|大观|1107|1110
        北宋|政和|1111|1118
        北宋|重和|1118|1119
        北宋|宣和|1119|1125
        北宋|靖康|1126|1127
        南宋|建炎|1127|1130
        南宋|绍兴|1131|1162
        南宋|隆兴|1163|1164
        南宋|乾道|1165|1173
        南宋|淳熙|1174|1189
        南宋|绍熙|1190|1194
        南宋|庆元|1195|1200
        南宋|嘉泰|1201|1204
        南宋|开禧|1205|1207
        南宋|嘉定|1208|1224
        南宋|宝庆|1225|1227
        南宋|绍定|1228|1233
        南宋|端平|1234|1236
        南宋|嘉熙|1237|1240
        南宋|淳祐|1241|1252
        南宋|宝祐|1253|1258
        南宋|开庆|1259|1259
        南宋|景定|1260|1264
        南宋|咸淳|1265|1274
        南宋|德祐|1275|1276
        南宋|景炎|1276|1278
        南宋|祥兴|1278|1279
        元|中统|1260|1264
        元|至元|1264|1294
        元|元贞|1295|1297
        元|大德|1297|1307
        元|至大|1308|1311
        元|皇庆|1312|1313
        元|延祐|1314|1320
        元|至治|1321|1323
        元|泰定|1324|1328
        元|天历|1328|1330
        元|至顺|1330|1333
        元|元统|1333|1335
        元|至元|1335|1340
        元|至正|1341|1368
        明|洪武|1368|1398
        明|建文|1399|1402
        明|永乐|1403|1424
        明|洪熙|1425|1425
        明|宣德|1426|1435
        明|正统|1436|1449
        明|景泰|1450|1457
        明|天顺|1457|1464
        明|成化|1465|1487
        明|弘治|1488|1505
        明|正德|1506|1521
        明|嘉靖|1522|1566
        明|隆庆|1567|1572
        明|万历|1573|1620
        明|泰昌|1620|1620
        明|天启|1621|1627
        明|崇祯|1628|1644
        南明|弘光|1645|1645
        南明|隆武|1645|1646
        南明|永历|1647|1661
        清|天命|1616|1626
        清|天聪|1627|1636
        清|崇德|1636|1643
        清|顺治|1644|1661
        清|康熙|1662|1722
        清|雍正|1723|1735
        清|乾隆|1736|1795
        清|嘉庆|1796|1820
        清|道光|1821|1850
        清|咸丰|1851|1861
        清|同治|1862|1874
        清|光绪|1875|1908
        清|宣统|1909|1911
        民国|民国|1912|1949
        """;

    private static readonly List<EraInfo> BuiltIn = ParseTable(BuiltInTable);
    private static readonly object UserGate = new();
    private static List<EraInfo> _user = new();

    public static IReadOnlyList<EraInfo> Eras
    {
        get
        {
            lock (UserGate) return BuiltIn.Concat(_user).ToList();
        }
    }

    private static List<EraInfo> ParseTable(string text)
    {
        var list = new List<EraInfo>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split(new[] { '|', '\t', ' ', ',', '，' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) continue;
            if (int.TryParse(parts[2], out int s) && int.TryParse(parts[3], out int e) && e >= s)
                list.Add(new EraInfo(ChineseConverter.Normalize(parts[0]), ChineseConverter.Normalize(parts[1]), s, e));
        }
        return list;
    }

    public static void LoadUserEras(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            var list = ParseTable(File.ReadAllText(path, Encoding.UTF8));
            lock (UserGate) _user = list;
        }
        catch
        {
            // 忽略格式错误
        }
    }

    // ------------------------------------------------------------------ 干支

    public static int GanZhiIndex(int year) => (((year - 4) % 60) + 60) % 60;

    public static string GanZhi(int year)
    {
        int i = GanZhiIndex(year);
        return Gan[i % 10] + Zhi[i % 12];
    }

    public static string Animal(int year) => Animals[(((year - 4) % 12) + 12) % 12];

    /// <summary>解析干支字符串为 0..59 的序号；无效时返回 null。</summary>
    public static int? ParseGanZhi(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length != 2) return null;
        int g = Array.IndexOf(Gan, s[0].ToString());
        int z = Array.IndexOf(Zhi, s[1].ToString());
        if (g < 0 || z < 0 || (g - z) % 2 != 0) return null;
        for (int i = 0; i < 60; i++)
            if (i % 10 == g && i % 12 == z) return i;
        return null;
    }

    public static IEnumerable<int> YearsForGanZhi(int index, int from, int to)
    {
        for (int y = from; y <= to; y++)
            if (GanZhiIndex(y) == index) yield return y;
    }

    // ------------------------------------------------------------------ 换算

    public static List<EraYear> FromYear(int year) =>
        Eras.Where(e => year >= e.StartYear && year <= e.EndYear)
            .Select(e => new EraYear(e, year - e.StartYear + 1))
            .ToList();

    /// <summary>解析“光绪二十年”“光緒甲午”“清乾隆六十年”等年号纪年，返回所有候选（同名年号会有多个）。</summary>
    public static List<EraYear> Parse(string input)
    {
        var results = new List<EraYear>();
        if (string.IsNullOrWhiteSpace(input)) return results;
        var s = ChineseConverter.Normalize(input.Trim()).Replace(" ", string.Empty).Replace("　", string.Empty);
        if (s.EndsWith('年')) s = s[..^1];
        if (s.Length == 0) return results;

        foreach (var era in Eras.OrderByDescending(e => e.Name.Length))
        {
            string? rest = null;
            if (s.StartsWith(era.Name, StringComparison.Ordinal)) rest = s[era.Name.Length..];
            else if (s.StartsWith(era.Dynasty + era.Name, StringComparison.Ordinal)) rest = s[(era.Dynasty.Length + era.Name.Length)..];
            if (rest == null) continue;

            if (rest.Length == 0)
            {
                results.Add(new EraYear(era, 1));
                continue;
            }
            if (ChineseNumeral.TryParse(rest, out int n))
            {
                results.Add(new EraYear(era, n));
                continue;
            }
            if (ParseGanZhi(rest) is int gz)
            {
                foreach (var y in YearsForGanZhi(gz, era.StartYear, era.EndYear))
                    results.Add(new EraYear(era, y - era.StartYear + 1));
            }
        }
        return results;
    }

    /// <summary>生成换算说明文本（供“纪年换算”工具窗口显示）。</summary>
    public static string Describe(string input)
    {
        var sb = new StringBuilder();
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var s = ChineseConverter.Normalize(input.Trim()).Replace(" ", string.Empty);
        if (s.EndsWith('年') && s.Length > 1 && s[..^1].All(char.IsDigit)) s = s[..^1];

        if (int.TryParse(s, out int year) && year is > 0 and < 3000)
        {
            sb.AppendLine($"公元 {year} 年：干支 {GanZhi(year)}（{Animal(year)}年）");
            var eras = FromYear(year);
            if (eras.Count == 0) sb.AppendLine("内置年号表未覆盖该年份（可通过 eras.user.txt 扩展）。");
            foreach (var e in eras) sb.AppendLine($"　{e.Label}（{e.Era.Dynasty}·{e.Era.Name} {e.Era.StartYear}–{e.Era.EndYear}）");
            return sb.ToString().TrimEnd();
        }

        var gzOnly = s.EndsWith('年') ? s[..^1] : s;
        if (ParseGanZhi(gzOnly) is int gi)
        {
            sb.AppendLine($"干支「{gzOnly}」对应的公元年份（960–2100）：");
            foreach (var y in YearsForGanZhi(gi, 960, 2100))
            {
                var eras = FromYear(y);
                sb.AppendLine($"　{y} 年" + (eras.Count > 0 ? "：" + string.Join("；", eras.Select(e => e.Label)) : string.Empty));
            }
            return sb.ToString().TrimEnd();
        }

        var results = Parse(s);
        if (results.Count == 0)
            return "无法识别。可输入：年号纪年（光绪二十年、光緒甲午、民国卅八年）、公元年份（1894）或干支（甲午）。";
        foreach (var r in results)
        {
            sb.Append($"{r.Label} = 公元 {r.Year} 年（{GanZhi(r.Year)}，{Animal(r.Year)}年）");
            if (!r.InRange) sb.Append($"　※ 超出年号范围（{r.Era.Name}共 {r.Era.Length} 年）");
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}
