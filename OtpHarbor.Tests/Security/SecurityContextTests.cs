using System.Reflection;
using TOTP.Infrastructure.Security;

namespace TOTP.Tests.Security;

public sealed class SecurityContextTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void SetDek_WithInvalidLength_FailsClosed(int length)
    {
        using var sut = new SecurityContext();

        var action = () => sut.SetDek(new byte[length]);

        Assert.Throws<ArgumentException>(action);
        Assert.False(sut.IsUnlocked);
    }

    [Fact]
    public void SetDek_CopiesCallerOwnedInput()
    {
        var input = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var expected = (byte[])input.Clone();
        using var sut = new SecurityContext();

        sut.SetDek(input);
        input.AsSpan().Clear();
        var storedCopy = sut.GetDekCopy();

        try
        {
            Assert.Equal(expected, storedCopy);
        }
        finally
        {
            storedCopy.AsSpan().Clear();
        }
    }

    [Fact]
    public void GetDekCopy_ReturnsIndependentCallerOwnedBuffer()
    {
        using var sut = new SecurityContext();
        sut.SetDek(Enumerable.Repeat((byte)7, 32).ToArray());
        var first = sut.GetDekCopy();
        first[0] = 99;
        var second = sut.GetDekCopy();

        try
        {
            Assert.Equal(7, second[0]);
        }
        finally
        {
            first.AsSpan().Clear();
            second.AsSpan().Clear();
        }
    }

    [Fact]
    public void Lock_ZeroesPinnedKeyBufferBeforeRelease()
    {
        using var sut = new SecurityContext();
        sut.SetDek(Enumerable.Repeat((byte)9, 32).ToArray());
        var field = typeof(SecurityContext).GetField(
            "_rawDek",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var retainedBuffer = Assert.IsType<byte[]>(field?.GetValue(sut));

        sut.Lock();

        Assert.False(sut.IsUnlocked);
        Assert.All(retainedBuffer, value => Assert.Equal(0, value));
    }

    [Fact]
    public void RepeatedUnlockLockCycles_AlwaysZeroReleasedKeyBuffers()
    {
        using var sut = new SecurityContext();
        var field = typeof(SecurityContext).GetField(
            "_rawDek",
            BindingFlags.Instance | BindingFlags.NonPublic);

        for (var cycle = 0; cycle < 1_000; cycle++)
        {
            var input = Enumerable.Repeat((byte)((cycle % 254) + 1), 32).ToArray();
            sut.SetDek(input);
            var retainedBuffer = Assert.IsType<byte[]>(field?.GetValue(sut));

            sut.Lock();

            Assert.False(sut.IsUnlocked);
            Assert.All(retainedBuffer, value => Assert.Equal(0, value));
        }
    }
}
