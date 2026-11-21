using System.Globalization;
using GoldBox.Core.Sound;
using GoldBox.Data.Por;

namespace GoldBox.Tools;

/// <summary>
/// amiga-sound: renders Pool of Radiance Amiga's music and effects (docs/re/sound.md §5) as the Client plays them, to a
/// 16-bit stereo WAV, for comparing with emulator captures. Reads the players' data from disk A's <c>program</c>.
/// <code>
///   amiga-sound PROGRAM OUT.wav [--song N] [--effect CUE@SECONDS …] [--seconds S] [--rate HZ] [--no-filter] [--mute MASK]
/// </code>
/// --song N starts song N at 0 s; --effect plays a sound cue (a <see cref="SoundCue"/> name) at the given time, as a
/// fight's sound call does (it ends the music); --no-filter leaves out the A500 output stage; --mute
/// leaves out channels (a hex bit mask).
/// </summary>
public static class SoundCommands
{
    public static int AmigaRender(string[] a)
    {
        if (a.Length < 2)
        {
            Console.Error.WriteLine("usage: amiga-sound PROGRAM OUT.wav [--song N] [--effect CUE@SECONDS …] [--seconds S] [--rate HZ] [--no-filter] [--mute MASK]");
            return 2;
        }
        var image = PorAmigaSound.Read(File.ReadAllBytes(a[0]))
            ?? throw new FormatException("not Pool of Radiance Amiga v1.0's program");
        int song = 0, rate = 48000;
        bool filter = true;
        int mute = 0;
        double seconds = 10;
        var effects = new List<(double At, SoundCue Cue)>();
        for (int i = 2; i < a.Length; i++)
        {
            switch (a[i])
            {
                case "--song": song = int.Parse(a[++i], CultureInfo.InvariantCulture); break;
                case "--seconds": seconds = double.Parse(a[++i], CultureInfo.InvariantCulture); break;
                case "--rate": rate = int.Parse(a[++i], CultureInfo.InvariantCulture); break;
                case "--no-filter": filter = false; break;
                case "--mute": mute = Convert.ToInt32(a[++i], 16); break;
                case "--effect":
                    var p = a[++i].Split('@');
                    effects.Add((double.Parse(p[1], CultureInfo.InvariantCulture), Enum.Parse<SoundCue>(p[0], true)));
                    break;
                default: throw new ArgumentException("unknown option " + a[i]);
            }
        }
        var music = new AmigaMusic(image, rate) { Filter = filter, Mute = mute };
        if (song != 0)
            music.Play(song);
        int total = (int)(seconds * rate), block = rate / 100;
        var left = new float[block];
        var right = new float[block];
        using var outFile = File.Create(a[1]);
        using var w = new BinaryWriter(outFile);
        w.Write("RIFF"u8);
        w.Write(36 + total * 4);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)2);
        w.Write(rate);
        w.Write(rate * 4);
        w.Write((short)4);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(total * 4);
        effects.Sort();
        int next = 0;
        for (int done = 0; done < total; done += block)
        {
            while (next < effects.Count && effects[next].At * rate <= done)
                music.PlayEffect(effects[next++].Cue);
            int n = Math.Min(block, total - done);
            Array.Clear(left);
            Array.Clear(right);
            music.Render(left.AsSpan(0, n), right.AsSpan(0, n));
            for (int k = 0; k < n; k++)
            {
                w.Write((short)Math.Round(Math.Clamp(left[k], -1f, 1f) * 32767));
                w.Write((short)Math.Round(Math.Clamp(right[k], -1f, 1f) * 32767));
            }
        }
        return 0;
    }
}
