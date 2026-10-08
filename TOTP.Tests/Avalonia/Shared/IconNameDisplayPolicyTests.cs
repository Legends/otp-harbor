using TOTP.Avalonia.Shared.Presentation;

namespace TOTP.Tests.Avalonia.Shared;

public sealed class IconNameDisplayPolicyTests
{
    [Fact]
    public void Truncate_PreservesNamesUpToFiftyCharacters()
    {
        var name = new string('a', IconNameDisplayPolicy.MaximumVisibleCharacters);

        Assert.Equal(name, IconNameDisplayPolicy.Truncate(name));
    }

    [Fact]
    public void Truncate_AppendsEllipsisAfterFiftyCharacters()
    {
        var name = new string('a', IconNameDisplayPolicy.MaximumVisibleCharacters + 1);

        Assert.Equal(
            new string('a', IconNameDisplayPolicy.MaximumVisibleCharacters) + "...",
            IconNameDisplayPolicy.Truncate(name));
    }
}
