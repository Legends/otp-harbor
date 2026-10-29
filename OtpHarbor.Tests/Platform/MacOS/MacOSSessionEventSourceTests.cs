using Microsoft.Extensions.Logging;
using Moq;
using TOTP.Core.Platform;
using TOTP.Platform.MacOS;

namespace TOTP.Tests.Platform.MacOS;

public sealed class MacOSSessionEventSourceTests
{
    [Fact]
    public void Poll_MapsLockStateAndSuppressesDuplicates()
    {
        var reader = new FakeReader { Locked = false };
        using var sut = CreateSut(reader);
        var states = new List<PlatformSessionState>();
        sut.SessionChanged += (_, args) => states.Add(args.State);

        sut.Poll();
        sut.Poll();
        reader.Locked = true;
        sut.Poll();

        Assert.Equal([PlatformSessionState.Active, PlatformSessionState.Locked], states);
    }

    [Fact]
    public void Poll_WhenStateIsUnavailable_DoesNotGuessFromApplicationState()
    {
        var reader = new FakeReader { Locked = null };
        using var sut = CreateSut(reader);
        var raised = false;
        sut.SessionChanged += (_, _) => raised = true;

        sut.Poll();

        Assert.False(raised);
    }

    [Fact]
    public void Start_WhenReaderIsUnsupported_DoesNotPoll()
    {
        var reader = new FakeReader { IsSupported = false, Locked = true };
        using var sut = CreateSut(reader);

        sut.Start();

        Assert.Equal(0, reader.ReadCount);
    }

    private static MacOSSessionEventSource CreateSut(IMacOSSessionStateReader reader) =>
        new(reader, Mock.Of<ILogger<MacOSSessionEventSource>>(), TimeSpan.FromHours(1));

    private sealed class FakeReader : IMacOSSessionStateReader
    {
        public bool IsSupported { get; set; } = true;
        public bool? Locked { get; set; }
        public int ReadCount { get; private set; }

        public bool? IsScreenLocked()
        {
            ReadCount++;
            return Locked;
        }
    }
}
