using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// The party member selection GAME.OVR 0x23819 (key byte), read whole s49 (docs/re/shop-format.md "Party selection").
/// It walks the party list (head DS:5D96, links at record +0x104) and writes the current member DS:5D92.
/// </summary>
public static class PartySelection
{
    /// <summary>The keys the program tests: the scan codes of Home and End, which are also the letters G and O.</summary>
    public const byte Home = 0x47, End = 0x4F;

    /// <summary>
    /// 0x23819. An empty list (head nil) makes the current member nil (0x23834). Home (0x47): when the current member
    /// is the head, the last member; otherwise the member whose link is the current one, walked from the head; with no
    /// current member (nil) that is the last member (its link is nil); a current member not in the list ends the walk
    /// on nil, so none is current. End (0x4F): the current member's next, or the head when it is the last. Any other
    /// key: the head (the camp, Magic, Alter, Order, the HMENU helper and the command line pass every extended key).
    /// [?] End with no current member reads the link at 0000:0104 (the interrupt table): not modelled, nothing changes.
    /// </summary>
    public static void Select(EclParty party, byte key)
    {
        var m = party.Members;
        if (m.Count == 0)
        {
            party.CurrentIndex = -1;
            return;
        }
        int cur = party.CurrentIndex >= 0 && party.CurrentIndex < m.Count ? party.CurrentIndex : -1;
        switch (key)
        {
            case Home:
                party.CurrentIndex = cur == 0 || cur < 0 ? m.Count - 1 : cur - 1;
                break;
            case End:
                if (cur >= 0)
                    party.CurrentIndex = cur + 1 < m.Count ? cur + 1 : 0;
                break;
            default:
                party.CurrentIndex = 0;
                break;
        }
    }
}
