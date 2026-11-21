using GoldBox.Core.Ecl;

namespace GoldBox.Client.Play;

/// <summary>
/// START's member pick 0x37E8 (00BA:2C48): the camp Cast's target pick (0x27AA9, prompt "cast on whom") and View's
/// Trade (0x2276E and 0x23190, their own prompts; docs/re/camp.md "Trade"). Each pass draws the party list with the
/// pick lit (00BA:2CA1) and runs the menu with the prompt and Select / Exit (00BA:2C41, 2C19): End the next member, Home
/// the one before (both wrap, 00BA:2D29 / 2D57), Escape (the menu's 0) clears the pick (00BA:2DD6) and the loop goes
/// on with nobody lit; Return and S end with the pick, E with nobody. With no pick, Home walks to the last member
/// (00BA:2CBE); End reads the next pointer of a nil record: nobody (a Select then would act on that address `[?]`; the
/// Client keeps no pick, so Select ends there like Exit). A click on a name picks it (ours).
/// </summary>
public static class MemberPick
{
    /// <summary>The pick's answer to Escape (the program's menu answers 0).</summary>
    internal const int Escaped = -10;

    /// <summary>
    /// The pick on <paramref name="t"/>'s party, starting on <paramref name="start"/>; <paramref name="menu"/> is the
    /// program's item line (Select Exit) or null for ours. Returns the member picked, or null.
    /// </summary>
    public static EclCharacter? Run(TradeContext t, string prompt, EclCharacter? start, string? menu)
    {
        var party = t.Party;
        var (items, keys) = CampScreens.ParseMenu(menu ?? "Select Exit");
        var pick = start;
        while (true)
        {
            t.RosterDrawnLit(pick);
            int a = t.Dialog.Ask(new ChoiceRequest(null, items, false, Escaped)
            {
                MenuPrompt = prompt.TrimEnd(),
                PartySelect = true,
                Hotkeys = keys,
                // Return gives 13, which ends the loop with the pick whatever word is lit (DOSBox-X s42: Exit lit from
                // the shared cell, Return traded); the pad's A is Return. A click on a word or its hotkey is that word.
                ConfirmAnswer = 0,
            }) is int x ? x : items.Count - 1;
            int n = party.Members.Count;
            if (a == Escaped)
                pick = null; // 00BA:2DD6, and the loop goes on
            else if (a == ChoiceRequest.NextMember)
                pick = n == 0 || pick is null ? null : party.Members[(party.Members.IndexOf(pick) + 1) % n];
            else if (a == ChoiceRequest.PreviousMember)
                pick = n == 0 ? null : party.Members[pick is null ? n - 1 : (party.Members.IndexOf(pick) + n - 1) % n];
            else if (a <= TradeContext.MemberPick && TradeContext.MemberPick - a < n)
                pick = party.Members[TradeContext.MemberPick - a]; // a click on a name (ours)
            else
                return a == 0 ? pick : null;
        }
    }
}
