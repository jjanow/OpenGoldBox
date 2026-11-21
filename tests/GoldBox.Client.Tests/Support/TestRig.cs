using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Core.Ecl;

namespace GoldBox.Client.Tests.Support;

/// <summary>
/// Shared parts of the headless rigs that run a game on the player's files: start new rigs from these.
/// </summary>
public static class TestRig
{
    /// <summary>The player's PoR v1.3 folder (<c>GOLDBOX_POR_DIR</c>), or null: the tests that need it return early.</summary>
    public static string? PorDir()
    {
        var d = Environment.GetEnvironmentVariable("GOLDBOX_POR_DIR");
        return string.IsNullOrWhiteSpace(d) || !Directory.Exists(d) ? null : d;
    }

    /// <summary>
    /// A <see cref="ClientPlayer"/> for a rig that steps through scripts: <see cref="ClientPlayer.TypesText"/> is off,
    /// so PRINT draws its text at once instead of asking one chained <see cref="DelayRequest"/> per character (the
    /// game's typing, DS:4A00, stays on in the Client). Pass <paramref name="typesText"/> = true for a rig that checks
    /// the typing.
    /// </summary>
    public static ClientPlayer Player(ScriptRunner runner, Func<EclMemory> memory, bool typesText = false)
    {
        var player = new ClientPlayer(runner, memory) { TypesText = typesText };
        runner.Answered = player.MenuEnded; // as Program.cs: every program menu clears VM $4AFF (needs Game set)
        return player;
    }

    /// <summary>A screen host that ignores pushes, pops and quits.</summary>
    public sealed class NoHost : IScreenHost
    {
        public void Push(IScreen screen) { }
        public void Pop() { }
        public void Quit() { }
        public void SaveBindings() { }
    }
}
