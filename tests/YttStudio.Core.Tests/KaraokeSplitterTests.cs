using YttStudio.Core.Editing;

namespace YttStudio.Core.Tests;

public sealed class KaraokeSplitterTests
{
    private readonly KaraokeSplitter splitter = new();

    [Fact]
    public void SplitsHangulIntoSingleSyllablesAndRetainsSpace()
    {
        Assert.Equal(["한", "글", " ", "테", "스", "트"], splitter.Split("한글 테스트"));
    }

    [Fact]
    public void SplitsBasicHiraganaAndKatakana()
    {
        Assert.Equal(["か", "な", "カ", "ナ"], splitter.Split("かなカナ"));
    }

    [Fact]
    public void CombinesSmallKanaWithThePreviousKana()
    {
        Assert.Equal(["きゃっ", "てっ"], splitter.Split("きゃってっ"));
    }

    [Fact]
    public void GroupsLatinWordsIncludingDigits()
    {
        Assert.Equal(["Hello123", " ", "café"], splitter.Split("Hello123 café"));
    }

    [Fact]
    public void SplitsHanIncludingAstralCharacters()
    {
        Assert.Equal(["漢", "字", "𠮷"], splitter.Split("漢字𠮷"));
    }

    [Fact]
    public void PreservesMixedScriptsAndPunctuation()
    {
        Assert.Equal(["가", "ナ", "A1", "中", " ", "!"], splitter.Split("가ナA1中 !"));
    }
}
