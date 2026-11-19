using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Ecl.Profiles;

namespace GoldBox.Core.Tests;

/// <summary>
/// The SotSB $7C00 character view (docs/re/survey-sotsb.md §10.2) against the program itself: the private fixture
/// <c>sotsb/program-v1.30/char_view.json</c> (<c>tools/scripts/sotsb_view_oracle.py</c>) holds random 439-byte party
/// records with every handled read of the reader OVR 0x9830 and the effects of the writer OVR 0x9D20 for offsets
/// 0 … 0x12F and the disk / wall-set cells, run under emulation.
/// </summary>
public class SotsbViewProgramTests
{
    private static readonly string? File_ = Find();

    private static string? Find()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "tests", "fixtures-private", "sotsb", "program-v1.30", "char_view.json");
            if (File.Exists(p))
                return p;
        }
        return null;
    }

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (File_ is null)
                Skip = "needs the private fixture sotsb/program-v1.30/char_view.json";
        }
    }

    private static IEnumerable<JsonElement> Trials() =>
        JsonDocument.Parse(File.ReadAllText(File_!)).RootElement.GetProperty("trials").EnumerateArray();

    private static (EclMemory Memory, EclParty Party) Setup(JsonElement t)
    {
        var party = new EclParty();
        foreach (var r in t.GetProperty("recs").EnumerateArray())
            party.Members.Add(new EclCharacter(Convert.FromHexString(r.GetString()!), EclCharacter.SotsbRecordSize));
        party.CurrentIndex = t.GetProperty("pos").GetInt32();
        var m = new EclMemory(profile: EclProfiles.Sotsb13)
        {
            Party = party,
            MemberMissing = t.GetProperty("missing").GetInt32() != 0,
            Disk = (byte)t.GetProperty("disk").GetInt32(),
        };
        m.Write(0x7F3E, 0x1234);
        return (m, party);
    }

    [OracleFact]
    public void Every_handled_read_matches_the_program()
    {
        int compared = 0;
        var bad = new List<string>();
        foreach (var t in Trials())
        {
            var (m, party) = Setup(t);
            var cur = party.Current!;
            foreach (var row in t.GetProperty("reads").EnumerateArray())
            {
                int k = row[0].GetInt32();
                int want = row[1].GetInt32();
                // Cases that set no value return the emulated stack: $7D0D, and $7ECF outside its table.
                if (k == 0x10D || (k == 0x2CF && cur[0x1B] is < 3 or > 25))
                    continue;
                int got = m.Read((ushort)(0x7C00 + k));
                compared++;
                if (got != want)
                    bad.Add($"k {k:X3}: program {want:X4}, VM {got:X4}");
            }
        }
        Assert.True(compared > 400, $"only {compared} reads compared");
        Assert.Empty(bad);
    }

    [OracleFact]
    public void Every_unhandled_read_is_the_cell()
    {
        foreach (var t in Trials().Take(2))
        {
            var (m, _) = Setup(t);
            var handled = t.GetProperty("reads").EnumerateArray().Select(r => r[0].GetInt32()).ToHashSet();
            for (int k = 0; k < 0x400; k++)
            {
                if (handled.Contains(k) || k == 0x33E)
                    continue;
                var a = (ushort)(0x7C00 + k);
                m.Write(a, (ushort)(0x4000 + k));
                Assert.Equal(0x4000 + k, m.Read(a));
            }
        }
    }

    [OracleFact]
    public void Every_write_matches_the_program()
    {
        int compared = 0;
        var bad = new List<string>();
        foreach (var t in Trials())
        {
            foreach (var w in t.GetProperty("writes").EnumerateArray())
            {
                var (m, party) = Setup(t);
                var before = (byte[])party.Current!.Record.Clone();
                m.PreviousDisk = 0;
                var walls = new List<(int Set, int Slot)>();
                m.WallSetRequested += (slot, set) => walls.Add((set, slot));
                int k = w[0].GetInt32(), v = w[1].GetInt32();
                m.Write((ushort)(0x7C00 + k), (ushort)v);
                var after = party.Current!.Record;
                var diff = Enumerable.Range(0, after.Length).Where(i => after[i] != before[i])
                    .Select(i => $"{i:X3}={after[i]:X2}");
                // The oracle's records carry the list's next pointers at 0x19D–0x1A0 (its setup); they are no field.
                var want = w[2].EnumerateArray().Where(d => d[0].GetInt32() is < 0x19D or > 0x1A0)
                    .Select(d => $"{d[0].GetInt32():X3}={d[1].GetInt32():X2}");
                string got = $"{string.Join(",", diff)} disk {m.Disk}/{m.PreviousDisk} a4b7 {(m.Ds84D3 ? 1 : 0)} walls {string.Join(";", walls)}";
                string exp = $"{string.Join(",", want)} disk {w[3].GetInt32()}/{w[4].GetInt32()} a4b7 {w[5].GetInt32()} walls " +
                             string.Join(";", w[6].EnumerateArray().Select(x => (x[0].GetInt32(), x[1].GetInt32())));
                // The program leaves DS:7421 alone unless k = 0x312; the VM's PreviousDisk starts at 0 like the fixture.
                compared++;
                if (got != exp)
                    bad.Add($"k {k:X3} v {v:X4}: program [{exp}], VM [{got}]");
            }
        }
        Assert.True(compared > 10000, $"only {compared} writes compared");
        Assert.Empty(bad.Take(20));
    }
}
