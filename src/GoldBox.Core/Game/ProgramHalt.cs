namespace GoldBox.Core.Game;

/// <summary>
/// A path on which the original program stops with a Turbo Pascal run-time error (it leaves to DOS through the exit
/// chain, START.EXE 0xB062). Parity: the rule raises this instead of inventing an outcome; the caller decides what a
/// remake does there. <see cref="Code"/> is the run-time error number (200 = division by zero).
/// </summary>
public sealed class ProgramHaltException(int code, string message) : Exception(message)
{
    /// <summary>Turbo Pascal run-time error number (200: division by zero, from the long division START 0xBA5D).</summary>
    public int Code { get; } = code;
}
