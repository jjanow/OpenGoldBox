# The camp: rest, magic, save and alter (Pool of Radiance, DOS v1.3)

What Encamp runs after vector 2 (`ecl-format.md` "Vector roles"; the camp routine 0x3B19 runs vector 3 when the rest
was interrupted): the camp menu GAME.OVR 0x196A0 and everything under it. Read from the program in session 13
(2026-11-17); addresses are GAME.OVR file offsets unless marked START (offsets in the unpacked `START.EXE` image); far
calls go through the overlay stubs (`ecl-format.md` "Program references"). The camp unit starts at 0x17831 (its code
segment: `cs:0x1E23` = 0x19654), the time unit at 0x23DCC.

Status marks: **P** read in the program, **D** checked against DOSBox-X, `[?]` not settled. talonius' source was used
only to find routines; every rule below is from the disassembly (objdump, 8086).

Code: `src/GoldBox.Core/Game/CampTime.cs` (`CampState`, rest time, clock + effect timeout), `CampRest.cs` (rest loop,
rest-time menu), `CampSpells.cs` (checks, preparation time, memorise, scribe, lists, Display), `CampParty.cs` (order,
drop, speed, pictures), `CampCast.cs` (casting outside a fight); `src/GoldBox.Data/Por/PorSaveGame.cs` /
`PorParty.cs` (`Build`, `Save`). Client: `src/GoldBox.Client/Play/CampScreens.cs`. Tests: `CampTests`,
`CampRestCaptureTests`, `SaveWriteTests` (Core), `CampScreenTests`, `CampPlayTests` (Client).

**DOSBox (capture `rest_interrupted`, s9, replayed s13)**: save A's party in camp at 10:50 with $6DD2 = $6DD3 = 24
(vector 2), then R, H, I×8, R. The rest stopped after 48 ticks (clock 14:50, two 1d100 draws), vector 3 ran.
`CampRestCaptureTests` replays the rest through `CampRest` on the game's generator and then vector 3: RandSeed and the
$4900 and $6B00 areas match. **D** for the rest loop's tick, the interval counter, the roll and the clock.

## Data-segment variables (not VM cells)

The camp keeps its state in the program's data segment. Several addresses overlap VM cells of the same number that
mean something else (VM $6DCA is Search, $6DCB surprise, $49FB no area map), so the Client keeps them in
`CampState`, never in `EclMemory`.

| DS | Size | Meaning |
|---|---|---|
| 49F3 | byte | interface state: 2 while the camp runs (saved and restored by 0x196A0) |
| 49FB | word | rest ticks since the last interruption roll; cleared only by the roll (not on camp entry) |
| 471E | 72 bytes | per creature-list position 1..72: has timed effects left (0x23DCC) |
| 6DB5 | far ptr | remembered cast target; cleared when the Cast menu opens and when the camp ends |
| 6DC2 | 7 words | the rest time, shaped like the game clock (1 minutes, 2 ten minutes, 3 hours, 4 days, 5 months, 6 years); cleared on camp entry and after each rest |
| 6DD0 | 8 bytes | spell preparation timer per party position 1..8 |
| 6DD8 | word | rest ticks since the last whole-party heal; cleared on camp entry |
| 6DDA | byte | set while the rest loop runs (border suppression; display only) |

VM cells read: $6DD2 (rest ticks between interruption rolls; zone $6B00 + 0x5A4) and $6DD3 (the roll's percent,
+0x5A6), $6E3E (party size), $49E5 (anti-magic area, zone $4900 + 0x1CA).

## Camp menu (0x196A0) — P

DS:49F3 := 2 (old value kept), DS:6DD8 := 0, the rest time cleared, the camp message (0x19654) printed at column 1, row 18, the
pending memorising and scribing of the whole party cleared (0x17A45). Then the menu (DS:0508 in v1.3: Save, View,
Magic, Rest, Alter, Exit, with its prompt 0x1968C) until Exit, Escape or an interrupted rest. Extended keys (Home / End) select
the previous / next member (0x23819). Save: the save (below), then **always** the quit question (0x19693), also after
Escape at the slot menu. View: the character view (0x197B3 → 0x21AED, below). Magic: 0x18C22. Rest: 0x17FAB. Alter: 0x1948A. On the way out:
0x17A45 again, DS:6DB5 := nil, DS:49F3 restored, the position line and the text window redrawn. The routine returns
the interrupted flag to 0x3B19.

Menu lines (the menu routine, START): every capital letter or digit starts an item and is its hotkey, so a word with
its capital in the middle has that letter as hotkey and a lower-case word joins the item before it; Escape answers 0. The Client parses the lines the same way and draws the
hotkey letter white. The menu routine itself (0x2CC8C: keys, highlight, Enter, the extended byte) is in `shop-format.md` "The menu routine" (s15).

## View from the camp, shop, temple and loot menu (0x21AED) — P, D (s39)

The camp's V (0x197B3), the shop's (0x730D), the temple's (0x5341) and the loot menu's (0x65B4) are all the far call
0085:0039, the overlay stub of 0x21AED: the same page (0x21084) and View menu as the party menu's
(`start-menu.md` §4 View, with its keys: ',' / '.' move the lit word, Return, space, Home, End and the arrows do
nothing, I S T D E and Escape act). 0x21AED and the Items page 0x21F32 call no member selection (no 0x23819 in unit
0x2103A): Home / End pick members only on the venue's own menu. The page's portrait window follows 0x21530 (portraits
on and disk byte 3), so in the city (save A, slots F / G / E of `por_test_saves.py`) there is none. The cell DS:6B2B
is shared: the venue menu's V hotkey moves it to View's place (2nd in the shop, temple and camp, 1st in the loot
menu), so View opens on that word (Trade, or Spells for a caster, in the shop; Items in the loot menu); View's Exit
(4th) leaves the venue menu on its 4th entry (Appraise, Share, Rest), Escape on the word last lit. On exit (0x21D4F)
the text box frame is redrawn (START 0x27D9) and the venue menu comes back; the camp's and the loot menu's show one
transient frame of the menu written over View's line.
**D** (s39, 3000 cycles; the same keys on both sides: V, '.', Return, Home, End, '.', E, V, ',', Escape, End, V,
Escape): shop (slot F) 6 of 6 View frames equal, temple (G) 6 of 6, loot (E) 6 of 6, camp (save A, Encamp) 6 of 6;
every other Client frame is the venue's screen differing only by the Client's help lines (and the camp fire's and the
loot picture's phase). Client: `TradeContext.ViewPage` (`ClientPlayer.ViewPage` → `PartyMenuFlow.ViewPage`, the game's
memory for the portrait rule), drawn by the explore screen's party-page overlay, which hands back after View
(`SheetRequest.IsView`); our notices for commands not built go on the page's bottom line; one highlight cell for every
screen with `--save` too (`Program.SharedHighlightFor`). Tests `VenueTests.Camp_shop_and_loot_view_use_the_programs_page`.
**From the command line (P + D s41).** The exploring command line's V and C (3D and overland, view3d.md "Area
loop") call the same 0x21AED and the camp Magic menu's Cast 0x17CD2 with DS:49F3 = 4 or 3: View's Items offer Use as
in the camp; View's exit and Cast's redraws (0x17D96, the target pick's 0x27AFA) draw START 0x3379 for that state (the
exploring screen, not the camp's). Client: `ClientPlayer.View` / `Cast`, `Venue.Explore`, `CampContext.InterfaceRedraw`.
**Down on a spell list** (P + D s42): 0x2D669 acts on the extended keys G / O (Home / End, 0x2D8C6 / 0x2D8D4) and I /
Q (PgUp / PgDn) only; the arrows (0x48 / 0x50, also the keypad's 8 / 2 through DS:288C) fall through and redraw. DOSBox-X
(save A on the 3D map and a `save-overland` save at 26 8,19 N; End × 3, C on Brother Sean): Down, Up, KP_Down and,
with NumLock, keypad 2 left the lit row; End wrapped it to the first spell. The s41 note had the sides swapped: the
Client moves the row on Up / Down (`ListPage`, as Home / End since s30), the program does not; a Client addition like
the View / Items arrows (user s40). No change.
**Trade (P s41/s42, D s42; built s42).** View starts by setting DS:471A := DS:5D92 (0x21B0B): the member Trade's pick
starts on. *Item* (the Items page's T, 0x22368): 0x21DAA first (readied: the unready message on row 24; a scroll to
scribe from: its question; else nothing), then 0x2276E: START 0x3379 redraws the screen for DS:49F3; START's member pick
0x37E8 with the ask-whom prompt (unit offset 0x1718, keypad flag 1) on DS:471A; nobody picked ends; else DS:471A := the
pick (before the test) and 0x23795 (`ShopRules.CanCarry`: the stat recalculation START 0x1758 on the pick, then 0xC7 >
15, or weight 0x102 + item 0x37 × 0x39 above START 0x1F4B + 1500) refuses with its own overloaded text (offset 0x1729)
through START 0x2233 (row 24, game-speed pause); else START 0x2197 appends a copy (next nil) to the pick's items, START
0x20A9 unlinks and frees the item from DS:5D92 (0xC7 is left to the next recalculation) and START 0x1758 runs on the
pick. To the giver itself the item moves to the end of its list (the carry test counts it twice). The Items loop then
sets its full-redraw flag (0x2238A, whatever the outcome) and recalculates DS:5D92. *Money* (View's T, 0x23190): from
0x2315D START 0x3379 and the pick with the trade-to prompt (offset 0x211A); nobody picked ends; else the page 0x21084
and its money lines 0x21547 of DS:5D92 (the viewer, not the pick: 0x21084 copies DS:5D92 into DS:4716), DS:471A := the
pick, then from 0x231A0: the money lines, the coin list of DS:5D92 (0x2D669 with Drop's arguments; title, how-many and
will-you-trade texts are this unit's own copies, offsets 0x2126, 0x213B, 0x2145), the amount (0x24F1D with the coin
count as maximum) and the transfer 0x250D5 (kind, amount, the pick, DS:5D92): refused (`Msg.Overloaded`, 0x250CA, row
24) when the pick's stored 0x102 + amount (16-bit) is above its START 0x1F4B + 1500, no recalculation first; else the
giver's coin word goes down and the pick's up (16-bit, no cap). **Weight bug, fixed (user, s42; not an option, as the
boar double free):** 0x250D5 takes the amount off the giver's 0x102 (0x24CA9) and then adds it back to the giver's
(0x24CC3 on [bp+0xE] again): in the program neither weight word moves, so the pick keeps its old weight until a
recalculation and can be handed coins past its limit. The Client moves the amount from the giver's weight to the pick's. Two flags
end the loop (0x23415–0x2346F): after a transfer (a refused one, and an amount of 0, count) the coin list comes again
while the giver holds money; with none Trade ends; leaving the coin list after a transfer goes back to 0x2315D (the
redraw and the pick, on the last pick). Leaving the first coin list before any transfer reads [bp−0x131] unwritten:
DOSBox-X returns to View every time (3 of 3, overland and hall), and so does the Client. View then draws its page again
(T is in the redraw set) on DS:5D92. **The amount (0x24F1D, P s42):** row 24 cleared, the question in the colour passed
(10), then digits only: each digit is appended (6 characters at most) and the value is read; a value above the maximum
replaces the text with the maximum at once; Backspace takes one off; Return gives the value (empty: 0), Escape 0.
**The pick's keys:** the menu shares the cell DS:6B2B (the Items page's T leaves it on the 2nd word, so Exit is lit),
but Return gives 13, which ends the loop with the pick whatever word is lit; E (or Exit) ends with nobody, Escape clears
the pick and stays. **D** (s42, 3000 cycles, `save-overland` 26 at 8,19 N as slot J, Thrender's flail unreadied with R;
the hall of `por_test_saves.py` slot C): T redraws the whole exploring layout (overland view, party list, empty text
box) under the pick, the viewer lit first; End, Return: the giver's Items page back without the item, which is last in
the receiver's list; each START 0x3379 (T, money T, the pick again after a transfer) steps the overland marker once;
the money pick starts on the last target; the coin page is the giver's; typing 9, 9 shows 9, then 36 (all held); after
a transfer the page's encumbrance is not redrawn; View's Exit lights DS:5D92 in the party list. In the hall (DS:49F3 =
0, case 0) the pick blanks everything inside the outer frame (no inner windows, no menu) and draws the party list in the
party menu's own layout with the pick lit; the coin page has its portrait there. Escape at the amount gives 0: nothing
moves, the coin list comes again, and leaving it then goes to the pick. Checked under emulation:
`tools/scripts/por_trade_oracle.py` (0x250D5 and 0x2276E, 300 cases each; `TradeProgramTests`). Client: `TradeRules`
(Core), `CharacterView` (`TradeItem`, `TradeMoney`, `LetGoOnPage`), `MemberPick` (the camp Cast's target pick too),
`TradeContext.InterfaceRedraw` / `TradeTarget` / `PickMenu`, `PartyMenuFlow.PickScreen` (case 0); the Client frames
equal DOSBox-X's but for its help lines (s42).
**What is recalculated (P + D s43).** START 0x1758 (00BA:0BB8) runs in full, never just the weight: on the item
Trade's pick in the carry test (0x237A1) and after the move (0x2280B); on the giver (DS:5D92, [bp−0x3F] from
0x21F39) by the Items loop after *every* command (0x224AF; also Ready, Sell, Id, Drop…), not by 0x2276E; in the money
Trade only by the page 0x21084 → 0x21642 on DS:5D92 after the pick, none in 0x250D5, and the money's pick never; and
by View's page on every full draw (0x21642: entry and after I, S, T; not after D). The Client does the same with
`StatRecalc` (`TradeContext.Recalculate`, the ITEMS table from `ClientResources.ItemTypes`; weight and item count only
without it). Under emulation: `por_trade_oracle.py` also runs START 0x1758 on the giver after 0x2276E, every other giver
starting with stale attack and movement bytes (`TradeProgramTests`, 300 cases): the giver's recalculation is settled
there, not on screen (View's redraw after I recalculates as well). **D** (s43, 3000 cycles, save A with Thrender's
0x102 edited to 7 and his dice to 3d7+9; camp, V, I, R on the flail, T, End, Return, E): View showed 538 and 1D6+2
(the page's own recalculation); R's 0x224AF already made the dice 1D2+1, the Trade took the encumbrance to 388, and
View came back with 388, 1D2+1 and no weapon line. The Client (`--save` of that save with the flail unreadied in the
file; its converted save keeps 0x102 = 7 and 3d7+9) showed 538 and 1D2+1 on entry (View's recalculation, as Ready is
not built), after the Trade the same final page as DOSBox-X. The Amiga View does the same (P s43): its page (hunk 10
+0x48E0) calls the fighting block at +0x4E92, which runs the Amiga's copy of START 0x1758 (hunk 13 +0x4014: the 13
equipment pointers at record +0xCE, item count +0xC9, hands +0x102, weight +0x104; called from seven hunks) on the
current creature first; the View loop (+0x52E8) draws the page on entry (+0x531A) and after I, T, S (+0x5540–0x5554),
not after D (+0x54F6: the money lines +0x4DB6 only) or R. The Client's shared loop is right for both.

**Items and Drop from View** (P + D, s39). Items is 0x21F32 in every venue: its page (0x221DE–0x2229B: braid rows 0, 2,
23; the name with "'S" and the title on row 1, the heading on row 3, the list window (1,5)–(38,22)) and the item list
routine 0x2D669 (called at 0x222C9, keypad flag 1), which stores 1 in the cell on entry (0x2D6B9). Its entries follow
DS:49F3 (`FightItemScreens.ItemsEntries`): the camp (2) has Use, the shop and temple (1) Sell and Id, the party menu,
hall (0) and loot (6) neither. Keys: Home / End and the keypad's 7 / 1 move the row; Down (scan code 0x50 = 'P') and
Return (13) match no entry (R U T D H J S I, 0x222DF–0x22496), so the loop only redraws the page; ',' / '.' move the
lit word; Escape leaves with the cell where it was (DOSBox-X: '.' to Use, Escape → View on its 2nd word, Trade).
Drop (D) is 0x234E1: the coin list over the page (the money lines are its rows, "Select type of coin" in colour 13,
Exit lit; 0x2D669 stores 1 again), any key but Escape / E takes the row, then the amount (input 0x24F1D) on row 24:
the question green, a space, the digits white, no cursor, the coin rows all green; then the coin list again until
Escape. View then comes back on Items (the cell is 1) and only the money lines are redrawn (0x21547): the page keeps
its old encumbrance. **D** (s39, 3000 cycles): camp (save A, Encamp; V, I, Return, End, Home, keypad End, '.',
Escape, D, Escape, D, Return, 1, Return, Escape, E) every page frame equal, 14 of 27 Client frames in all (the misses
are the camp screen's help lines and fire phase and one Client frame between the camp menu and the page); party
menu (`unstarted` slot C, the same keys bar the camp's) 13 of 13; shop (slot F: V, I, '.', End, Escape, D, Escape,
E; Ready Trade Drop Halve Join Sell Id Exit) 7 of 10 (help lines twice, the same transient). Client:
`TradeContext.ItemsPage` / `Coins` (`PartyMenuFlow.ItemsPage` / `Coins`, `CharacterPageRequest`, drawn by
`PartyMenuScreen` with `PageChrome`, the fight's page code; `ItemsPages` builds the Items page for both); Sell and Id
run as before, the other item commands are still our notice; keyboard Up / Down move the row (the Client's, as Left
/ Right move the word; the program ignores both: a deliberate difference, user s40). Tests
`PartyMenuScreenTests.View_drop_goes_through_the_coin_list`, `View_is_the_camp_view_menu_on_the_program_page`.
**Sell and Id over the Items page** (P s41: 0x22472 → 0x21DAA, 0x22D28; 0x22F8C): every text goes through START 0x77F8
in the box (1, 21)–(38, 22), colour 14, straight over the page (no frame), and each routine blanks the box at its
end (03F1:0137). Sell: a readied item gets the unready message on row 24 (START 0x2233, the game-speed pause); a
scroll with a spell to scribe its owner's name at (1, 21) (START 0x23B6), the warning from column name length + 2
and the yes/no; then the offer (cleared first), the deal question (0x2D9A0: No lit); Yes: the sold message on row 24, the item
removed and paid, and the overloaded message on row 24 when platinum went to the pool (0x24CDD). Id: the 200-gold offer, the
yes/no; Yes: paid (character, else pool) or the no-money message on row 24; paid: the nothing-new words before
the item when it was identified already, else it is identified and the looks-like words before its new name
(string 0x22F70, `Msg.LooksLike`, importer 0.7.30), both in the box, then PauseOnly. While a text types or pauses
row 24 keeps the page's menu with the chosen entry lit; after a yes/no it keeps the question's line with the answer lit
(nothing redraws row 24 until the next question or row-24 message). **D** (s42, a shop save from `fight_save.py`
with the slot-F code, Thrender given 500 gold, a scroll with its first spell flagged for scribing (0x3C | 0x80) and an
unidentified scroll (0x35 = 1): View, Items, End, End, S, Y, Y, I, Y): the scribe warning (the name in its colour at
(1, 21), the warning typed in yellow, the lose-the-scroll question with No lit), Yes, the box blanked and the offer typed
under the warning's line with Yes lit, the deal question, Yes, the sold message, the page back with Ready lit (the
cell); Id's offer under the page menu with Id lit, Yes, the looks-like words and the new name typed under the deal
line with Yes lit, PauseOnly (0.97 s on both sides), the page. Before s42 the Client drew the
page's menu under the second text; now 96 of 104 Client frames equal on the Sell run (the misses: help lines, the
encumbrance below) and every Id frame but the item list (the run had not sold the first scroll). The test save's
encumbrance differed (DOSBox-X 1058, the Client 538: the edited gold and items are not in the save's weight word).
Settled s43: the program recalculates on **View** (every full draw of the page, 0x21642 → START 0x1758), not on Load
(`save-format.md`, derived bytes on Load); the Client's View now does the same.
**D** (s41, slot F's shop, Thrender: View, Items, Sell on the
readied flail, Id, Yes with too little gold, Escape): the unready message, the offer typed over rows 21–22 under
the page menu with Id lit, the deal question with No lit, the no-money message, the box blanked: 44 of 46
Client frames equal (the misses the help line and one transition frame between the shop menu and View's page).
Client: `CharacterView.SellOnPage` / `IdentifyOnPage` with `TradeContext.TalkOverItemsPage` (the party-menu
overlay stays up and draws the box, `PartyMenuFlow.TextOverPage`; the kept yes/no line `PartyMenuScreen._talkChoice`);
tests `ItemsPageTalkTests`, `PartyMenuScreenTests.Sell_and_ids_yes_no_line_stays_on_row_24_while_the_next_text_types`.

## Rest (0x17FAB → 0x24A66) — P, D

**0x17FAB** (the camp's Rest and the Magic menu's Rest): for every party member the preparation time (0x17831, below;
it also stores each member's base hours in record 0x2C); the longest m goes into the rest time: hours = m div 60, ten
minutes = (m − 60·hours) div 10, minutes = m mod 10 (days stay 0). Then the rest (0x24A66), then the rest time is
cleared and the position line redrawn.

**The rest loop 0x24A66**: each party position's timer DS:6DD0 := 0; all 72 flags DS:471E := 1; tick := 0; the text
box cleared; the rest time shown; DS:6DDA := 1; the rest-time menu. Unless it was left, while any of days, hours, ten
minutes, minutes is non-zero:

1. a key waiting (START keypressed): the rest time shown, the stop question 0x24A36 (Yes stops before the tick);
2. rest time − 5 minutes (0x24246);
3. every 5th tick the rest time is shown;
4. the clock + 5 minutes (0x241A1, which also ages the effects, below);
5. the heal count (0x2463D);
6. the preparation timers (0x248D6), then the hourly base count (0x2497E);
7. when $6DD2 > 0: DS:49FB + 1; at $6DD2 or more DS:49FB := 0 and 1d100 (the dice routine through 00B0:0048, the
   generator's `Random(100) + 1`) ≤ $6DD3 interrupts: the message box cleared, the rest time shown, the interruption
   message 0x24A45 (white, row 19), a key; the loop ends and the routine returns true.

Then the text box is cleared and DS:6DDA := 0.

**Rest-time menu 0x244ED**: highlighted word starts at 2 (minutes). Shows its label (0x243AF) at column 1, row 17, then days
and hours (two digits each, with a colon between), then minutes as 10 × ten-minutes + minutes; the highlighted word in white
(0x243BC). Menu (0x244A5): Rest, Days (hotkey Y), Hours, Mins, Inc, Dec, Exit. Extended keys: Up = Inc, Down = Dec, Left = the next word up
(past days back to minutes), Right = the next word down (past minutes to days); other extended keys do nothing.
Enter = Rest. **D** (s38, 3000 cycles): Left / Right move the field on row 17 (minutes → days → hours → minutes for
Right) and leave row 24's lit word as it is; a hotkey lights its word. The menu routine (0x2CC8C) lights an entry
from its capital to two cells before the next capital, so "daYs" lit shows YS white (D, A green) and Rest lit shows REST and the
D of DAYS white (the A stays green); while the rest runs row 24 keeps Rest lit. The span is the menu routine's for
every line (0x2C99A / 0x2CAA9, read s39: shop-format.md "The menu routine"); besides this menu only the icon editor's
"Parts color-1 color-2" line shows it (D s39). Client: `ChoiceRequest.LeftAnswer` /
`RightAnswer` / `ConfirmAnswer` (Enter = Rest; a click still picks the clicked word), `ExploreScreen.DrawLine`; every
row-24 and row-17 frame equal to DOSBox-X (tests `CampPlayTests.Rest_time_menu_left_right_…`,
`Rest_lit_also_lights_the_D_of_days`). Y / H / M pick the word; Inc adds 1 to it (5 to the minutes word when minutes are highlighted); Dec
takes 1 (5 minutes) through 0x24246; both then carry (0x2416F). Returns true on Rest, false on Exit / Escape.

**Rest-time arithmetic**: 0x2416F = the clock carry 0x240BE on DS:6DC2 (so a year overflow would age the party), then
any months fold into days (30 each), months := 0, days capped at 99. 0x24246 (count, unit): nothing when days, hours,
ten minutes and minutes are all 0; while count > word[unit] the next non-zero word above it (stopping at months) lends
one, carried down with each lower word gaining its limit; when the search reaches months the whole rest time is
cleared and count := 0; then word[unit] − count and the carry.

**Heal (0x2463D)**: DS:6DD8 + 1; at 0x120 (288 ticks, a day) each party member is healed 1 HP through 0x2C647 without
bypass (`spells-effects.md`); the rest time shown, the heal message 0x24623 (row 19), the party list redrawn when
anyone healed, a key, the box cleared, DS:6DD8 := 0. The message comes even when nobody healed.

**Preparation (0x248D6, 0x2497E)**: each tick every member's timer goes down by one (a byte: 0 wraps to 255). When it
is 0 and record 0x2C is 0, the next pending spell is finished: scribing first (0x247B3), else memorising (0x246F1);
the timer := 3 × the level of the next pending one (0 when none). Every 12th tick (an hour) each member with record
0x2C > 0 (signed) loses one; reaching 0, the timer := 3 × the level of its first pending scroll spell, else of its
first pending memorised spell (nothing is finished at that moment). So spells finish one at a time, 3 ticks (15
minutes) per level, after the base hours.

- **0x246F1** (memorise): the 21 slots in order; a slot above 0x7F: with done clear it loses bit 7, the message
  (0x246E3 after the name on row 19, the spell name on row 20, a key) shows and done is set; with done set the scan returns that
  spell's level (table byte 1). 0 when none follows.
- **0x247B3** (scribe): the readable items' scroll bytes above **0x80**; finishing one sets record 0x32 + id := 1
  (known), takes it off the scroll (far 0096:0043: the byte := 0, item word 0x30 − 1, below 0xD2 the item leaves the
  character; the scan goes on over the removed item's bytes), then its message (0x247A7). `[?]` The item removal routine
  (START) is not read beyond unlinking.

**Effect timeout 0x23DCC** (called by the clock advance 0x241A1, count and unit): outside camp (DS:49F3 ≠ 2) all 72
flags := 1; in camp nothing happens unless a flag of positions 1..$6E3E is set. The duration in minutes (count × the
clock limits below the unit, 16-bit) goes in steps of at most 10. Per step, each flagged creature of the list (flag
cleared first) walks its effect list up to the record that was last when the step began: a record with 0 minutes
(word +1) is skipped; one with more minutes than the step loses the step and sets the flag; any other is removed
(0x2AF10 through 00B0:002A, the handler with "removed" when byte 4 is set). After a removal the walk goes on at the following
record, except when the record before the removed one was the list head: then it starts again at the head, which ages
a second time in that step. Records after the walk set the flag when they have minutes left.

Outside camp the same routine runs after every clock advance (0x241A1 calls it with its own count and unit: steps,
Look, CLOCK, combat rounds), and in a fight (DS:49F3 = 5) the list DS:5D96 holds the monsters too, so they age as well.
Core: `CampTime.AdvanceOutsideCamp` / the `TimeOutEffects` overload with the flags; exploration through
`AreaLoop.EffectTimeout` (wired by `PorGame`), the fight's end of round through `FightKit.WireClock`. The flags
DS:471E are one array per session (`PorGame.Camp`, `PorPlayer.Camp`): what a fight or a step leaves in them decides
which members a later rest ages. DOSBox `fight_casts`: the Mirror Image's minutes 2 → 1 at the end of round 0, gone at
the end of round 2.

## Magic menu (0x18C22) — P

Menu DS:0531 (Cast, Memorize, Scribe, Display, Rest, Exit), no prompt; extended keys select the member; until Exit,
Escape or an interrupted rest.

**Check 0x17B9D** (activity 1 cast, 2 memorise, 3 scribe): cast with $49E5 > 0 → the no-magic message (0x17B33);
any activity with status 1 (animated) or record 0x10D = 0 → the no-condition message (0x17B53) with the
activity's ending (0x17B6A / 0x17B7A / 0x17B8A) (the character's message, a key).

**Spell lists** (the list builder behind the spell menu; menu types used by the camp): 0 memorised slots 1..0x7F; 1
the spell book, ids 1..55 with record 0x32 + id ≠ 0 (56 is never listed); 3 the spells of the character's readable
items (base slot 11..13), 6 the same above 0x80 only; 5 memorised slots above 0x7F. A level heading (DS:0A79 + 41 ×
level) comes before a spell whose level differs from the previous one; a pending spell is marked with an asterisk. For the scroll
lists a scroll identifies itself (item 0x35 := 0) when the reader has effect 0x10 or a cleric level with a cleric
scroll (slot 12); a still-hidden scroll adds nothing. The page title is the character's name, the word 0x239CE and the list's
words (0x23973–0x239BF); with an action (Cast, Memorize, Scribe: 0x27018–0x27026) the prompt 0x27053.

**Cast (0x17CD2)**: DS:6DB5 := nil; the check; then the memorised list (Cast) until nothing is chosen: the text box is
cleared and the spell cast (0x27C38); an empty list → the no-spells message (0x17CBA). **The cast 0x27C38 outside a fight**:
table byte 7 = 0 → the spell name (row 19), the combat-only line 0x27BC6 (row 20), the question 0x27BE0 (Yes: the memorised spell
is removed, START 0x202B). Else the cast message (0x27C16) and the spell name (a key), then the non-combat target handler **0x27AA9**: DS:6DB5
defaults to the caster; the target list is the caster; mode 1: done; mode 2: the member pick (prompt 0x27A96,
starting at DS:6DB5; giving up clears the targets and DS:6DB5 and nothing is cast); mode 4: every creature of the list;
other modes: nothing. On a pick: invisibility leaves the caster (0x2BEBC), the memorised spell is used up (START
0x202B), DS:681B := spell, the spell routine runs (table DS:6B84), DS:681B := 0.
Core models every spell routine 1–67 (`FightSpells.Modelled`, s13); a spell without one would be refused with a
notice, the spell kept. **Effect messages outside a fight** (read s27, P): 0x2C540's text goes to START 0x2B8B, whose
out-of-fight branch (DS:49F3 ≠ 5, at START BA:2004 → BA:217E) is START 0x16EC(creature, text, 10, pause 1) with no
picture; the unaffected words (0x2C532) call START 0x16EC with the same arguments. START 0x16EC outside a fight ignores
its row: r = 18 while DS:6DDA is set (rest), else 17 and the text box's frame is drawn again first (03F1:0000 with 1, 17,
38, 22, style 0: the frame one cell outside the box); (1, r)–(38, 22) cleared, the name at (1, r + 1) (START 0x1816),
the text word-wrapped into (1, r + 2)–(38, 22) in colour 10 (START 0x709:0x0768); with the pause flag PauseOnly then
START 0x2383, which outside a fight clears (1, 18)–(38, 22). Client: `CampScreens.EffectMessage` for 0x2C540's text and
the unaffected words. **The camp's own member messages** go through the same routine (P s39, text and pause flag at
each call): the refusals and the Cast / Memorize / Scribe ones (0x17CAC, 0x17D82, 0x181F2, 0x18457, pause 1; "can
memorize:" 0x17ED5, pause 0), Order's selected message (0x19095, 0) and Drop's (0x19195 pause 0, 0x191E7 / 0x1920B /
0x19258 pause 1). So the name is on row 18 in its colour, the text typed from row 19 while DS:4A00 stands, and the
pause is the game-speed delay with rows 18–22 cleared after it, never a key. **D** (s39, Drop answered No at speed 4):
"will be gone" typed under the name, the question, the relief message typed and cleared 0.9 s later; the Client
(`CampScreens.Say` → `PanelMessage`) had printed name and text together on row 17 and waited for a key; now 31 of 31
frames equal with the help lines masked (6 of 57 before). The text types out (0x77F8) only while DS:4A00 is set, which in the camp
means after the start-up or a fight with no PRINT or ENCMENU since (view3d.md "Typing state"); the Client's camp prints
through `ClientPlayer.Text`, which follows it (s29). `[?]` The other messages of a camp cast (damage through 0x2B8B,
healing, the spells' own) are still listed in our words.
**D** (s29, save A in camp, Brother Sean's Bless at speed 4 with DS:4A00 set; rows 17–23 compared pixel for pixel
with the Client, every distinct frame): the spell list page stays up and the text box's frame is drawn over it at row
17 (the redraw of START 0x16EC's camp branch); the cast message shows the caster's name (cyan) and the casts word
(green) on row 19, the spell's name on row 20, drawn at once (not typed), then the game-speed pause (0.9 s) and the
box cleared, no key; then for each member the name on row 18 and the blessed words typed from row 19, the pause, the
rows cleared, as read. The Client now matches those rows (`CampScreens.CastOne`, test `CampEffectMessageTests`). **Cast's target** (read s39): a
one-member spell outside a fight (0x27AA9, target type 2) redraws the camp screen (0x27AFA, START 0x27D9: the kept
spell page goes, the party list is drawn), then runs START's member pick 0x37E8 (00BA:2C48) on DS:6DB5, the remembered
target (the caster the first time, 0x27AB8): each pass draws the list with the pick lit (00BA:2CA1) and the menu "Cast
Spell on whom" with Select / Exit (00BA:2C41 / 2C19; keypad flag in the camp); End the next member, Home the one
before, both wrapping (00BA:2D29, 2D57); E or Escape (0) clears the pick (00BA:2DD6); the loop ends on a key of the
set 00BA:2C1F (0x0D, 0x1B, 'E', 'S'), which the menu's Escape (0) is not: **Escape clears the pick and the menu comes
again** with nobody lit (Select then ends with no target, Cast does nothing). With no pick, Home walks from the first
member to the one whose next pointer is nil (the last); End reads the next pointer of a nil record, 0000:0104 (the
INT 41h vector): no member, nobody lit, and a Select would cast on that address `[?]` (not tried; the Client keeps no
pick there). DS:5D92 does not change. **D** (s39, Brother Sean's Cure Light Wounds, End, End, S on Phineas): as read.
**D** (s41, the same spell from save A's camp): Escape left the menu up with Brother Sean no longer lit; End then lit
nobody; Escape, Home lit Phineas. Client: `CampScreens.PickTarget` (Escape: `CampScreens.Escaped`, the pick cleared
and the menu again; test `CampEffectMessageTests.The_target_picks_escape_clears_the_pick_and_the_menu_goes_on`).
**Read s30 (P):** after a pick Cast 0x17CD2 draws the text box's frame (03F1:0000 around (1, 17)–(38, 22), style 0:
rows 16 and 23, columns 0 and 39) over the page, blanks its inside (03F1:0137 = START 0x4047), casts (0x27C38), then
sets its position word to −1 (0x17D52), so the next 0x239D6 draws the window and title again and the list starts on
its first page (combat-menu.md "Spell lists' Next / Prev"). Nothing between clears row 24, so the list's menu line
stays. Client: the page stays under the box while the cast runs (`TradeContext.ShowPageUnder` →
`ClientPlayer.PageUnder`, drawn by `ExploreScreen.DrawOverPage`: the page, the frame, the box's text, row 24 kept
unless a key prompt or menu takes it), then the list comes back afresh (tests `CampEffectMessageTests`,
`CampPlayTests.A_camp_cast_keeps_the_spell_list_under_the_text_box`). A DOSBox-X check in s32 put 21 memorised
spells in one character. The title is on row 1, the first level heading on row 5, and the choose-spell prompt
precedes the menu on row 24. PgDn exposes the last four spells and keeps the selection on row 22. PgUp restores
the first page and its selection. The Client places the choose-spell prompt with the title.

**Memorize (0x1813B)**: the check; the pending list (type 5) first: when not empty, the keep question 0x180F0 — No
clears the pending slots (0x1798A), Yes ends here. Then until done: no slot byte at all (0x17DD5: cleric or
magic-user levels 1–3) → the no-slots message (0x18108); else the slots are shown (0x17DA1, per class with any
slot its free slots of levels 1–3, blank where the slot byte is 0) and the spell book (Memorize): nothing chosen ends;
a spell whose class and level have a slot left (0x17A8D: slot byte 0xB1 + 3·class + level less the memorised or
pending spells of that class and level, a byte that wraps) goes pending (+0x80) into the first zero byte from record
0x17 on (the walk is not bounded by the 21 slots), then the slots are sorted (0x18055: exchange sort by id, bit 7
ignored, empties first). When the book was shown, the pending list and the question again (0x18123; No clears).

**Scribe (0x1838C)**: the check; the pending scroll list (type 6) first (the keep question 0x182EA, No clears the bits
0x179CD, Yes ends). Then the scroll spells (type 3, Scribe) until nothing is chosen (an empty list → the message 0x18300): a
spell already known → 0x18318; a readable item with that spell pending → 0x18334; no slot byte for its class and
level (unchecked index) → 0x18357; else the first byte equal to the spell on **any** item (readable or not) gets bit 7. These three messages
are on the bottom line. When shown, the pending list and the question again (0x18376).

**Preparation time 0x17831**: base hours 0; 4 when any memorised slot or readable scroll byte is pending; 6 when a
pending spell's level is above 2; record 0x2C := base; minutes = base × 60 + 15 × (sum of pending memorised levels) +
15 × (sum of pending scroll levels) (byte sums).

**Display (0x187ED)**: a window (title 0x187E4) listing each member's name (heading) and its effects in list order:
ids 1, 2, 5, 8–14, 16, 17, 19–25, 28, 29, 32–34, 36–39, 41, 42, 45, 46, 49, 51–53 by the first spell 1..56 whose table
effect byte (+10) is the id (the text 0x186F1 and the id when none); ids 4, 7, 27, 31, 35, 44, 50, 54, 55, 59, 61, 71, 72, 89
by their own texts (0x186F9–0x187C6, in that order); every other effect is left out. The text 0x187D0 when none,
a blank line after each member. The screen (P s34): the list opens with an empty entry (flag 0); each name is a label (flag 1),
the effect rows and the none text are plain entries with no indent of their own, and the blank line after each member
is a plain (pickable) entry. One window START 03F1:02E8 (1, 1, 38, 22, colour 15, the title centred on row 1), then
0x2D669 (overlay stub 03C5:0034) with empty prompt and word, colours 11 / 10 / 15, keypad flag 1, the window
(1, 4)–(38, 22) (19 rows: the empty entry on row 4, the first name on row 5) and the Exit flag; START 03C5:0043 frees
the list and START 0x27D9 empties the text window. Client: `CampScreens.Display` (`SpellPageLayout.CentredTitle`),
test `CampPlayTests.Display_is_the_programs_list_…`.

## Alter (0x1948A) — P

Menu DS:055A (Order, Drop, Speed, Icon, Pics, Exit), prompt 0x19441; extended keys select the member.

- **Order (0x18FB1)**: prompt 0x18F71 with DS:0584 (Select, Exit) or DS:05AD (Place, Exit). Select mode: extended
  keys pick the member. Place mode: Home moves the current character up (0x18CDA: it swaps with the one before; the
  head goes to the end of the list), End down (0x18E32: swaps with the one after; the last goes to the head); the
  current character stays the same record. Enter, P and S switch the mode (into Place: the message 0x18F9F; back:
  the message box cleared). The Client adds the mouse: in Place mode a click on a name above the current member moves
  it up one place, below it down one (our addition; the program has no mouse).
- **Drop (0x19116)**: the only member: the quit question 0x190A8 (Yes removes it and quits). Else the message 0x190B6, the
  question 0x190C3: Yes → the farewell 0x190D5 (record 0x10D ≠ 0) or 0x190E7 (otherwise), then 0x1CC10 (free the icon, size
  decreased): the member leaves the list, the one before it (or the new head) becomes current, $6E3E − 1. No →
  the message 0x190FC.
- **Speed (0x192C8)**: the speed line (0x19261, the value, 0x1926F; row 18); the menu offers Faster while n > 0,
  Slower while n < 9, and Exit (0x19286–0x19296); Down / F speed up, Up / S slow down, until Exit or Escape. The game delay is DS:49E2
  (`EclMemory.GameSpeed`): new game 1, the save copies it into $49FC, Load takes it back. The Client's Alter Speed,
  DELAY, the combat pauses and the Done menu's Speed all use `EclMemory.GameSpeed`. The program never scales its fixed
  delays (flights, hit pictures, the attack pose, picture loops) by it; the Client's ANIMATION setting (QoL, default
  ORIGINAL = the program's timings; combat-menu.md "Animation speed") does, and game speed 0 plus INSTANT is the
  fastest play.
- **Icon** (0x1951F): the icon editor 0x1D1AC (far 0078:002F; character creation's routine, read s20, start-menu.md
  §3, Core `IconEditor`) on the current character (DS:5D92), then the screen redrawn for the interface state (START
  0x3379, 00BA:27D9). Client: `CampContext.EditIcon` runs the party menu's editor (`PartyMenuFlow.EditIcon`); the
  explore screen puts its icon page over the camp (keys, pad, mouse) and closes it when the Alter menu asks again.
  On the overland (DS:49FA > 1) the editor's end loads ICON blocks 0 and 1 into icon slots 11 and 12 (0x1D84A); slot 11
  is the marker's third animation slot, which held block 2, so the marker draws the wrong frames for counters 4 and 5
  until the overland is entered again (**D**, s25; geo-format.md "Marker"). Client: `AreaLoop.IconEditorDone` after
  the camp's editor, matched to DOSBox-X.
- **Pics**: monsters on/off, portraits on/off, Exit (0x19449–0x19485); M and P toggle the two flags (saved as $49FF bit 1 and bit 0).

## Save (0x1FD3E) — P

The slot menu (prompt 0x1FCAB, the ten letters A–J 0x1FCBD); Escape gives up (the camp still asks the quit
question). The file `savgam<letter>.dat` in the save directory is created (a disk prompt on failure); the
saving message 0x1FD11 on the bottom line; the three wall sets' ids and slots into $4AFA–$4AFF, the game delay into
$49FC, monster pictures × 2 + portraits into $49FF, the disk number into $6E12; then the layout of `save-format.md`
(disk byte, the three word areas, the script area, the party position, location type, interface state, the count n
and eight string[40] stems `CHRDAT<letter><k>`), then each member's files (`.SAV` record; `.ITM`/`.SPC` deleted and
written again only when there are items / effects) and the roster files `<name>.CHA/.ITM/.SPC` of each member deleted.
`SaveWriteTests`: save A (shipped) and save B (written by the program in DOSBox-X) read into memory and written back
give the same bytes, except the leftover memory after each stem; the member files come back byte for byte.

The Client writes saves to its own folder (`$XDG_DATA_HOME/goldbox/por-saves/`), never into the game folder; `--save
X` looks there first. `[?]` The roster-file deletion (its name-to-file rule) is not done. Location type: the
program writes DS:49FA, the view mode (0x1FF9D; `save-format.md`); `PorSaveGame.Build(memory, n, letter, state)` takes
it from `EclMemory.ViewMode` and $49FC from `EclMemory.GameSpeed` (s15); the Client's camp Save uses it. The bytes
after each stem and unused name
slots are written as zero (leftover memory in the program).

## Open `[?]`

- The camp Cast's other messages (damage, healing, the spells' own).
- The roster-file deletion on save.
- Cell positions of the camp's messages other than the rest's (the Client keeps its own frame).
