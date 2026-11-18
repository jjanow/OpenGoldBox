using GoldBox.Core.Ecl.Profiles;

namespace GoldBox.Core.Ecl;

/// <summary>Where an opcode's tail of extra operands comes from (docs/re/ecl-format.md "Instructions and control flow").</summary>
public enum EclVariadic
{
    None,
    /// <summary>VMENU (0x15): count = 3rd fixed operand; the extra operands are menu texts.</summary>
    Menu,
    /// <summary>ON GOTO / ON GOSUB (0x25/0x26): count = 2nd fixed operand; the extra operands are jump targets.</summary>
    On,
    /// <summary>HMENU (0x2B): count = 2nd fixed operand; the extra operands are menu texts.</summary>
    HMenu,
}

/// <summary>Where the opcode's effect is taken from: the program (P) or Lee's FAQ only (L).</summary>
public enum EclEffectSource
{
    /// <summary>Checked in the game program (column "From" = P).</summary>
    Program,
    /// <summary>Only from Lee 12.3.3 (column "From" = L): every use in code carries a <c>// [?]</c> marker.</summary>
    Lee,
}

/// <summary>One row of the opcode table in docs/re/ecl-format.md.</summary>
/// <param name="Op">Opcode byte.</param>
/// <param name="Mnemonic">Our short name.</param>
/// <param name="Operands">Names of the fixed operands (count = column "Ops").</param>
/// <param name="Variadic">Variadic tail, if any.</param>
/// <param name="SkipCount">Operands the IF-skip consumes (column "Skip").</param>
/// <param name="Source">Effect source.</param>
public sealed record EclOpcodeInfo(byte Op, string Mnemonic, string[] Operands, EclVariadic Variadic, int SkipCount, EclEffectSource Source)
{
    public int FixedCount => Operands.Length;

    /// <summary>Index (0-based) of the fixed operand that holds the variadic count, or -1.</summary>
    public int CountOperandIndex => Variadic switch
    {
        EclVariadic.Menu => 2,
        EclVariadic.On or EclVariadic.HMenu => 1,
        _ => -1,
    };

    /// <summary>Role of operand <paramref name="index"/> (0-based), including the variadic tail.</summary>
    public string Role(int index) => index < Operands.Length
        ? Operands[index]
        : Variadic switch
        {
            EclVariadic.On => "target",
            EclVariadic.Menu or EclVariadic.HMenu => "choice",
            _ => "?",
        };
}

/// <summary>
/// The PoR v1.3 opcode table (docs/re/ecl-format.md "Opcode table"). Opcodes 0x00–0x3D except 0x1F have a handler;
/// 0x1F and ≥ 0x3E have none (the IF-skip still knows 0x1F, with 2 operands).
/// </summary>
public static class EclOpcodes
{
    public const byte Exit = 0x00, Goto = 0x01, Gosub = 0x02, Compare = 0x03, Add = 0x04, Sub = 0x05, Div = 0x06,
        Mul = 0x07, Random = 0x08, Save = 0x09, LoadChar = 0x0A, LoadMon = 0x0B, SetupMon = 0x0C, Approach = 0x0D,
        Picture = 0x0E, InputNum = 0x0F, InputStr = 0x10, Print = 0x11, PrintClr = 0x12, Return = 0x13, CmpAnd = 0x14,
        VMenu = 0x15, IfEq = 0x16, IfNe = 0x17, IfLt = 0x18, IfGt = 0x19, IfLe = 0x1A, IfGe = 0x1B, ClrMon = 0x1C,
        PartyStr = 0x1D, ChkParty = 0x1E, Undefined1F = 0x1F, NewEcl = 0x20, LoadFiles = 0x21, PSurprise = 0x22,
        Surprise = 0x23, Combat = 0x24, OnGoto = 0x25, OnGosub = 0x26, Treasure = 0x27, Rob = 0x28, EncMenu = 0x29,
        GetTable = 0x2A, HMenu = 0x2B, Parley = 0x2C, Call = 0x2D, Damage = 0x2E, And = 0x2F, Or = 0x30,
        SpriteOff = 0x31, FindItem = 0x32, PrintRet = 0x33, Clock = 0x34, SaveTable = 0x35, AddNpc = 0x36,
        LoadPieces = 0x37, Program = 0x38, Who = 0x39, Delay = 0x3A, Spell = 0x3B, Protect = 0x3C, ClearBox = 0x3D;

    /// <summary>
    /// The table as profile data (<see cref="EclProfiles.Por13"/>): the rows below, and the IF-skip counts of GAME.OVR
    /// 0x9379. These differ from the handler's count for 0C, 36 (one short) and 15, 25, 26, 2B (0); 0x1F = 2; opcodes
    /// ≥ 0x3E = 0.
    /// </summary>
    public static EclOpcodeTable Por13 { get; } = BuildTable();

    /// <summary>Handler info, or null for an opcode with no handler (0x1F, ≥ 0x3E).</summary>
    public static EclOpcodeInfo? Get(byte op) => Por13.Get(op);

    public static bool IsHandled(byte op) => Por13.IsHandled(op);

    public static bool IsIf(byte op) => op is >= IfEq and <= IfGe;

    /// <summary>Mnemonic, also for opcodes with no handler.</summary>
    public static string Name(byte op) => Por13.Name(op);

    /// <summary>Operand count used by the IF-skip routine (GAME.OVR 0x9379); see <see cref="Por13"/>.</summary>
    public static int SkipCount(byte op) => Por13.SkipCount(op);

    private static EclOpcodeTable BuildTable()
    {
        var rows = Build();
        var skip = new int[256];
        for (int op = 0; op < 256; op++)
            skip[op] = op == Undefined1F ? 2 : rows[op]?.SkipCount ?? 0;
        return new EclOpcodeTable(rows, skip, new Dictionary<byte, string> { [Undefined1F] = "UNDEF1F" });
    }

    private static EclOpcodeInfo?[] Build()
    {
        var t = new EclOpcodeInfo?[256];
        const EclEffectSource P = EclEffectSource.Program, L = EclEffectSource.Lee;

        void Row(byte op, string name, EclEffectSource src, int skip, params string[] ops) =>
            t[op] = new EclOpcodeInfo(op, name, ops, EclVariadic.None, skip, src);
        void RowVar(byte op, string name, EclEffectSource src, EclVariadic v, params string[] ops) =>
            t[op] = new EclOpcodeInfo(op, name, ops, v, 0, src);

        // Skip = number of fixed operands, except where noted (bold rows of the spec table).
        Row(Exit, "EXIT", P, 0);
        Row(Goto, "GOTO", P, 1, "target");
        Row(Gosub, "GOSUB", P, 1, "target");
        Row(Compare, "COMPARE", P, 2, "a", "b");
        Row(Add, "ADD", P, 3, "a", "b", "dst");
        Row(Sub, "SUB", P, 3, "a", "b", "dst");
        Row(Div, "DIV", P, 3, "a", "b", "dst");
        Row(Mul, "MUL", P, 3, "a", "b", "dst");
        Row(Random, "RANDOM", P, 2, "max", "dst");
        Row(Save, "SAVE", P, 2, "src", "dst");
        Row(LoadChar, "LOADCHAR", L, 1, "index");
        Row(LoadMon, "LOADMON", L, 3, "monster", "count", "picture");
        Row(SetupMon, "SETUPMON", L, 2, "monster", "distance", "picture"); // skip 2 of 3
        Row(Approach, "APPROACH", L, 0);
        Row(Picture, "PICTURE", L, 1, "picture");
        Row(InputNum, "INPUTNUM", P, 2, "digits", "dst");
        Row(InputStr, "INPUTSTR", P, 2, "length", "dst");
        Row(Print, "PRINT", L, 1, "text");
        Row(PrintClr, "PRINTCLR", L, 1, "text");
        Row(Return, "RETURN", P, 0);
        Row(CmpAnd, "CMPAND", P, 4, "a1", "b1", "a2", "b2");
        RowVar(VMenu, "VMENU", P, EclVariadic.Menu, "dst", "prompt", "count");
        Row(IfEq, "IFEQ", P, 0);
        Row(IfNe, "IFNE", P, 0);
        Row(IfLt, "IFLT", P, 0);
        Row(IfGt, "IFGT", P, 0);
        Row(IfLe, "IFLE", P, 0);
        Row(IfGe, "IFGE", P, 0);
        Row(ClrMon, "CLRMON", L, 0);
        Row(PartyStr, "PARTYSTR", P, 1, "dst");
        Row(ChkParty, "CHKPARTY", L, 6, "field", "effect", "dst_min", "dst_max", "dst_avg", "dst_any");
        Row(NewEcl, "NEWECL", P, 1, "script");
        Row(LoadFiles, "LOADFILES", P, 3, "map", "unused", "picture");
        Row(PSurprise, "PSURPRISE", P, 2, "dst_party", "dst_mon");
        Row(Surprise, "SURPRISE", P, 4, "party1", "party2", "mon1", "mon2");
        Row(Combat, "COMBAT", L, 0);
        RowVar(OnGoto, "ONGOTO", P, EclVariadic.On, "select", "count");
        RowVar(OnGosub, "ONGOSUB", P, EclVariadic.On, "select", "count");
        Row(Treasure, "TREASURE", L, 8, "cp", "sp", "ep", "gp", "pp", "gems", "jewelry", "items");
        Row(Rob, "ROB", L, 3, "whole_party", "money_pct", "item_chance");
        Row(EncMenu, "ENCMENU", L, 14, "monster", "distance", "picture", "dst", "r_combat", "r_wait", "r_flee",
            "r_advance", "r_parley", "msg0", "msg1", "msg2", "move1", "move2");
        Row(GetTable, "GETTABLE", P, 3, "table", "index", "dst");
        RowVar(HMenu, "HMENU", P, EclVariadic.HMenu, "dst", "count");
        Row(Parley, "PARLEY", L, 6, "haughty", "sly", "nice", "meek", "abusive", "dst");
        Row(Call, "CALL", P, 1, "routine");
        Row(Damage, "DAMAGE", L, 5, "attack", "dice", "sides", "bonus", "extra");
        Row(And, "AND", P, 3, "a", "b", "dst");
        Row(Or, "OR", P, 3, "a", "b", "dst");
        Row(SpriteOff, "SPRITEOFF", L, 0);
        Row(FindItem, "FINDITEM", P, 1, "item");
        Row(PrintRet, "PRINTRET", L, 0);
        Row(Clock, "CLOCK", P, 1, "minutes");
        Row(SaveTable, "SAVETABLE", P, 3, "src", "table", "index");
        Row(AddNpc, "ADDNPC", L, 1, "monster", "morale"); // skip 1 of 2
        Row(LoadPieces, "LOADPIECES", P, 3, "walldef1", "walldef2", "walldef3");
        Row(Program, "PROGRAM", P, 1, "program");
        Row(Who, "WHO", L, 1, "prompt");
        Row(Delay, "DELAY", P, 0);
        Row(Spell, "SPELL", L, 3, "spell", "dst_slot", "dst_char");
        Row(Protect, "PROTECT", L, 1, "src");
        Row(ClearBox, "CLEARBOX", L, 0);
        return t;
    }
}
