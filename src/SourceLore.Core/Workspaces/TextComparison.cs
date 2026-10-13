using System.Text;

namespace SourceLore.Core.Workspaces;

public sealed record TextLine(string Content, string Ending)
{
    public string Exact => Content + Ending;
}

public sealed record TextContent(string Text, string Encoding, IReadOnlyList<TextLine> Lines)
{
    public const int MaxBytes = 8 * 1024 * 1024, MaxLines = 100_000, MaxLineLength = 100_000;
    public string EndingLabel => Lines.Count == 0 ? "空文件" :
        string.Join(" / ", Lines.Select(line => line.Ending switch { "\r\n" => "CRLF", "\n" => "LF", "\r" => "CR", _ => "无末尾换行" }).Distinct());
    public static TextContent Decode(byte[] bytes)
    {
        if (bytes.Length > MaxBytes) throw new InvalidDataException("文件超过 8 MiB 预览上限，请使用外部工具查看。");
        string text, encoding;
        try
        {
            if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe }))
            { encoding = "UTF-16 LE BOM"; text = new UnicodeEncoding(false, true, true).GetString(bytes, 2, bytes.Length - 2); }
            else if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff }))
            { encoding = "UTF-16 BE BOM"; text = new UnicodeEncoding(true, true, true).GetString(bytes, 2, bytes.Length - 2); }
            else
            {
                var bom = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf });
                encoding = bom ? "UTF-8 BOM" : "UTF-8";
                text = new UTF8Encoding(false, true).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
            }
        }
        catch (DecoderFallbackException) { throw new InvalidDataException("编码不受支持；预览支持 UTF-8 和带 BOM 的 UTF-16。"); }
        if (text.Contains('\0')) throw new InvalidDataException("二进制内容无法使用文本预览，请使用外部工具查看。");
        var lines = new List<TextLine>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n'))
            { if (i - start + 1 > MaxLineLength) throw new InvalidDataException("单行超过 100,000 字符预览上限。"); continue; }
            var end = i;
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            lines.Add(new(text[start..end], text[end..(i + 1)]));
            if (lines.Count > MaxLines) throw new InvalidDataException("文件超过 100,000 行预览上限。");
            start = i + 1;
        }
        if (start < text.Length) lines.Add(new(text[start..], ""));
        if (lines.Count > MaxLines) throw new InvalidDataException("文件超过 100,000 行预览上限。");
        return new(text, encoding, lines);
    }
}

public enum DiffKind { Context, Removed, Added }
public sealed record DiffRow(DiffKind Kind, int? LeftNumber, int? RightNumber, TextLine? Left, TextLine? Right);
public sealed record ComparisonSource(string Label, bool Exists, TextContent Content);
public sealed class TextComparison
{
    public string Path { get; }
    public ComparisonSource Left { get; }
    public ComparisonSource Right { get; }
    public IReadOnlyList<DiffRow> Rows { get; }
    public bool UsedCoarseBlocks { get; }
    public string SourceLabel => $"{Path} — {Left.Label} → {Right.Label}";
    public string FormatLabel => $"左：{Format(Left)}；右：{Format(Right)}" + (UsedCoarseBlocks ? "；大改动按完整删除/新增块显示" : "");
    private static string Format(ComparisonSource side) => side.Exists ? $"{side.Content.Encoding}，{side.Content.EndingLabel}" : "不存在（空内容）";
    public string Unified { get; }
    public string LeftView { get; }
    public string RightView { get; }
    private TextComparison(string path, ComparisonSource left, ComparisonSource right, IReadOnlyList<DiffRow> rows, bool coarse)
    {
        Path = path; Left = left; Right = right; Rows = rows; UsedCoarseBlocks = coarse;
        var unified = new StringBuilder($"--- {left.Label}\n+++ {right.Label}\n{FormatLabel}\n");
        var before = new StringBuilder(); var after = new StringBuilder();
        foreach (var row in rows)
        {
            var line = row.Left ?? row.Right!;
            var mark = row.Kind switch { DiffKind.Removed => "-", DiffKind.Added => "+", _ => " " };
            unified.Append(mark).Append(' ').Append(row.LeftNumber?.ToString() ?? "·").Append('/').Append(row.RightNumber?.ToString() ?? "·").Append(' ').Append(line.Content);
            if (row.Kind != DiffKind.Context) unified.Append(EndingTag(line));
            unified.Append('\n');
        }
        for (var index = 0; index < rows.Count;)
        {
            var row = rows[index];
            if (row.Kind == DiffKind.Context)
            {
                AppendSide(before, row.LeftNumber, row.Left, " ");
                AppendSide(after, row.RightNumber, row.Right, " "); index++; continue;
            }
            var removed = new List<DiffRow>(); var added = new List<DiffRow>();
            while (index < rows.Count && rows[index].Kind != DiffKind.Context)
            {
                var change = rows[index++];
                if (change.Left is not null) removed.Add(change);
                if (change.Right is not null) added.Add(change);
            }
            for (var i = 0; i < Math.Max(removed.Count, added.Count); i++)
            {
                var oldLine = i < removed.Count ? removed[i] : null;
                var newLine = i < added.Count ? added[i] : null;
                AppendSide(before, oldLine?.LeftNumber, oldLine?.Left, "-");
                AppendSide(after, newLine?.RightNumber, newLine?.Right, "+");
            }
        }
        Unified = unified.ToString(); LeftView = before.ToString(); RightView = after.ToString();
    }
    private static string EndingTag(TextLine line) => line.Ending switch { "\r\n" => " ⟦CRLF⟧", "\n" => " ⟦LF⟧", "\r" => " ⟦CR⟧", _ => " ⟦无末尾换行⟧" };
    private static void AppendSide(StringBuilder output, int? number, TextLine? line, string mark)
    {
        if (line is not null) output.Append(mark).Append(' ').Append(number).Append(' ').Append(line.Content).Append(mark == " " ? "" : EndingTag(line));
        output.Append('\n');
    }
    public static TextComparison Build(string path, ComparisonSource left, ComparisonSource right)
    {
        var a = left.Content.Lines; var b = right.Content.Lines;
        var rows = new List<DiffRow>(); var budget = 2_000_000; var coarse = false;
        void Context(int i, int j) => rows.Add(new(DiffKind.Context, i + 1, j + 1, a[i], b[j]));
        void Block(int aStart, int aEnd, int bStart, int bEnd)
        {
            while (aStart < aEnd && bStart < bEnd && a[aStart] == b[bStart]) Context(aStart++, bStart++);
            var suffix = 0;
            while (aStart < aEnd - suffix && bStart < bEnd - suffix && a[aEnd - suffix - 1] == b[bEnd - suffix - 1]) suffix++;
            aEnd -= suffix; bEnd -= suffix;
            var n = aEnd - aStart; var m = bEnd - bStart;
            var cells = (long)(n + 1) * (m + 1);
            if (n > 0 && m > 0 && cells <= budget)
            {
                budget -= (int)cells;
                var lcs = new int[n + 1, m + 1];
                for (var i = n - 1; i >= 0; i--)
                    for (var j = m - 1; j >= 0; j--) lcs[i, j] = a[aStart + i] == b[bStart + j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
                var x = 0; var y = 0;
                while (x < n || y < m)
                {
                    if (x < n && y < m && a[aStart + x] == b[bStart + y]) Context(aStart + x++, bStart + y++);
                    else if (x < n && (y == m || lcs[x + 1, y] >= lcs[x, y + 1]))
                    { var i = aStart + x++; rows.Add(new(DiffKind.Removed, i + 1, null, a[i], null)); }
                    else { var j = bStart + y++; rows.Add(new(DiffKind.Added, null, j + 1, null, b[j])); }
                }
            }
            else
            {
                if (n > 0 && m > 0) coarse = true;
                for (var i = aStart; i < aEnd; i++) rows.Add(new(DiffKind.Removed, i + 1, null, a[i], null));
                for (var j = bStart; j < bEnd; j++) rows.Add(new(DiffKind.Added, null, j + 1, null, b[j]));
            }
            for (var i = 0; i < suffix; i++) Context(aEnd + i, bEnd + i);
        }
        // Unique lines form stable anchors. The LIS keeps their order on both sides.
        var aKeys = a.Select((line, index) => (line, index)).GroupBy(pair => pair.line).Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single().index);
        var matches = b.Select((line, index) => (line, index)).GroupBy(pair => pair.line).Where(group => group.Count() == 1 && aKeys.ContainsKey(group.Key))
            .Select(group => (A: aKeys[group.Key], B: group.Single().index)).OrderBy(pair => pair.A).ToArray();
        var tails = new List<int>(); var previous = new int[matches.Length];
        for (var i = 0; i < matches.Length; i++)
        {
            var lo = 0; var hi = tails.Count;
            while (lo < hi) { var mid = (lo + hi) / 2; if (matches[tails[mid]].B < matches[i].B) lo = mid + 1; else hi = mid; }
            previous[i] = lo == 0 ? -1 : tails[lo - 1];
            if (lo == tails.Count) tails.Add(i); else tails[lo] = i;
        }
        var anchors = new List<(int A, int B)>();
        for (var i = tails.Count == 0 ? -1 : tails[^1]; i >= 0; i = previous[i]) anchors.Add(matches[i]);
        anchors.Reverse();
        var ai = 0; var bi = 0;
        foreach (var anchor in anchors) { Block(ai, anchor.A, bi, anchor.B); Context(anchor.A, anchor.B); ai = anchor.A + 1; bi = anchor.B + 1; }
        Block(ai, a.Count, bi, b.Count);
        return new(path, left, right, rows, coarse);
    }
}

public interface ITextComparisonBackend
{
    // A null target is the working file; an empty revision is an absent initial-parent side.
    Task<TextComparison> CompareAsync(WorkspaceIdentity workspace, string path, string sourceRevision, string? targetRevision);
}

public interface IHistoryContentBackend
{
    Task<TextComparison> CompareOnlineAsync(WorkspaceIdentity workspace, string path, string sourceRevision, string? targetRevision);
}
