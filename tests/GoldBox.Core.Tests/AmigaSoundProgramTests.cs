using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoldBox.Core.Sound;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>
/// The Amiga music player against the program itself (docs/re/sound.md §5): <c>tools/scripts/por_amiga_sound_oracle.py</c>
/// runs v1.0's own player under emulation and keeps, per 25 vertical blanks, a hash of every custom-chip write (private
/// fixture <c>amiga-v1.0/sound_player.json</c>). <see cref="AmigaMusicPlayer"/>, fed the data
/// <see cref="PorAmigaSound"/> reads from the player's <c>program</c>, must write the same registers in the same order.
/// </summary>
public class AmigaSoundProgramTests
{
    private static readonly string? FixturePath = Find();

    private static string? Find()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "tests", "fixtures-private", "por", "amiga-v1.0", "sound_player.json");
            if (File.Exists(p))
                return p;
        }
        return null;
    }

    public sealed class AmigaOracleFactAttribute : FactAttribute
    {
        public AmigaOracleFactAttribute()
        {
            if (PorAmigaData.Dir is null)
                Skip = "needs the player's Amiga Pool of Radiance v1.0 files: set " + PorAmigaData.EnvVar;
            else if (PorAmigaData.Missing is { } missing)
                Skip = missing;
            else if (FixturePath is null)
                Skip = "needs the private fixture tests/fixtures-private/por/amiga-v1.0/sound_player.json";
        }
    }

    private static readonly Lazy<JsonElement> Root = new(() => JsonDocument.Parse(File.ReadAllText(FixturePath!)).RootElement);

    private static readonly Lazy<AmigaSoundImage> Image = new(() =>
    {
        var (program, _) = PorAmigaArt.Locate(PorAmigaData.Dir!);
        return PorAmigaSound.Read(File.ReadAllBytes(program)) ?? throw new InvalidOperationException("players not found");
    });

    /// <summary>Formats writes as the oracle does: "REG:VALUE" hex, DMACON masked to its set and audio bits, AUDxLC as hunk:offset.</summary>
    private sealed class Recorder : IPaulaRegisters
    {
        public readonly StringBuilder Line = new();

        private void Add(string s)
        {
            if (Line.Length > 0)
                Line.Append(' ');
            Line.Append(s);
        }

        public void Word(int register, int value) =>
            Add(register == 0x96 ? $"96:{value & 0x800F:x}" : $"{register:x}:{value & 0xFFFF:x}");

        public void Long(int register, int address)
        {
            int sample = address - AmigaEffectPlayer.Base;
            if ((uint)sample < (uint)Image.Value.EffectSamples.Length)
            {
                Add($"{register:x}:29:{sample:x}");
                return;
            }
            int o = address - AmigaMusicPlayer.Base;
            Add((uint)o < (uint)Image.Value.Music.Length ? $"{register:x}:28:{o:x}" : $"{register:x}:{address:x}");
        }
    }

    [AmigaOracleFact]
    public void Every_sampled_effect_writes_what_the_program_writes()
    {
        var root = Root.Value;
        int block = root.GetProperty("block").GetInt32(), ticks = Math.Min(600, root.GetProperty("ticks").GetInt32());
        var effects = root.GetProperty("effects").EnumerateObject().ToArray();
        Assert.Equal(15, effects.Length);
        foreach (var effect in effects)
        {
            var rec = new Recorder();
            var player = Assert.IsType<AmigaEffectPlayer>(AmigaEffectPlayer.Create(Image.Value, rec));
            Assert.True(player.Play(int.Parse(effect.Name)));
            var lines = new List<string>();
            for (int tick = 0; tick < ticks; tick++)
            {
                rec.Line.Clear();
                player.Tick();
                lines.Add(rec.Line.ToString());
            }
            Check("effect " + effect.Name, lines, effect.Value, block);
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

    private static void Check(string name, List<string> got, JsonElement want, int block)
    {
        var mine = Blocks(got, block);
        var theirs = want.EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(theirs.Count, mine.Count);
        for (int i = 0; i < mine.Count; i++)
            Assert.True(theirs[i] == mine[i], $"{name}: ticks {i * block}..{i * block + block - 1} differ");
    }

    private static List<string> Run(IEnumerable<(int Ticks, int Song)> steps)
    {
        var rec = new Recorder();
        var p = new AmigaMusicPlayer(Image.Value, rec);
        var lines = new List<string>();
        foreach (var (ticks, song) in steps)
        {
            p.PlaySong(song);
            for (int t = 0; t < ticks; t++)
            {
                rec.Line.Clear();
                p.Tick();
                lines.Add(rec.Line.ToString());
            }
        }
        return lines;
    }

    [AmigaOracleFact]
    public void The_players_data_is_found_through_their_code()
    {
        var d = Image.Value;
        Assert.Equal(78_504, d.Music.Length);
        Assert.Equal(167, d.MusicRelocs.Length);
        Assert.Equal(15, d.EffectRelocs.Length);
        Assert.Equal(58_284, d.EffectSamples.Length);
        Assert.Equal(0x574, d.EffectsOrigin);
        Assert.Equal(13, d.Cues.Length);
        Assert.Equal(2, d.Cues[^1]); // the battle song
    }

    [AmigaOracleFact]
    public void Every_song_writes_what_the_program_writes()
    {
        var root = Root.Value;
        int block = root.GetProperty("block").GetInt32(), ticks = root.GetProperty("ticks").GetInt32(), n = 0;
        foreach (var s in root.GetProperty("songs").EnumerateObject())
        {
            Check("song " + s.Name, Run([(ticks, int.Parse(s.Name))]), s.Value, block);
            n++;
        }
        Assert.Equal(7, n);
    }

    [AmigaOracleFact]
    public void A_song_started_over_another_and_a_stop_match_the_program()
    {
        var root = Root.Value;
        int block = root.GetProperty("block").GetInt32();
        Check("1-3-0", Run([(300, 1), (300, 3), (50, 0)]), root.GetProperty("cases").GetProperty("1-3-0"), block);
    }

    /// <summary>
    /// An effect over a running song (s43, docs/re/sound.md §5.4): PlayEffect clears "music on", so from the next
    /// vertical blank the effect tick runs instead of the music tick. Both players write the same chip: song 2 for
    /// 100 ticks, effect 4 (the step) for 100, then song 2 again, as the oracle's case "2-e4-2".
    /// </summary>
    [AmigaOracleFact]
    public void An_effect_over_a_running_song_matches_the_program()
    {
        var root = Root.Value;
        int block = root.GetProperty("block").GetInt32();
        var rec = new Recorder();
        var music = new AmigaMusicPlayer(Image.Value, rec);
        var effects = Assert.IsType<AmigaEffectPlayer>(AmigaEffectPlayer.Create(Image.Value, rec));
        var lines = new List<string>();
        void Ticks(Action tick)
        {
            for (int t = 0; t < 100; t++)
            {
                rec.Line.Clear();
                tick();
                lines.Add(rec.Line.ToString());
            }
        }
        music.PlaySong(2);
        Ticks(music.Tick);
        Assert.True(effects.Play(4));
        Ticks(effects.Tick);
        music.PlaySong(2);
        Ticks(music.Tick);
        // the first effect tick switches all four channels off with 1-3 at volume 0; channel 0 comes on one tick later
        Assert.Contains("96:f a8:3c b8:0 c8:0 d8:0", lines[100]);
        Assert.Contains("96:8001 96:e", lines[101]);
        Check("2-e4-2", lines, root.GetProperty("cases").GetProperty("2-e4-2"), block);
    }

    /// <summary>
    /// The Client's mixer (<see cref="AmigaMusic"/>): an effect ends the song on every channel at the next tick (the
    /// right side, channels 1 and 2, is silent from then on) and nothing brings it back when the effect is over.
    /// </summary>
    [AmigaOracleFact]
    public void An_effect_ends_the_running_song_for_good()
    {
        var music = new AmigaMusic(Image.Value, 44_100) { Filter = false };
        music.Play(2);
        var left = new float[44_100 * 2];
        var right = new float[left.Length];
        music.Render(left, right);
        Assert.True(right.Max(Math.Abs) > 0.01f);
        music.PlayEffect(SoundCue.Step);
        left = new float[44_100 * 6];
        right = new float[left.Length];
        music.Render(left, right);
        int tick = (int)Math.Ceiling(44_100 / AmigaMusic.TickHz);
        Assert.All(right.Skip(tick), x => Assert.Equal(0f, x));
        Assert.True(left.Skip(2 * tick).Take(20 * tick).Max(Math.Abs) > 0.01f); // the step effect on channel 0
        Assert.All(left.Skip(44_100 * 3), x => Assert.Equal(0f, x));            // ended; song 2 does not resume
    }

    [AmigaOracleFact]
    public void Songs_2_to_5_stop_by_themselves_and_1_loops()
    {
        foreach (var (song, stops) in new[] { (1, false), (2, true), (3, true), (4, true), (5, true), (6, false) })
        {
            var p = new AmigaMusicPlayer(Image.Value, new Recorder());
            p.PlaySong(song);
            for (int t = 0; t < 50 * 120; t++)
                p.Tick();
            Assert.Equal(!stops, p.IsPlaying);
        }
    }

    [AmigaOracleFact]
    public void The_intro_song_renders_sound()
    {
        var music = new AmigaMusic(Image.Value, 44_100);
        music.Play(1);
        var left = new float[44_100 * 4];
        var right = new float[left.Length];
        music.Render(left, right);
        foreach (var buf in new[] { left, right })
        {
            float peak = buf.Max(Math.Abs);
            double rms = Math.Sqrt(buf.Average(x => (double)x * x));
            Assert.InRange(peak, 0.05f, 1.5f);
            Assert.True(rms > 0.01, $"rms {rms}");
        }
        music.Play(0);
        var l = new float[44_100];
        var r = new float[l.Length];
        music.Render(l, r); // the stop is taken at the next tick; the output filter rings out
        Array.Clear(l);
        Array.Clear(r);
        music.Render(l, r);
        Assert.All(l, x => Assert.True(Math.Abs(x) < 1e-4f));
        Assert.All(r, x => Assert.True(Math.Abs(x) < 1e-4f));
        music.Filter = false;
        Array.Clear(l);
        music.Render(l, r);
        Assert.All(l, x => Assert.Equal(0f, x));
    }

    [AmigaOracleFact]
    public void Effects_play_on_the_left_channel_only()
    {
        var music = new AmigaMusic(Image.Value, 44_100);
        music.PlayEffect(SoundCue.Impact);
        var left = new float[22_050];
        var right = new float[left.Length];
        music.Render(left, right);
        Assert.True(left.Max(Math.Abs) > 0.01f);
        Assert.All(right, x => Assert.Equal(0f, x));
    }
}
