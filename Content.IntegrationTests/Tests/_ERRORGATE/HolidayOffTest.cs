#nullable enable
using Content.Server.Holiday;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ERRORGATE;

/// <summary>
///     Holiday greetings ("Have a happy National Coming Out Day!") stay off: the cvar is off by default and the system must obey it
///     from the start, not only after the cvar changes.
/// </summary>
[TestFixture]
public sealed class HolidayOffTest
{
    [Test]
    public async Task NoHolidayIsActiveWithTheDefaultSetting()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var holidays = server.EntMan.System<HolidaySystem>();
            holidays.RefreshCurrentHolidays();
            Assert.That(holidays.GetCurrentHolidays(), Is.Empty);
        });

        await pair.CleanReturnAsync();
    }
}
