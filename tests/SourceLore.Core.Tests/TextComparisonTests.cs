using System.Text;
using SourceLore.Core.Workspaces;

namespace SourceLore.Core.Tests;

public class TextComparisonTests
{
    [Theory]
    [InlineData("same", false)]
    [InlineData("staged", true)]
    [InlineData("00000000", false)]
    public void CommittedSecondParentDoesNotMeanUnfinishedMerge(string staged, bool merging)
    {
        Assert.Equal(merging, new WorkspaceSnapshot("same", "main", "other-parent", [], DateTimeOffset.UtcNow, staged).HasMerge);
    }
    [Theory]
    [InlineData("", "新增中文\n")]
    [InlineData("删除中文\r\n", "")]
    [InlineData("相同\r\n尾行", "相同\n尾行\n")]
    [InlineData("甲\n乙\n丙\n", "甲\n丁\n丙\n")]
    [InlineData("", "")]
    public void EveryRowReconstructsExactBothSides(string before, string after)
    {
        var result = Build(before, after);
        Assert.Equal(before, string.Concat(result.Rows.Where(row => row.Left is not null).Select(row => row.Left!.Exact)));
        Assert.Equal(after, string.Concat(result.Rows.Where(row => row.Right is not null).Select(row => row.Right!.Exact)));
        Assert.Equal(result.LeftView.Count(character => character == '\n'), result.RightView.Count(character => character == '\n'));
        Assert.True(result.LeftView.Count(character => character == '\n') <= result.Rows.Count);
        if (before != after) Assert.Contains(result.Rows, row => row.Kind != DiffKind.Context);
    }
    [Fact]
    public void UnicodeBomAndEndingsAreExplicitWithoutChangingBytes()
    {
        foreach (var encoding in new Encoding[] { new UTF8Encoding(true), new UnicodeEncoding(false, true), new UnicodeEncoding(true, true) })
        {
            var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("中文\r\n末行")).ToArray();
            var backup = bytes.ToArray();
            var text = TextContent.Decode(bytes);
            Assert.Equal("中文\r\n末行", text.Text);
            Assert.Contains("BOM", text.Encoding);
            Assert.Contains("CRLF", text.EndingLabel);
            Assert.Contains("无末尾换行", text.EndingLabel);
            Assert.Equal(backup, bytes);
        }
    }
    [Fact]
    public void LimitsAcceptBoundaryAndRejectBeyondItAndInvalidEncodings()
    {
        Assert.Single(TextContent.Decode(Encoding.UTF8.GetBytes(new string('a', TextContent.MaxLineLength))).Lines);
        Assert.Throws<InvalidDataException>(() => TextContent.Decode(Encoding.UTF8.GetBytes(new string('a', TextContent.MaxLineLength + 1))));
        Assert.Equal(TextContent.MaxLines, TextContent.Decode(Encoding.UTF8.GetBytes(new string('\n', TextContent.MaxLines))).Lines.Count);
        Assert.Throws<InvalidDataException>(() => TextContent.Decode(Encoding.UTF8.GetBytes(new string('\n', TextContent.MaxLines + 1))));
        Assert.Throws<InvalidDataException>(() => TextContent.Decode(new byte[TextContent.MaxBytes + 1]));
        Assert.Throws<InvalidDataException>(() => TextContent.Decode([0xff]));
        Assert.Throws<InvalidDataException>(() => TextContent.Decode([0xff, 0xfe, 0x61]));
        Assert.Throws<InvalidDataException>(() => TextContent.Decode([0x61, 0]));
        var boundary = Enumerable.Repeat((byte)'a', TextContent.MaxBytes).ToArray();
        for (var i = 100; i < boundary.Length; i += 101) boundary[i] = (byte)'\n';
        Assert.Equal(TextContent.MaxBytes, TextContent.Decode(boundary).Text.Length);
    }
    [Fact]
    public void LargeRepeatedRewriteHasBoundedFallbackAndStillReconstructsBothSides()
    {
        var before = string.Concat(Enumerable.Repeat("a\n", 2000));
        var after = string.Concat(Enumerable.Repeat("b\n", 2000));
        var diff = Build(before, after);
        Assert.True(diff.UsedCoarseBlocks);
        Assert.Equal(before, string.Concat(diff.Rows.Where(row => row.Left is not null).Select(row => row.Left!.Exact)));
        Assert.Equal(after, string.Concat(diff.Rows.Where(row => row.Right is not null).Select(row => row.Right!.Exact)));
    }
    private static TextComparison Build(string a, string b) => TextComparison.Build("程序.txt", new("版本", true, TextContent.Decode(Encoding.UTF8.GetBytes(a))), new("工作文件", true, TextContent.Decode(Encoding.UTF8.GetBytes(b))));
    [Fact]
    public void ReplacementLinesShareTheSameVisualRowOnBothSides()
    {
        var comparison = Build("共同\n旧内容\n", "共同\n新内容\n");
        Assert.Contains("旧内容", comparison.LeftView.Split('\n')[1]);
        Assert.Contains("新内容", comparison.RightView.Split('\n')[1]);
        Assert.Contains("2", comparison.LeftView.Split('\n')[1]);
        Assert.Contains("2", comparison.RightView.Split('\n')[1]);
    }
}
