using KeyFlip;
using Xunit;

public class KeyLayoutTests
{
    private static readonly KeyLayout Layout = KeyLayout.Load(KeyLayout.DefaultId);

    [Fact] public void EmbeddedLayout_Loads()
    {
        Assert.Equal("en-us_ar-101", Layout.Id);
        Assert.False(string.IsNullOrWhiteSpace(Layout.Name));
        Assert.Equal(66, Layout.Keys.Count);
    }

    [Fact] public void EmbeddedLayout_NoDuplicateKeys()
    {
        Assert.Equal(Layout.Keys.Count, Layout.Keys.Select(k => k.En).Distinct().Count());
        var singles = Layout.Keys.Where(k => k.Ar.Length == 1).Select(k => k.Ar).ToList();
        Assert.Equal(singles.Count, singles.Distinct().Count());
        var alefs = Layout.Keys.Where(k => k.Ar.Length == 2).Select(k => k.Ar[1]).ToList();
        Assert.Equal(alefs.Count, alefs.Distinct().Count());
    }

    [Fact] public void EmbeddedLayout_LamAlefKeys()
    {
        var two = Layout.Keys.Where(k => k.Ar.Length == 2).Select(k => k.En).OrderBy(c => c).ToArray();
        Assert.Equal(new[] { 'B', 'G', 'T', 'b' }, two);
    }

    [Fact] public void EveryRow_RoundTrips()
    {
        foreach (var (en, ar) in Layout.Keys)
        {
            Assert.Equal(ar, KeyboardConverter.Convert(en.ToString(), KeyboardConverter.Direction.EnglishToArabic));
            Assert.Equal(en.ToString(), KeyboardConverter.Convert(ar, KeyboardConverter.Direction.ArabicToEnglish));
        }
    }

    [Fact] public void ConverterUsesEmbeddedLayout() =>
        Assert.Equal(Layout.Keys, KeyboardConverter.Table);

    [Fact] public void UnknownLayout_ThrowsNamingFile()
    {
        var e = Assert.Throws<InvalidDataException>(() => KeyLayout.Load("nope"));
        Assert.Contains("nope.json", e.Message);
    }
}
