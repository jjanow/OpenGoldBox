namespace GoldBox.Core.Explore;

/// <summary>
/// DOS's line inputs as the program runs them on the keys it reads (ecl-format.md "The ECL line inputs";
/// LineInputProgramTests compares them with the program under emulation):
/// <list type="bullet">
/// <item>START 0x7A73 (INPUTSTR, names): each key 0x20–0x7A is added while the line is shorter than the maximum,
/// Backspace (8) takes the last off, Return (13) or Escape (27) ends the line, which is then upper-cased (a–z only) and
/// cut to 40 characters (the result is a string[40]). Escape keeps what was typed. Every other key does nothing.</item>
/// <item>START 0x7BF6 (INPUTNUM): 0x7A73 with 6 characters, then Turbo Pascal's Val into a longint; a line Val
/// rejects, or a value below 0 or above 65536, is asked again (an empty line too); the answer is the value's low word
/// (65536 gives 0).</item>
/// </list>
/// </summary>
public static class DosLineInput
{
    /// <summary>The characters 0x7A73 types (0x7AC8–0x7ACE).</summary>
    public static bool Types(char c) => c is >= ' ' and <= 'z';

    /// <summary>The key ends the line (0x7B7C–0x7B88).</summary>
    public static bool Ends(int key) => key is 13 or 27;

    /// <summary>
    /// 0x7A73 on <paramref name="keys"/> from <paramref name="start"/>: the line (upper-cased, at most 40) and the index
    /// after the key that ended it; null when the keys run out first.
    /// </summary>
    public static (string Text, int Next)? Line(IReadOnlyList<int> keys, int start, int max)
    {
        var s = new System.Text.StringBuilder();
        for (int i = start; i < keys.Count; i++)
        {
            int k = keys[i];
            if (k is >= 0x20 and <= 0x7A)
            {
                if (s.Length < max)
                    s.Append((char)k);
            }
            else if (k == 8 && s.Length > 0)
                s.Length--;
            if (Ends(k))
                return (Finish(s.ToString()), i + 1);
        }
        return null;
    }

    /// <summary>The line as 0x7A73 hands it back: a–z upper-cased (0x7BA5–0x7BDA), then cut to the string[40].</summary>
    public static string Finish(string typed)
    {
        var u = new string([.. typed.Select(c => c is >= 'a' and <= 'z' ? (char)(c - 32) : c)]);
        return u.Length > 40 ? u[..40] : u;
    }

    /// <summary>INPUTNUM's answer to one line (0x7C34–0x7C76): the low word, or null when the program asks again.</summary>
    public static ushort? Number(string line)
    {
        if (Val(line) is not { } v || v < 0 || v > 65536)
            return null;
        return (ushort)v;
    }

    /// <summary>
    /// Turbo Pascal's Val into a longint (System unit, called at 0x7C3E): an optional sign, then decimal digits or '$' and
    /// hex digits, nothing else; null when Val reports an error.
    /// </summary>
    public static long? Val(string s)
    {
        int i = 0;
        while (i < s.Length && s[i] == ' ')
            i++;
        bool neg = false;
        if (i < s.Length && s[i] is '+' or '-')
        {
            neg = s[i] == '-';
            i++;
        }
        bool hex = false;
        if (i < s.Length && s[i] == '$')
        {
            hex = true;
            i++;
        }
        int digits = 0;
        long v = 0;
        for (; i < s.Length; i++)
        {
            int d = s[i] switch
            {
                >= '0' and <= '9' => s[i] - '0',
                >= 'A' and <= 'F' when hex => s[i] - 'A' + 10,
                >= 'a' and <= 'f' when hex => s[i] - 'a' + 10,
                _ => -1,
            };
            if (d < 0)
                return null;
            v = v * (hex ? 16 : 10) + d;
            digits++;
        }
        if (digits == 0)
            return null;
        return neg ? -v : v;
    }
}
