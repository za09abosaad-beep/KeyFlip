using KeyFlip;
using Xunit;

public class LayoutSwitcherTests
{
    private static IntPtr H(long v) => new IntPtr(v);

    [Fact] public void NoMatch_ReturnsNull() =>
        Assert.Null(LayoutSwitcher.Choose(new[] { H(0x040C040C) }, LayoutSwitcher.LangArabic));

    [Fact] public void EmptyList_ReturnsNull() =>
        Assert.Null(LayoutSwitcher.Choose(Array.Empty<IntPtr>(), LayoutSwitcher.LangEnglish));

    // Arabic 102 under Saudi locale vs Arabic 101 under Egypt locale: the keyboard decides.
    [Fact] public void PrefersArabic101Keyboard_OverOtherKeyboard() =>
        Assert.Equal(H(0x04010C01), LayoutSwitcher.Choose(new[] { H(0xF0230401), H(0x04010C01) }, LayoutSwitcher.LangArabic));

    [Fact] public void AcceptsOtherArabicVariant() =>
        Assert.Equal(H(0x04010C01), LayoutSwitcher.Choose(new[] { H(0x04090409), H(0x04010C01) }, LayoutSwitcher.LangArabic));

    [Fact] public void PrefersUs_OverUk() =>
        Assert.Equal(H(0x04090409), LayoutSwitcher.Choose(new[] { H(0x08090809), H(0x04090409) }, LayoutSwitcher.LangEnglish));

    [Fact] public void PrefersUsKeyboard_UnderUkLocale() =>
        Assert.Equal(H(0x04090809), LayoutSwitcher.Choose(new[] { H(0x08090809), H(0x04090809) }, LayoutSwitcher.LangEnglish));

    [Fact] public void AcceptsUkWhenNoUs() =>
        Assert.Equal(H(0x08090809), LayoutSwitcher.Choose(new[] { H(0x04010401), H(0x08090809) }, LayoutSwitcher.LangEnglish));
}
