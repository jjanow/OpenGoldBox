using System.Globalization;
using System.Text;
using System.Text.Json;

namespace GoldBox.Core.Ecl;

/// <summary>An operand as loaded into the slots (raw; hi is null when the type carries one byte).</summary>
public sealed record EclTraceOperand(byte Type, byte Lo, byte? Hi, string? Text);

/// <summary>A VM read made through the value fetch.</summary>
public readonly record struct EclTraceRead(ushort Address, ushort Value);

/// <summary>A host call: name, arguments and result, all as stable text.</summary>
public sealed record EclTraceHostCall(string Name, IReadOnlyList<string> Args, string? Result);

/// <summary>One executed instruction.</summary>
public sealed class EclTraceStep
{
    public long Seq { get; set; }
    public int Script { get; set; }
    public int Disk { get; set; }
    public int Vector { get; set; }
    public ushort Pc { get; set; }
    public byte Op { get; set; }
    public List<EclTraceOperand> Operands { get; } = [];
    public List<EclTraceRead> Reads { get; } = [];
    public List<EclMemoryWrite> Writes { get; } = [];
    public List<EclTraceHostCall> HostCalls { get; } = [];
    public List<string> Diagnostics { get; } = [];
    /// <summary>The IF outcome: true taken, false skipped, null not an IF.</summary>
    public bool? IfTaken { get; set; }
    /// <summary>
    /// Flags before the step, in the order = ≠ &lt; &gt; ≤ ≥: what a DOSBox breakpoint on the dispatcher (GAME.OVR
    /// 0x3C71) sees at DS:6F78..6F7D for this step.
    /// </summary>
    public bool[] FlagsIn { get; set; } = new bool[6];
    /// <summary>Flags after the step, same order (= the next step's <see cref="FlagsIn"/>).</summary>
    public bool[] Flags { get; set; } = new bool[6];
    public int StackDepth { get; set; }
    public ushort NextPc { get; set; }
    /// <summary>Set when the step ended the run (exit, newecl, fault, unhandled, stop).</summary>
    public string? End { get; set; }
}

/// <summary>Receives trace steps and VM events outside a step.</summary>
public interface IEclTraceSink
{
    void Step(EclTraceStep step);
    /// <summary>An event outside a step: script load, run start/end.</summary>
    void Event(string kind, IReadOnlyDictionary<string, string> fields);
}

/// <summary>
/// Writes the trace as JSON Lines: one object per step or event, fixed key order, invariant culture, addresses as
/// 4-digit upper-case hex strings, values as decimal numbers, no timestamps (so two runs diff cleanly).
/// <para>Step keys: <c>k</c>="step", <c>seq</c>, <c>script</c>, <c>disk</c>, <c>vec</c>, <c>pc</c>, <c>op</c>
/// (hex), <c>mn</c>, <c>ops</c> [{t, lo, hi?, w? (hi:lo as hex), s?}], <c>rd</c> [{a, v}], <c>wr</c> [{a, r, old, new}],
/// <c>host</c> [{n, args, ret?}], <c>if</c>?, <c>fin</c> (flags before the step, as DS:6F78..6F7D at the
/// dispatcher), <c>flags</c> (after the step; both "=≠&lt;&gt;≤≥" as six 0/1 digits), <c>sp</c>,
/// <c>next</c>, <c>diag</c>? [text], <c>end</c>?.</para>
/// <para>Event keys: <c>k</c> = event kind, then the event's fields in insertion order.</para>
/// </summary>
public sealed class JsonlTraceWriter(TextWriter output) : IEclTraceSink
{
    private static readonly JsonWriterOptions Options = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    public static string Hex(ushort a) => a.ToString("X4", CultureInfo.InvariantCulture);

    public void Step(EclTraceStep s)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, Options))
        {
            w.WriteStartObject();
            w.WriteString("k", "step");
            w.WriteNumber("seq", s.Seq);
            w.WriteNumber("script", s.Script);
            w.WriteNumber("disk", s.Disk);
            w.WriteNumber("vec", s.Vector);
            w.WriteString("pc", Hex(s.Pc));
            w.WriteString("op", s.Op.ToString("X2", CultureInfo.InvariantCulture));
            w.WriteString("mn", EclOpcodes.Name(s.Op));
            w.WriteStartArray("ops");
            foreach (var o in s.Operands)
            {
                w.WriteStartObject();
                w.WriteNumber("t", o.Type);
                w.WriteNumber("lo", o.Lo);
                if (o.Hi is { } hi)
                {
                    w.WriteNumber("hi", hi);
                    w.WriteString("w", Hex((ushort)(o.Lo | (hi << 8))));
                }
                if (o.Text is not null)
                    w.WriteString("s", o.Text);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("rd");
            foreach (var r in s.Reads)
            {
                w.WriteStartObject();
                w.WriteString("a", Hex(r.Address));
                w.WriteNumber("v", r.Value);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("wr");
            foreach (var x in s.Writes)
            {
                w.WriteStartObject();
                w.WriteString("a", Hex(x.Address));
                w.WriteString("r", x.Region);
                w.WriteNumber("old", x.Old);
                w.WriteNumber("new", x.New);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("host");
            foreach (var h in s.HostCalls)
            {
                w.WriteStartObject();
                w.WriteString("n", h.Name);
                w.WriteStartArray("args");
                foreach (var a in h.Args)
                    w.WriteStringValue(a);
                w.WriteEndArray();
                if (h.Result is not null)
                    w.WriteString("ret", h.Result);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            if (s.IfTaken is { } taken)
                w.WriteBoolean("if", taken);
            w.WriteString("fin", FlagString(s.FlagsIn));
            w.WriteString("flags", FlagString(s.Flags));
            w.WriteNumber("sp", s.StackDepth);
            w.WriteString("next", Hex(s.NextPc));
            if (s.Diagnostics.Count > 0)
            {
                w.WriteStartArray("diag");
                foreach (var d in s.Diagnostics)
                    w.WriteStringValue(d);
                w.WriteEndArray();
            }
            if (s.End is not null)
                w.WriteString("end", s.End);
            w.WriteEndObject();
        }
        output.WriteLine(Encoding.UTF8.GetString(ms.ToArray()));
    }

    public void Event(string kind, IReadOnlyDictionary<string, string> fields)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, Options))
        {
            w.WriteStartObject();
            w.WriteString("k", kind);
            foreach (var (key, value) in fields)
                w.WriteString(key, value);
            w.WriteEndObject();
        }
        output.WriteLine(Encoding.UTF8.GetString(ms.ToArray()));
    }

    public static string FlagString(bool[] f)
    {
        var c = new char[6];
        for (int i = 0; i < 6; i++)
            c[i] = f[i] ? '1' : '0';
        return new string(c);
    }
}

/// <summary>Keeps every step in memory (tests).</summary>
public sealed class ListTraceSink : IEclTraceSink
{
    public List<EclTraceStep> Steps { get; } = [];
    public List<(string Kind, IReadOnlyDictionary<string, string> Fields)> Events { get; } = [];
    public void Step(EclTraceStep step) => Steps.Add(step);
    public void Event(string kind, IReadOnlyDictionary<string, string> fields) => Events.Add((kind, fields));
}
