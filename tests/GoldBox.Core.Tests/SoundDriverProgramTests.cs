using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoldBox.Core.Sound;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>
/// The sound driver against the program itself: <c>tools/scripts/por_sound_oracle.py</c> runs v1.3's driver under
/// emulation and keeps, per 25 ticks, a hash of every port write (private fixture
/// <c>program-v1.3/sound_driver.json</c>). <see cref="PcSoundDriver"/>, fed the data <see cref="PorSoundDriver"/> reads
/// from the player's START.EXE, must write the same bytes to the same ports on every tick.
/// </summary>
public class SoundDriverProgramTests
{
    private static readonly string? FixturePath = Find();

    private static string? Find()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "tests", "fixtures-private", "por", "program-v1.3", "sound_driver.json");
            if (File.Exists(p))
                return p;
        }
        return null;
    }

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (FixturePath is null)
                Skip = "needs the private fixture tests/fixtures-private/por/program-v1.3/sound_driver.json";
            else if (!PorData.Present)
                Skip = PorData.SkipReason;
        }
    }

    private static readonly Lazy<JsonElement> Root = new(() => JsonDocument.Parse(File.ReadAllText(FixturePath!)).RootElement);

    private static readonly Lazy<PcSoundDriverImage> Image = new(() =>
    {
        var exe = PorData.Open().Find("START.EXE")!;
        var (img, _, _) = PorProgram.LoadImage(File.ReadAllBytes(exe));
        return PorSoundDriver.Read(img) ?? throw new InvalidOperationException("driver not found");
    });

    private sealed class Recorder : ISoundPorts
    {
        public readonly StringBuilder Line = new();
        public void Out(int port, byte value)
        {
            if (Line.Length > 0)
                Line.Append(' ');
            Line.Append($"{port:X2}:{value:X2}");
        }
    }

    private static List<string> Blocks(List<string> ticks, int block)
    {
        var r = new List<string>();
        for (int i = 0; i < ticks.Count; i += block)
        {
            var text = string.Join('\n', ticks.Skip(i).Take(block));
            r.Add(Convert.ToHexStringLower(SHA1.HashData(Encoding.ASCII.GetBytes(text)))[..16]);
        }
        return r;
    }

    private static List<string> Run(PcSoundDevice dev, int tune, int ticks, (int At, int Tune)? second = null)
    {
        var rec = new Recorder();
        var drv = new PcSoundDriver(Image.Value, rec);
        drv.Program(dev, tune);
        var lines = new List<string>();
        for (int t = 0; t < ticks; t++)
        {
            if (second is { } s && t == s.At)
                drv.Program(dev, s.Tune);
            rec.Line.Clear();
            drv.Tick();
            lines.Add(rec.Line.ToString());
        }
        return lines;
    }

    private static void Check(string name, List<string> got, JsonElement want, int block)
    {
        var mine = Blocks(got, block);
        var theirs = want.EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(theirs.Count, mine.Count);
        for (int i = 0; i < mine.Count; i++)
            Assert.True(theirs[i] == mine[i], $"{name}: ticks {i * block}..{i * block + block - 1} differ");
    }

    [OracleFact]
    public void The_driver_is_found_by_its_code()
    {
        var d = Image.Value;
        var drv = Root.Value.GetProperty("driver");
        Assert.Equal(drv.GetProperty("divisor").GetInt32(), d.TimerDivisor);
        Assert.Equal(21, d.TuneCount);
        Assert.Equal(13, d.BiosChainTicks);
        Assert.Equal(0x30, d.VoiceSize);
        Assert.Equal(236.7, d.TickHz, 1);
    }

    /// <summary>
    /// Every tune on both devices, 600 ticks. Tandy tune 18 is left out: its stream makes the driver read bytes of its
    /// own code (below the data the importer keeps), which the module does not hold; no PoR event plays it.
    /// </summary>
    [OracleFact]
    public void Every_tune_writes_what_the_program_writes()
    {
        var root = Root.Value;
        int block = root.GetProperty("block").GetInt32(), ticks = root.GetProperty("ticks").GetInt32(), n = 0;
        foreach (var t in root.GetProperty("tunes").EnumerateObject())
        {
            var parts = t.Name.Split('/');
            var dev = parts[0] == "speaker" ? PcSoundDevice.Speaker : PcSoundDevice.Tandy;
            int tune = int.Parse(parts[1]);
            if (dev == PcSoundDevice.Tandy && tune == 18)
                continue;
            Check(t.Name, Run(dev, tune, ticks), t.Value, block);
            n++;
        }
        Assert.Equal(41, n);
    }

    [OracleFact]
    public void A_tune_started_over_another_matches_the_program()
    {
        var root = Root.Value;
        int block = root.GetProperty("block").GetInt32(), ticks = root.GetProperty("ticks").GetInt32(), n = 0;
        foreach (var c in root.GetProperty("cases").EnumerateObject())
        {
            // "speaker/1+40/9": tune 1, then tune 9 programmed before tick 40.
            var parts = c.Name.Split('/', '+');
            var dev = parts[0] == "speaker" ? PcSoundDevice.Speaker : PcSoundDevice.Tandy;
            Check(c.Name, Run(dev, int.Parse(parts[1]), ticks, (int.Parse(parts[2]), int.Parse(parts[3]))), c.Value, block);
            n++;
        }
        Assert.Equal(3, n);
    }
}
