using GoldBox.Client.Play;
using GoldBox.Core.Game;
using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;

namespace GoldBox.Client.Tests;

/// <summary>
/// s45: the fight's texts under the Amiga pages. The Amiga program holds DOS's fight strings byte for byte and draws
/// them as stored (the creature message routine hunk 13 +0x4970, the attack line hunk 4 +0x27F0, both through the text
/// window's print hunk 25 +0x10EC); DOS upper-cases. Static <see cref="AmigaView.On"/> is not touched (tests run in
/// parallel): <see cref="CombatText.Show"/> and <see cref="CombatText.Effect(string, bool)"/> take the machine.
/// </summary>
public class AmigaCombatTextTests
{
    private static string? Env(string name) =>
        Environment.GetEnvironmentVariable(name) is { } d && Directory.Exists(d) ? d : null;

    /// <summary>The texts the Amiga program does not hold (s45): Alt-M's refusal, Alt-X's message (no such paths in its
    /// fight keys, hunk 1 +0x1D5A) and handler 77's hold (its handler 77, hunk 7 +0x1A6C, prints "not affected").</summary>
    private static readonly string[] NotOnAmiga = ["CombatMsg.DoesNotWork", "CombatMsg.GodsIntervene", "CombatMsg.HandlerHeldFast"];

    [Fact]
    public void Our_wordings_follow_the_machines_case_rule()
    {
        var t = new CombatText();
        Assert.Equal("ATTACKS", t.Show(CombatMsg.Attacks, amiga: false));
        Assert.Equal(t.Raw(CombatMsg.Attacks), t.Show(CombatMsg.Attacks, amiga: true));
        Assert.Equal(t[CombatMsg.Attacks], t.Show(CombatMsg.Attacks, amiga: false));   // AmigaView.On is off in tests
        Assert.Equal(t.Effect(EffectTexts.Blessed), t.Effect(EffectTexts.Blessed, amiga: false));
    }

    /// <summary>
    /// Every fight text and effect message the Client takes from DOS's GAME.OVR / START.EXE is a C string of the Amiga
    /// <c>program</c> (the same bytes, case included) but the three it has none for; under the Amiga pages the Client
    /// shows those bytes as stored, under DOS upper case.
    /// </summary>
    [Fact]
    public void The_fight_texts_are_the_amiga_programs_string_for_string()
    {
        if (Env("GOLDBOX_POR_DIR") is not { } dos || Env("GOLDBOX_POR_AMIGA_DIR") is not { } amiga)
            return;
        var program = new PorGameDirectory(dos).Program()!;
        var bytes = File.ReadAllBytes(PorAmigaArt.Locate(amiga).Program);
        var text = new CombatText(program);
        var keys = Enum.GetValues<CombatMsg>().Select(m => "CombatMsg." + m).Concat(EffectTexts.All).ToList();
        var missing = new List<string>();
        int found = 0;
        foreach (var key in keys)
        {
            var s = program.Text(key);
            Assert.True(s is { Length: > 0 }, key);                 // every one read from the DOS program
            byte[] c = [.. System.Text.Encoding.Latin1.GetBytes(s!), 0];
            if (bytes.AsSpan().IndexOf(c) < 0)
                missing.Add(key);
            else
                found++;
        }
        Assert.Equal(NotOnAmiga.Order(), missing.Order());
        Assert.Equal(keys.Count - NotOnAmiga.Length, found);
        Assert.Equal(Enum.GetValues<CombatMsg>().Length, text.FromGame);
        foreach (var m in Enum.GetValues<CombatMsg>())
        {
            Assert.Equal(program.Text("CombatMsg." + m), text.Show(m, amiga: true));
            Assert.Equal(program.Text("CombatMsg." + m)!.ToUpperInvariant(), text.Show(m, amiga: false));
        }
        foreach (var key in EffectTexts.All)
            Assert.Equal(program.Text(key), text.Effect(key, amiga: true));
        // A few as the Amiga draws them (hunk 5 +0xA2, +0xCE, +0xF8; hunk 14 +0x88, +0x2B4).
        Assert.Equal("Attacks", text.Show(CombatMsg.Attacks, amiga: true));
        Assert.Equal("Hitting for ", text.Show(CombatMsg.HittingFor, amiga: true));
        Assert.Equal("and Misses", text.Show(CombatMsg.AndMisses, amiga: true));
        Assert.Equal("A battle begins...", text.Show(CombatMsg.BattleBegins, amiga: true));
        Assert.Equal("Hitpoints", text.Show(CombatMsg.PanelHitpoints, amiga: true));
    }

    /// <summary>
    /// The Amiga's print (hunk 25 +0x10EC) counts each word's space, delimiter or terminating NUL: a line's last word needs
    /// one cell more than on DOS (FS-UAE s45: "Hitting for 12" / "points of" / "damage" in the panel's columns 23–38).
    /// </summary>
    [Fact]
    public void The_amiga_print_wraps_a_last_word_one_cell_earlier()
    {
        const string hit = "Hitting for 12 points of damage";
        var amiga = new TextWindow(23, 1, 38, 21) { Amiga = true };
        amiga.PrintIn(23, 10, 38, 21, hit, true, 10);
        Assert.Equal(["HITTING FOR 12", "POINTS OF", "DAMAGE"], amiga.Lines.Skip(9).Take(3));   // AmigaView.On off: DOS glyphs
        var dos = new TextWindow(23, 1, 38, 21) { Amiga = false };
        dos.PrintIn(23, 10, 38, 21, hit, true, 10);
        Assert.Equal(["HITTING FOR 12", "POINTS OF DAMAGE", ""], dos.Lines.Skip(9).Take(3));
        // A word that fits with its space stays; the box full: back to the top after the page prompt, blanked.
        int pauses = 0;
        var full = new TextWindow(1, 17, 38, 18) { Amiga = true, Pause = () => pauses++ };
        full.PrintIn(1, 17, 38, 18, new string('a', 37) + " b " + new string('c', 37) + " d", true, 10);
        Assert.Equal(1, pauses);
        Assert.Equal([new string('C', 37), "D"], full.Lines);
    }

    /// <summary>
    /// DAMAGE's hit / dies line (hunk 4 +0x2384–0x239E) goes through the text window's print with pen 24 (DOS colour 15),
    /// its texts DOS's bytes (hunk 5 +0x34, +0x40, +0x4E; GAME.OVR 0x9554–0x9569); the pen reaches the module.
    /// </summary>
    [Fact]
    public void The_damage_line_takes_the_amiga_callers_pen()
    {
        if (Env("GOLDBOX_POR_DIR") is not { } dos || Env("GOLDBOX_POR_AMIGA_DIR") is not { } amiga)
            return;
        var bytes = File.ReadAllBytes(PorAmigaArt.Locate(amiga).Program);
        Assert.Equal(24, PorAmigaText.ReadCalls(bytes)!.Pens[PorAmigaText.DamageLinePen]);
        var program = new PorGameDirectory(dos).Program()!;
        foreach (var key in new[] { "Msg.DamageDies", "Msg.DamageHitFor", "Msg.DamagePoints", "Msg.DamageHitContinue" })
            Assert.True(bytes.AsSpan().IndexOf([.. System.Text.Encoding.Latin1.GetBytes(program.Text(key)!), (byte)0]) >= 0, key);
        var module = PorModule.Open(PorModuleCache.Ensure(dos, Path.Combine(Path.GetTempPath(), "goldbox-tests", "client-amiga-cache"), amiga));
        Assert.Equal(24, module.AmigaCallPens()![PorAmigaText.DamageLinePen]);
    }
}
