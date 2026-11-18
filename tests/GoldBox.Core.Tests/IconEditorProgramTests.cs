using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// The icon editor (GAME.OVR 0x1D1AC) against the program itself: the private fixture <c>program-v1.3/icon_editor.json</c>
/// (<c>tools/scripts/por_icon_oracle.py</c>) holds, per case, the icon bytes 0xBD..0xC6 before, the menu answers (a
/// hotkey, '0' for Escape), the IconOk answers and the bytes the routine left. <see cref="IconEditor"/> fed the same
/// answers must leave the same bytes.
/// </summary>
public class IconEditorProgramTests
{
    private static readonly string? File_ = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "..", "program-v1.3", "icon_editor.json") : null;

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture program-v1.3/icon_editor.json";
        }
    }

    [OracleFact]
    public void Matches_0x1D1AC_under_emulation()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var fails = new List<string>();
        int n = 0;
        foreach (var k in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            n++;
            var c = new EclCharacter();
            Convert.FromHexString(k.GetProperty("icon").GetString()!).CopyTo(c.Record, IconEditor.Head);
            var ed = new IconEditor(c);
            var answers = new Queue<char>(k.GetProperty("answers").GetString()!);
            foreach (char key in k.GetProperty("keys").GetString()!)
                if (ed.Key(key == '0' ? '\0' : key))
                {
                    if (answers.Dequeue() == 'Y')
                        break;
                    ed.Restart();
                }
            var got = Convert.ToHexString(c.Record, IconEditor.Head, 10).ToLowerInvariant();
            if (got != k.GetProperty("after").GetString())
                fails.Add($"case {n}: {got} != {k.GetProperty("after").GetString()} (keys {k.GetProperty("keys").GetString()})");
        }
        Assert.True(n > 0);
        Assert.True(fails.Count == 0, $"{fails.Count} of {n} differ:\n" + string.Join("\n", fails.Take(10)));
    }

    [Fact]
    public void Keep_commits_and_exit_reverts()
    {
        var c = new EclCharacter();
        c[IconEditor.Head] = 13;
        c[IconEditor.Colours] = 0x9F;
        var ed = new IconEditor(c);
        ed.Key('P');
        ed.Key('H');
        ed.Key('N'); // 13 → 0
        Assert.Equal(0, c[IconEditor.Head]);
        ed.Key('E'); // reverts
        Assert.Equal(13, c[IconEditor.Head]);
        ed.Key('H');
        ed.Key('P');
        ed.Key('K'); // kept 12
        ed.Key('E'); // back to the main menu
        Assert.Equal(IconEditor.Menu.Main, ed.State);
        ed.Key('2');
        ed.Key('B');
        ed.Key('N'); // high nibble 9 → 10
        Assert.Equal(0xAF, c[IconEditor.Colours]);
        ed.Key('K');
        ed.Key('E');
        Assert.True(ed.Key('E'));
        Assert.Equal(12, c[IconEditor.Head]);
        Assert.Equal(0xAF, c[IconEditor.Colours]);
    }
}
