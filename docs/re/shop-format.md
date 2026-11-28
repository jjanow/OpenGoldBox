# Shops, temples and money (Pool of Radiance, DOS v1.3)

What COMBAT runs when the shop flag $6E6C or the temple flag $6DE2 is set (`combat-format.md` "Shop" / "Temple"
for the frame and menus), and the money routines they share with the loot step. Read from the program in
session 11 (2026-11-17); addresses are GAME.OVR file offsets unless marked START (offsets in the unpacked
`START.EXE` image). Status marks: **P** read in the program, **D** checked against DOSBox-X, `[?]` not settled.

Code: `src/GoldBox.Core/Game/` — `Money`, `ShopRules`, `TempleRules` (pure rules; menus and texts stay with the
caller). Tests: `tests/GoldBox.Core.Tests/ShopRulesTests.cs`; DOSBox replay `TreasureShopCaptureTests`.

**DOSBox (s11, capture `treasure_shop`)**: in a shop stocked by a random TREASURE, with the first character
holding 150 cp, 37 sp, 9 ep, 3 gp, 600 pp (3010 gold): Buy of a 2000-gold item left exactly 202 pp and nothing else;
Id of an already identified flail took 200 (162 pp left); Sell of the flail (value 3) gave 1 gp; Pool and Share
over six members gave the pool, coins and weights the model computes (remainders to the first members). **D** for
those steps. Seen as well: the weight word stayed stale after the payment (1449 with the old coins counted) and was
recomputed when the items screen was redrawn; the shop's Buy list showed 7 items with the 8th on the "Prev" page:
Buy's position word 1 with the list routine's top 1 after start-up, so the window started at the second item (s46,
"Buy" below). Appraise is not checked in DOSBox.

**DOSBox (s12, capture `temple`)**: Cure Blindness on a character who is not blind (cast anyway: 1000 paid, no
draw), Cure Light (1d8: 1 draw), Serious (2d8 + 1: 2 draws, capped at max HP), Critical at full HP (600 paid, 3
draws, no change) and Raise Dead on a living character without the money (nothing paid, no draw): coins, HP and
RandSeed match `TempleRules` + `Money.Pay` (`TempleCaptureTests`). The weight word stayed stale through all
payments and was recomputed when View opened. **D** for those entries and the payment.

## Money — P

- **Character money**: seven words at creature +0x88: copper, silver, electrum, gold (+0x8E), platinum (+0x90),
  gems (+0x92), jewellery (+0x94). Carried weight: word +0x102 (`character-format.md`); each coin, gem or jewel
  counts 1.
- **Pool** (DS:67F4): seven dwords in the same order (the TREASURE money operands land here too). DS:6DC0 (a data
  byte, not the VM cell $6DC0) = "the pool holds money".
- **Coin values** (DS:0D24, five dwords, in copper): 1, 10, 100, 200, 1000. Gold (DS:0D30 = 200) is the unit.
- **Gold total** of a character (0x238F8) or of the pool (0x24D31): (Σ k=0..4 coins[k] × value[k] + 100) div 200
  (longint): every coin counts, rounded half up; gems and jewellery do not count.
- **Paying** rewrites the payer's coins. Character (0x24DB9, one word argument): the five coin words := 0,
  platinum := n div 5, gold := n mod 5. Pool (0x24E0E, one word): the same on the pool dwords. So after any purchase
  the payer holds only platinum and up to 4 gold; silver and copper are gone (converted at their value). The
  weight word is **not** recomputed by these routines. The next recalculation (START 0x1758) of the payer is in
  View: its character page (0x21084) calls 0x21642, which recalculates the current character first (0x2165C), and
  the items screen (0x21F32) recalculates after every action (0x224AF). Buy's copy step recalculates before the
  payment, so after a purchase +0x102 counts the item and the old coins until View (settled s15; matches the s11 /
  s12 DOSBox notes).
- **Price checks**: Buy (0x7035) and the temple (0x4636) compare the price with the **low word** of the character's
  total; Identify compares the 32-bit total. The pool is compared as a signed 32-bit total. The remaining pool
  total is passed as one word (so a pool above 65,535 gold loses the excess) — settled, was `[?]`.
- **Carrying limit** (0x24C89): START 0x1F4B (the strength allowance, `character-format.md` "Movement
  recalculation") + 1500, as a word. **Overloaded** (0x24CDD, creature, n): weight + n (16-bit sum) > limit; then
  "room" = limit − weight.
- **Add platinum** (0x24E8C, n): if overloaded by n, the character gets `room` platinum (and weight), the pool's
  platinum the rest (an "overloaded, money goes to the pool" message); else all n. Used by the appraiser's sale.

## Shop menu (0x7212)

Buy, View, Take, Pool, Share, Appraise, Exit; Take and Share only while the pool holds **money** (s12: 0x25C07
returns two flags, money = any of the seven pool dwords non-zero, items = treasure list non-empty; the shop and the
temple test only the money flag, and the shop's stock is the treasure list anyway). The temple uses the same
Take/Pool/Share/Appraise/View routines.

**Party selection** (s12): after the menu the shop compares the key with 0x47 / 0x4F and calls 0x23819 with it,
without testing the menu's extended-key flag, so both G / O and Home / End reach it (the scan codes of Home and End
are 0x47 and 0x4F). 0x23819: 0x47 → the previous member of the party list (the head wraps to the last), 0x4F → the
next (the last wraps to the head), any other key → the head. The camp calls it only for extended keys (Home / End);
the loot menu tests G / O like the shop, and pools only on a non-extended P.
**0x23819 whole (W s49, run under emulation: `por_venue_oracle.py`, Core `PartySelection.Select`, test
`VenueProgramTests`).** It walks the list from the head DS:5D96 (links at +0x104) and writes DS:5D92. Head nil →
current nil. 0x47: current = head → the last member; else the member whose link is the current one (with no current
member that is the last; a current record not in the list walks off the end → nil). 0x4F: the current member's link,
or the head when the link is nil (with no current member it reads the link at 0000:0104, the interrupt table `[?]`;
the engine leaves the current member as it is). Any other key → the head. Callers and the keys they pass: the shop
(0x741D / 0x742C), temple (0x545A / 0x5469) and loot menu (0x6345 / 0x6354, 0x66ED / 0x670A) only 0x47 / 0x4F (G / O or
Home / End); the party menu only extended keys in its set {G, O} (0x19C9E); the camp (0x19762), Magic (0x18C85), Alter
(0x194E2), Order (0x1901E), the menu helper 0x8B57 (0x8BCE) and the 3D / overland command line (0x17254, 0x173C3, any
extended key but the arrows it acts on) pass **every** extended key: their menus run with the keypad flag 1, so
in those menus the arrows (not on the command line), PgUp, PgDn, Ins, Del, the F-keys, Alt-letters and the digit keys
3 / 5 / 9 and backslash, either row (cursor codes 0x51, 0x20, 0x49, 0x37: the conversion takes the character)
make the first member current there, and the digits 7 / 1 act as Home / End (the digits read, not seen; Up at the
camp menu and PgUp on the 3D command line seen in DOSBox-X s49). The Client answers Home / End (and G / O
where tested) through the routine. **Decided (user, s50): hybrid.** Every key the Client gives no meaning to in that
menu takes the program's rule: F1–F10, Alt-letters, Ins / Del, PgUp / PgDn, the digits 1–9 and backslash (either row)
make the first member current, 7 / 1 act as Home / End (Client `DosKeys.MemberPick`, `ChoiceRequest.ExtendedKeysPickFirst`
on the camp, Magic, Alter, Order's Select and the menu helper; `ExploreScreen.PickMember` on the command line). Keys
the Client uses there keep its meaning, a deliberate difference: the arrows (the lit word, the command line's steps),
Confirm, Cancel, hotkeys and any key bound to a command-line action. Order's Place mode: other extended keys not read
`[?]`, left alone. Tests `KeyBufferTests.Member_pick_codes_of_extended_keys`,
`CampScreenTests.Camp_extended_keys_make_the_first_member_current`.
**Client run on Xvfb (s57, DOS art, save A):** 3D command line: PgUp and F5 make the first member current (after End
picked another); camp menu: Insert and Alt-Z the first member, keypad 1 / 7 as End / Home; Magic: F2; Alter: PgDn; the
menu helper 0x8B57 (the loot's exit question): keypad 1 the next member, Shift-F3 the first. Shift-F1 there opens the
Client's controls screen (F1 has a Client meaning; only the loot and venue menus give Shift-F1 the program's Take).
Order's Select not tried.

**Extended keys in the venue menus** (read s52 lane A, decided s53, built s54). The shop (0x72F9–0x7426), temple
(0x5327–0x5463), Take: (0x6315–0x6354) and Appraise (0x2663B's first menu) loops run the menu routine with the keypad
flag and compare the byte it returns with their letters without testing the extended flag, so an extended key whose
scan code equals a letter acts as it (a digit gives its cursor-pad code first): F8 (0x42) Buy; Shift-F3 (0x56) View;
Shift-F1 (0x54) Take and Del (0x53) Share in the shop and temple, run whatever the line shows (with an empty pool
the coin take 0x25974 blanks its window (1, 1)–(38, 22), the list routine returns at once on the empty list
(0x2D69C) and the loop ends, then the venue's redraw (read s54; the Client shows only the redraw, the same end
screen); Share shares nothing); F7 (0x41) Appraise; keypad 7 / 1 (0x47 /
0x4F) the party selection; PgUp / keypad 9 (0x49) Items in Take:; Home / keypad 7 (0x47) Gems in Appraise. P (Pool)
and the temple's H (Heal) are tested only for a plain key, so Down / Up do nothing there. **Decided (user, s53):**
the s50/s51 rule: keys the Client gives no meaning to act as the program; keys with a Client meaning keep it (Right,
0x4D = M in Take:, keeps moving the lit word; Home / End stay the member keys where the menu picks members). Client
`VenueScreens.VenueKeys`, the Appraise menu in `MoneyScreens.Appraise`, `DosKeys.KeypadMenuScan`; tests
`VenueTests.Venue_extended_keys_follow_the_s53_rule`, `KeyBufferTests.Keypad_menu_codes`.
**Client run on Xvfb (s57, DOS art, `por_test_saves.py` saves `shop-stock`, `loot-shop`, `loot-temple`, `loot-full`;
keys by keycode through XTEST):** shop: F8 opens Buy (with an empty stock Buy returns at once, so the `shop-stock` save
gives it three items), Shift-F3 View, F7 Appraise, P then Del shares the pool, Shift-F1 with the pool empty only
redraws, keypad 1 / 7 the next / previous member with NumLock off and on (the Client reads them as End / Home either
way). Temple: keypad 1, Shift-F3, P + Del, Shift-F1 the same. Take: (money and items): PgUp, and keypad 9 with NumLock
off and on, open the item take. Appraise (gems taken from the loot first): Home acts as Gems (the gem's value
line, then the Sell / Keep menu).

**Buy** (0x7035, P). An item picker over the treasure list (0x6D04) returns a key; B or Enter buys the highlighted
item, any other key leaves. Price = the item's value word (0x3A). Before listing, the picker writes value 1 into
every stock item whose value is 0 (0x6D3C: nothing in a shop is free) and renames each item: 30 spaces, the value
right-aligned in them, the name copied over from the first character, the length set to 30 (the list's text; s12,
s46). A name of 30 characters or more hides the value: the naming routine's names end in a space, so the cleric
scroll's 29 letters and that space cover it (DOSBox-X s46: the row shows the name alone). **The page** (P + D s46):
the window (1, 1)–(38, 22) titled "Shop", the list routine 0x2D669 with the window (1, 4)–(38, 22), 19 rows paged by
Next / Prev, the prompt "Items: ", the word "Buy", Exit; the position word starts at 1 (0x7050) and keeps the row
the routine leaves, so the window opens at the second item after start-up (top 1) and with the second row lit after
a list that left the top at 0 (`por-art-formats.md` "Creation's list labels"). No member switch on the page (Home /
End move the highlight in the list routine); the buyer is the shop menu's current member. Refusals (not enough
money 0x7136, the copy step's overload 0x6F39, Take's overload 0x2573C) go through START 0x2233: the text on row 24
in green over the page with its rows unlit, the game-speed pause, then the page again (DOSBox-X s46: a cloak without
the money; the Client's `VenueScreens.RowMessageOver` equal pixel for pixel, during and after the pause).
1. If price ≤ low word of the character's gold, or else ≤ the pool's gold: copy the item (0x6F0D).
2. The copy (0x6F0D, on DS:5D92) first asks 0x23795, which runs the full stat recalculation START 0x1758 on the
   character (0x237A1) and then refuses (message START 0x1693, nothing paid) when it has more than 15 items (+0xC7
   > 15) or weight + item weight × count (count 0 = 1; 16-bit) > the carrying limit. Else a copy with `next`
   cleared goes to the **end** of the character's item list (+0xC8) and START 0x1758 runs again (0x7018): item
   count, weight, dice and the other derived bytes. The shop's item stays: stock never runs out.
3. Then the payer pays (character first) as above, with no recalculation after it. Else a "not enough money"
   message.
**What is recalculated (P + D s43).** Buy and the loot's item Take share 0x6F0D (Take calls it through the stub
0028:002A), so both recalculate the taker in full before the test and after the copy; Appraise (below) after every
pass of its menu. Checked under emulation: `por_trade_oracle.py` runs 0x6F0D on 300 made-up characters, every
other one with stale derived bytes and item count (`TradeProgramTests`); the `treasure_shop` capture's record after
Buy (s4buy: weight 1449 = the items and the coins *before* the payment, 0xC7 = 3) equals `ShopRules.Buy` with
`StatRecalc` byte for byte. **D** (s43, 3000 cycles, save A's Thrender with 0x102 edited to 7 and dice to 3d7+9,
test saves of `por_test_saves.py` style; each result read from a camp Save right after, since Load, Save, the camp
and leaving the loot or shop do not recalculate): a loot of one random item (TREASURE 129, COMBAT with no monsters)
left untaken kept 7 and 3d7+9; Take of the long sword gave 598 (= 150 + 350 + 60 + 38 coins), 1d6+2, 0xC7 = 3; a
shop (TREASURE 131, $6E6C = 1) Buy of 10 arrows (150) with 300 pp gave 1236 (the 336 coins before the payment, not
the 272 after), 1d6+2, 0xC7 = 3. The Client: `ShopRules.GiveCopy` / `Buy` with `TradeContext.Recalculate`; Sell and
Appraise leave 0xC7 to the next recalculation as the program does (Sell's is the Items loop's 0x224AF).

**View → items** (0x21AED, P where marked; the shop calls it at 0x730D, the temple at 0x5341: page and keys in `camp.md` "View", s39). In a shop (DS:49F3 = 1) the items menu adds **Sell** and **Id**
(strings at unit 0x2103A + 0xEA8 / 0xEAE; 0x22104–0x2217E). Sell is added for a member with 0x84 < 0x80, and for
others only when in play = 0 or status = 1 — the same gate as Trade in View (0x21BFE) and in the items menu
(0x22034): an NPC's goods can be traded or sold only once it is out of play or animated (status 1). Settled s15
(the rule was read in s12; only its intent was open). Core: `FightItemScreens.ItemsEntries` (`MayDeal`).
- **Sell** (S, 0x22472): first 0x21DAA: a readied item is refused (a "must be unreadied" message); a scroll (0x2A255) whose
  props hold a byte > 0x7F (spell flagged for scribing) asks for confirmation first. Then 0x22D28: price = value / 2;
  with count > 1, price = (count × price, 16-bit) / 20. (The program has a branch for count × price without the
  /20 for types 73 and 28, but its test is `type = 73 and type = 28`, so it is never taken.) The price is offered with a yes/no question. On yes: the item is removed (START 0x20A9), the character gets
  N div 5 platinum and N mod 5 gold. If that overloads the character: when room ≤ the platinum, it keeps `room`
  platinum and the rest goes to the pool's platinum; else all of it; the gold always goes to the character. The
  weight word is not updated here.
- **Id** (0x22F8C): a 200-gold offer, yes/no; on yes 200 gold from the character (32-bit total)
  or else the pool, else the not-enough message; after payment: `hidden` = 0 → a "nothing new" message (the
  200 are spent); else `hidden` := 0 and the item is renamed (`item-format.md` "Identify").

**Loot item take** (0x6184, s12): the same copy step 0x6F0D gives the highlighted treasure item to the current
character (T or Enter; refused when it cannot carry it); on success the original is unlinked from the treasure list
and freed. Repeats until another key or an empty list (W s52, run under emulation; on an empty list from the start it ends at once: `combat-format.md` "Take"). Its page (0x6092, P + D s46) is Buy's with the title "Loot",
the word "Take" and the plain names, position word 0 (0x619F) then kept.

**Take** (0x25974 → 0x25711, P + D s46): the window (1, 1)–(38, 22) with no title, then until the pool is empty or
the player leaves the list: the kinds held in the pool as a list (built 6 down to 0, each put in front, so copper
first: "Copper 100"), the list routine with the window (2, 2)–(15, 8), the prompt "Select type of coin", Exit; any
key but Escape / E / 0 takes the row (Return too); position word 0 once (0x259A4), then kept. Then the amount
(0x24F1D, colour 10, "How much " + the coin + " will you take?", at most the pool's low word) on row 24 over the list
drawn unlit. If the amount would overload the character: message, nothing moves. Else amount := min(amount,
pool[kind]); pool − amount; the character's word + amount, weight + amount. Afterwards DS:6DC0 = any kind left.
DOSBox-X s46: Copper, 30, Return → the list again with "Copper 70".
**Amount 0** (P + D s47). The input 0x24F1D answers 0 to Escape and Val of the typed digits to Return (Val('') = 0:
nothing typed is 0 too). 0x25974 calls 0x25711 with it whatever it is (0x25BAF), then frees the list and sets DS:6DC0
(0x25BB7–0x25BF7) and loops. 0x25711 in order: the carry test 0x24CDD (weight word 0x102 + amount, 16 bits, above the
limit 0x24C89 → the message from its own string through START 0x2233 and nothing else); amount := the pool's low word
when amount > pool (32-bit compare; never true, the input caps at the low word); pool − amount (32 bits); the
character's coin word + amount; 0x24CC3 weight + amount. So 0 refuses a member already over the limit with the message
and does nothing for anyone else. DOSBox-X s47 (scratch save A, TREASURE 100 copper + COMBAT, member 1 with 60000
platinum and weight 60000): Take, Copper, Escape → "OVERLOADED" on row 24 over the dimmed list, then the list again
with "Copper 100"; Return with nothing typed the same. The Client (`MoneyScreens.Take`) now passes 0 to
`ShopRules.Take`; its screens equal DOSBox-X's at both points.
**Original bug, fixed (user, s48)** (P s47): the program's input maximum is the pool's **low word** (0x25B89), so a
pool of 65536 or more of one coin (Pool can gather that from several members' 16-bit words) lets only
`pool mod 65536` be taken at a time, and at exactly 65536 (or any multiple) the maximum is 0: every amount typed
becomes 0 and that coin can never be taken (Share still divides it). The Client's maximum is the pool, at most 65535
(`MoneyScreens.TakeMaximum`; the five-digit input and the character's coin word hold no more), under every art.
**Original bug, fixed (user, s50):** 0x25711's carry test adds weight + amount in 16 bits and the coin word adds
unchecked, so an amount above 65535 − weight passes the test and both words wrap (the program reaches that with a pool
of about 63000 to 65535; the s48 fix would make it reachable from larger pools). The engine's maximum is
min(pool, 65535 − the character's coin word of that kind, 65535 − its weight), and Take cuts a larger amount to it, so
neither word wraps (Core `ShopRules.TakeMaximum`; test `VenueTests.Take_maximum_leaves_no_wrap_of_weight_or_coins`).

**Pool** (0x25191, P): DS:6DC0 := 1; every party member whose 0x84 is 0 or 0xB3 adds its seven words to the pool,
loses that much weight, and its seven words are zeroed. Other members (NPCs) keep their money.

**Share** (0x252C3, P):
1. n = members with 0x84 = 0 or 0xB3 (0x25269). For each kind with a positive pool: share = pool div n,
   remainder = pool mod n (longint, kept as words); else both 0. n = 0 with money in the pool (only NPCs other
   than 0xB3 in the party) reaches the longint division START 0xB9F6 with a zero divisor: run-time error 200
   (0xBA5D → RunError 0xB062), the program quits to DOS before anything changes (s15; `ProgramHaltException`).
   The Client shows the halt (our wording), waits for a key and closes; the game takes no more commands.
2. For each member with 0x84 < 0x80 (so 0xB3 members count in n but receive nothing here), kinds 6 down to 0:
   if the share fits (not overloaded), add it, then one coin of the remainder if the **whole remainder** would fit (0x25450 tests the remainder, then adds 1); else add `room` and
   put share − room back into the remainder.
3. For kinds 6 down to 0 with a remainder: every member in list order (no 0x84 test) takes min(remainder, room),
   room = limit − weight as an unsigned word (an already overloaded member wraps to a huge room and takes it all).
4. The pool := the remainders; DS:6DC0 := any left.
A member overloaded before the share gets a wrapped `room` in step 2 as well (16-bit subtraction): parity keeps it.

**Appraise** (0x2663B, **W s52**: read whole and run under emulation, `por_trade_oracle.py` "appraise", test
`TradeProgramTests.Appraise_matches_0x2663B_under_emulation`): needs gems or jewellery on the current character (else a message). Menu Gems /
Jewelry / Exit, repeated until Exit or nothing is left.
- Each appraisal takes one gem (+0x92) or jewel (+0x94) and rolls its value: gem **1d100**: 1–25 → 10, 26–50 → 50,
  51–70 → 100, 71–90 → 500, 91–99 → 1000, 100 → 5000. Jewel **1d100**, then Random(n) + b: 1–10 → 100 + R(900),
  11–20 → 200 + R(1000), 21–40 → 300 + R(1500), 41–50 → 500 + R(2500), 51–70 → 1000 + R(5000), 71–90 → 2000 +
  R(6000), 91–100 → 2000 + R(10000) (two draws).
- Choices: Sell, plus Keep when 1 more weight does not overload the character and it has fewer than 16 items. K
  (only when offered) adds an identified item (type 70, word 0x65 gem / 0xD6 jewel, value, weight 1, `hidden` 0)
  at the end of the inventory. **Original bug (s52):** with no items (+0xC8 nil) the new record is put in a
  local (0x26AFB gem, 0x26E4E jewel) and never into +0xC8, so the kept gem or jewel is lost: the count is down, no
  item, no platinum (emulation: every such Keep). **Fixed, not an option** (user, s53): Core adds it as the first
  item (`ShopRules.Appraise`). The
  first menu runs with the keypad flag 1, so Home (scan 0x47 = G) appraises a gem too (built s54, "Extended keys in
  the venue menus"). Any other key sells: value div 5 platinum through "add platinum" (so a jewel loses
  value mod 5). Every pass of the menu, Exit included, ends with START 0x1758 on the character (0x26EC2); the Keep
  test reads the stored weight and item count (0x24CDD, +0xC7 against 16). With nothing to appraise at the start the
  no-gems message and no recalculation (0x26661); when the last gem or jewel is gone after an appraisal the routine
  ends without a message (0x26698 → 0x26ECF; the Client said the message there before s43).

**Exit** (shop 0x7350–0x740D, temple 0x5384–0x544A; P + D s47): with money in the pool (0x25C07's first answer) a
question offers to go back; leaving keeps the pool unspent (the next TREASURE overwrites it). Pool items alone do not
ask. The shop prints its message through the text routine 0709:0768 in the box (1, 17)–(38, 22), colour 10, cleared
first, and the question after it in colour 15 with no clear (it runs on after the message's two spaces, no line
break). The **temple** passes the clear flag 1 and colour 10 for its question too (0x53E8), so the priest's message is
typed and at once replaced by the question (DOSBox-X: the priest's parting message typed to its end, then the box
cleared and the go-back question typed in green); probably meant as the shop's. **Original
bug, fixed (user, s48)**: the Client prints the temple's question without the clear, running on after the priest's
message as the shop's does, in the temple's colour 10 (Client s49, test save `loot-temple`, Pool, Exit: the message
and the question both in the box). Both then call the **menu helper 0x8B57** (002B:0061) with an empty prompt,
"~Yes ~No", colours 13 / 10 / 15 and its Return flag [bp+8] = 0: it runs the menu routine 0x2CC8C (keypad flag 1)
on the **shared highlight** until the key is a letter or digit (the set at its CS:1664, 0x8BEF); a hotkey answers its
index (0x8C11–0x8C49), any other letter 0xFF (unreachable: the menu routine ignores letters that are no hotkey);
Return (13 with the flag 0) and Escape (0) loop; an extended key (Home / End, an arrow, a keypad digit) runs the party
selection 0x23819 and the party list (0x8BC2–0x8BE0) and loops. Answer 1 (No) sets the end flag; any other answer
blanks the text box (0709:0DD3) and the menu runs again. DOSBox-X s47 (test saves `loot-shop` / `loot-temple`, Pool,
then E): the question opens on Yes (the cell back to the first after Exit); Escape, Return and X changed nothing; End
lit the next member and kept the question; Y left the box empty and the menu lit on its first word; N left. The Client
(`MoneyScreens.StayAtExit`, `Dialog.MenuHelper`) equal on each screen but the help lines. **Deliberate difference
(user, s48)**: the keyboard's Return answers the lit word, as the pad's Confirm and a click do (the program loops on
it); Escape still does nothing (Client s49: Return at the loot's question on Yes went back to the menu with the box
blank, Right then Return left). The cursor-pad meaning of the digits (keypad flag 1) is not modelled in the Client's
menus.

**The entry and the redraws** (P s47): 0x7212 sets DS:49F3 = 1, calls START 0x3379 (case 1: the windows blanked
inside, the venue box's head + body) and the party list, zeroes the pool and DS:6DC0, names every stock item (START
0xFE2), then loops: the menu (0x25C07 for the line), the key's routine, START 0x3379 again after B and T (0x743C) and
after A when the appraisal ran (0x744D), and the party list every pass (0x745A). The temple (0x5280) is the same with
its own keys, but its redraw tests B (0x5471, a copy of the shop's; no temple key is B) and T (0x5479), and A
(0x548A); its H runs the heal list only for a non-extended key (0x532B: 'H' is also Up's scan code). After a loot the
text box is therefore empty when the shop opens (DOSBox-X s47; the Client kept the loot's question before s47).

## Temple (0x5280) — P

Heal list (0x4F4E): 10 names from DS:00F1 + 41·k (k = 1..10), the 10th leaves. Every entry acts on the **current
character** (DS:5D92); prices are immediates in each routine.

**The heal list's screen** (P + D s46). Row 24 blanked (0709:0DD3), the window (1, 1)–(38, 22) and the title window
(1, 1)–(38, 1) (START 03F1:02E8, frames one cell outside), then each pass: the name and the question at (1, 1) in
colour 15 (0709:0626), the text box (1, 17)–(38, 22) blanked, and the list routine 0x2D669 (0x50E7) with the window
(2, 4)–(38, 15), colours 13 / 10 / 15, an empty prompt and the word "Heal": rows from column 2, the lit one white, the
others green, no cursor; row 24 "HEAL EXIT" from column 0. H or Return (0x50F2) runs the row's entry, the 10th sets the
end flag; key 0 ends too. DOSBox-X s46: Escape and E leave (E as Exit's hotkey leaves the shared menu cell on the 2nd
word, so the temple menu then opens on View; after Escape on Heal); with Exit lit (',' / '.'), Return still runs the
lit row (13). The entry's texts and questions come in the text box with the list above, its rows unlit: the absent
condition's message is START 0x16EC(member, text, 0, 0) at 0x45B9 (the name on row 18 in its colour, the text from
row 19 in green, no pause), then the cure-anyway question with 13, 10, 15. At the end the list is freed, row 24
blanked, the screen drawn again (START 0x27D9, the text box empty) and the party list.
**The position word** [bp−0xC] (the list's highlight, passed by reference) is never stored before the first call. Its
stack word is the 0 argument (0x5314) of the temple menu's call of the menu routine 0x2CC8C (03C5:002A at 0x531C),
pushed as AX whose AH is 0 from `mov ax, 0x28` (0x52D6 / 0x52EB) through the two string calls (System 0x0B25 and
0x0B0B keep AH); 0x5332 is the routine's only caller, right after that menu. So the word is **0 on every entry**: the
list starts lit on its first row and takes the list routine's global top (DS:6B29) to 0. Later passes keep the row
the list returned. DOSBox-X s46 (residue probe site 5, `por_dosbox_game.py --residue`, test save G): 0 on the first
call after a fresh start (top 1 before), again after leaving the list lit on row 3 and after View; the first list lit on
Cure Blindness; after Cure Light Wounds and a No to its price, the list lit on Cure Light Wounds again.
The pay question (0x4636: 03C5:0039 at 0x46CE) passes the colours 13, 15, 10: the words white, the lit word and the
hotkey letters green ("Y" green, "ES" white, "NO" green with No lit), as only the camp cast's two questions (0x27CCA /
0x27D30) also do; the other 23 yes/no calls pass 13 or 14, 10, 15. Client: `VenueScreens.HealList` (a list routine
page, `SpellPageLayout.WholeTitle`, `ListRequest.ReturnRunsFirstAction`), `ChoiceRequest.MenuColours` (also the camp
cast's lose-it question 0x27CCA, read only; the Client does not ask 0x27D30's); pixel-equal to DOSBox-X on the first
list, the price question, the list after it, Exit lit, the absent-condition message with its question, and the temple
menu after Escape and after E (but the Client's help lines).

**The Amiga's heal list** (hunk 25 +0x408E, read s47, P + FS-UAE s47 on disk A's save A patched as a temple, speed 9).
The same routine as DOS's, with C's choices: row 24 blanked (hunk 25 +0x197A with 0, 24, 39, 24), the window
routine (hunk 25 +0x19F4: blank, frame blit hunk 25 +0x16D4, centred title through the string routine) with (1, 1)–(38,
22) and an empty title, then again with (1, 1)–(38, 1); each pass the name and the greeting text (strcat) at (1,
1) through the string routine in **pen 28** (DOS colour 15, white), the window routine with (1, 17)–(38, 22) (the text
box blanked and its frame blitted again), and the list routine (hunk 13 +0x2B76) with the window (2, 4)–(38, 15), pens
26 / 28 / 16, an empty prompt, the word "Heal Exit" and no Exit flag. The names are the Amiga's own table (hunk 26 +0x3AE
+ 41·k, DOS's words). Return or H runs the row (+0x41FC–0x4206), the tenth or key 0 ends. **The position word**
(−0xC(a5)) is never stored before the call either: compiled C, its stack slot holds whatever the temple menu's calls left
(`[?]` not read; FS-UAE s47: lit on Cure Blindness on entry, and on Cure Light Wounds again after its price was refused,
as DOS). The entries (hunk 25 +0x39A4 absent, +0x39FE pay) are DOS's: the absent text through the creature message
routine (hunk 13 +0x4970, no pause) and the cast-anyway question with pens 26 / 28 / 16, then +0x4A8C blanks (1, 18)–(38,
22); the price text through the text window's print in pen 28 with the clear flag, then "pay for cure " with 26 / 16 /
28 (the swapped pens, `PorAmigaText.YesNoSwappedWordsPen`); no money: the not-enough-money text through hunk 13 +0x4928 (row
24, pen 28, Delay(speed · 5 ticks), row 24 blanked) with the price text left in the box; paid: +0x4A8C, then the creature
message (member, "is cured.", 0, pause 1). At the end (+0x42CA) the list is freed, the whole screen blanked (0, 0)–(39,
24), redrawn (hunk 13 +0x5B16) and the party list (+0x3A96). FS-UAE s47 against the Client `--art amiga` (DOS save
A's temple; shift (−2, +1)): the first page, the list after a refused price, the price question, the absent message with
its question and the not-enough-money line equal pixel for pixel but the names (other parties), once the Client drew the
name line in pen 28 (`SpellPageLayout.AmigaTitlePen`) and the frames in the program's order (the big window, then the
title window over it: the Amiga frame's side tiles below row 2 are the big window's; the Client had framed (0, 2)–(39,
16) by itself). **Both arts (s47):** the not-enough-money line is START 0x1693 on row 24 (0x475A) and the cured text is
START 0x17E3 then 0x16EC(member, "is cured.", 0, 1) (0x476B–0x478E): the Client had typed both into the box and waited
for a key (`VenueScreens.Heal`, `MemberMessage`). DOSBox-X s47 (save A patched as a temple, speed 9): Cure Blindness
cast anyway and paid with too little money: the not-enough-money text on row 24 in green, the price text left in the box, as
the Client now; the Client `--art dos` heal page (frames in the new order) equal to DOSBox-X's pixel for pixel. The
cured path is read, not seen in either emulator (neither party had the money).

Flow per entry: if the condition is absent the priest says so and asks whether to cast the cure anyway (0x458A); on yes, or when
the condition holds, payment (0x4636): the price is shown with a yes/no question; character (16-bit total) or
pool pays, else the not-enough message and the answer becomes no. After payment a "cured" message shows and the effect runs —
even for Raise Dead / Stone to Flesh on a target that does not qualify, which then does nothing (money spent).

| # | Routine | Price | Condition | Effect after payment |
|---|---|---|---|---|
| 0 | 0x47C0 | 1000 | effect 33 (blindness) | remove effect 33 (0x2AF10, no lock) |
| 1 | 0x4864 | 1000 | any of effects 31, 34, 43, 44, 50, 57 (DS:0114) | remove the first record of each of the six through 0x2AF10, DS:681F = 1 meanwhile (below) |
| 2 | 0x4978(1) | 100 | none | heal 1d8 |
| 3 | 0x4978(2) | 350 | none | heal 2d8 + 1 |
| 4 | 0x4978(3) | 600 | none | heal 3d8 + 3 |
| 5 | 0x4CC8 | 1000 | effect 55 (poison) | remove the first record of 55, 22, 15 in that order through 0x2AF10, DS:681F = 1 meanwhile (below); **the engine removes every record of each with the lock held (original bug fixed, s51)** |
| 6 | 0x4A87 | 5500 | status 6 (dead) or 1, tested before the questions | raise dead, below (paid but nothing done when the test failed) |
| 7 | 0x4DB1 | 3500 | an item with `cursed` ≠ 0, or effect 36 | DS:6C95 := DS:5D92, then the remove-curse routine 0x29558: if the character has effect 36, that effect is removed (0x2BF6B → 0x2AF10) and nothing else; else `cursed` := 0 on the **first** cursed item only |
| 8 | 0x4E9F | 2000 | status 7 (stone) | if still 7: status 0, in play 1, HP 1 |

- **Dice**: only entries 2–4 draw (1–3 × Random(8) + 1 through 0x2BCCD, summed in a byte; the bonus is added
  after; max 27).
- **Heal** (0x2C647, called with flag 0 at 0x49C2 / 0x4A11 / 0x4A61): only for status 0, 1, 4 or 5 (set at OVR
  0x2C627); effect 50 blocks it; HP 0x11B += amount (byte), capped at max HP 0x32; no check before payment (full HP
  still pays). Then if in play 0x10D = 0: status 5 → 4, and a status-4 creature outside combat (DS:49F3 ≠ 5) gets
  effect handler 0x4E (0x2AEEA, "removed", no record). **Settled s51** (run under emulation, below): handler 0x4E
  (0x10634) is the revive 0x2C753 with the creature's HP: outside a fight the square test 0x317E1 passes, so status 0,
  in play 1, HP kept, the revived message (START 0x16EC) and the teams counted. So a temple heal wakes an unconscious
  or dying character out of play and brings it back to status 0. Core `EffectHandlers.RestoreHitPoints` (78 →
  `Revive`).
- **Raise dead** (0x4B22–0x4C9D), literally: remove effects 32 and 55 (DS:681F = 1); HP 1, status 0, in play 1;
  Con (0x14) − 1 if > 0; d = max HP − 0xB1 (rolled HP) if positive, else 0 (byte). If the new Con is below 14 the
  routine ends (0x4BCE → 0x4CA1: max HP untouched). Else s = Σ over
  level bytes 0x96 + k (k = 0..7) that are > 0: k = 2 (fighter) → level × (Con − 14); else 2 × level when Con > 15,
  else level (byte sum); d := d div s when s > 0; max HP −= d unless Con ≥ 17 and the fighter level ≤ 0. No dice,
  no survival roll, no ageing. **Original bug, fixed, not an option (user s61)**: the division takes off 1 or 2
  points where the Con point lost was worth one a level, and Con 14 counts for classes the hall gives nothing at 14
  (worked cases in `s60-lane-C.md` question 1). The engine (`TempleRules.RaiseDead` / `RaiseDeadLoss`) keeps the
  removals, HP 1, status 0, in play 1 and Con − 1, then takes off max HP the hall's own Constitution bonus (0x1D8B0's
  per-class term: DS:408E[Con] signed, +1 above 16 and +1 above 17 when the class byte 0x2F is 2) for the old Con minus
  that for the new, per level: Σ over the classes with a level > 0 of level × (bonus(old) − bonus(new)), divided by
  the number of those classes (as training and creation add (rolls + Σ bonus) div classes); max HP never below 1.
  A fighter 16 → 15 at level 5 loses 5 (the program 2); a magic-user 7 → 6 at level 5 loses 5 (the program nothing:
  below Con 14). Without the program's tables (no DS:408E) max HP is left as it is. The two removals run before the HP rule: a
  record of 32 (Animate Dead) with byte 4 set runs handler 32 (0xF776) under the lock: no death, but it writes the
  hostile byte (param >> 4), auto, undead level, attack level, move 12, NPC byte and type 0 (s51, emulation).
- **Effect removal** (0x2AF10): the code removes one record per call (the model drops the first match); the
  handler call on removal follows `item-format.md` "Effect record". **DS:681F** (settled s49): 0x2AF10 never reads
  it; the handlers do. Its readers in the whole program are 0xEC7A (the re-add every recurring handler uses: among them
  15, 43, 44, 50, 62, and 34 through 43 / 44), 0xF784 (handler 32: no death) and 0xFFFA (handler 57: nothing at all). A disease handler that
  runs on removal (byte 4 set) acts as on a timeout: it re-adds itself through 0xEC7A and only then does its harm
  (Strength, a hit point, the 1-point heal), so without the lock a cure would put the disease straight back and harm
  once more; with it the record just goes. Handler 31 (0xFF9A) ends the creature's actions on its combat record (START
  0x31D7, record +0x108) whatever the lock.
- **Cure 1 whole (0x4864, W s49, run under emulation: `por_venue_oracle.py`, test `VenueProgramTests`).** Found :=
  any of the six ids on the character (START 0x2D39, DS:0114 + 1..6). Not found: the not-diseased text (CS:02CF) to
  the "cure anyway" question 0x458A, its answer replacing the default Y. On Y: the price text (CS:02E0) and 1000 to the
  pay question 0x4636; on its Y: DS:681F := 1, for i = 1..6 0x2AF10(DS:5D92, DS:0114[i], nil), DS:681F := 0 (written 0,
  not restored). Core `TempleRules.CureDisease` with the game's effect engine outside a fight (`TradeContext.Effects`).
- **Every entry whole (W s51, `por_venue_oracle.py` "templeEntries", test
  `VenueProgramTests.Temple_entries_match_the_program_under_emulation`, 1300 cases).** Entries 0x47C0, 0x4864,
  0x4978(1..3), 0x4CC8, 0x4A87, 0x4DB1, 0x4E9F run on made-up characters (statuses 0/1/4/5/6/7 in and out of play,
  Con 0–19, signed levels, rolled HP, effect records 55, 22, 15, 32, 36, 33 and the disease ids with byte 4 set or not,
  doubles, cursed items), the questions answered Y / N: the questions asked, the record, effect list and items, DS:681F,
  RandSeed and the START 0x16EC count equal Core's (`TempleRules.Apply` with the engine; the count not for cure 7,
  whose spell routine's messages the rules do not raise). **Poison (0x4CC8) whole:**
  test START 0x2199(member, 55, var record); absent: the not-poisoned text (CS:072E) and 0x458A; on Y the price text
  (CS:073F), 1000 to 0x4636; on its Y: DS:681F := 1, 0x2AF10(DS:5D92, 55, nil), (…, 22, nil), (…, 15, nil), DS:681F := 0.
  The cure runs after payment whether or not 55 was there. **Raise dead (0x4A87) whole:** [bp−0x106] := status 6 or 1,
  tested before the questions; absent: the not-dead text (CS:04F8) and 0x458A; price text (CS:0505), 5500; on Y, only when
  [bp−0x106] is set, 0x4B22–0x4C9D (above, with a local copy of DS:5D92); otherwise the money is gone and nothing happens.
  **Remove curse (0x4DB1):** the cursed-item walk stops at the first cursed item; else START 0x2199(member, 36); the
  pay path sets DS:6C95 := DS:5D92 and calls the spell routine 0x29558 (read whole s60): with effect 36, 0x2BF6B says
  its text (unit CS:1078) through START 0x16EC(member, text, 10, 1) and removes the record (0x2AF10), then 0x29558 says
  CS:251E through START 0x2B8B(member, text, 1); else the first cursed item's byte 0x36 := 0 and CS:252B through START
  0x2B8B; nothing cursed, nothing said. Outside a fight both end in START 0x16EC's camp branch (name row 18, text row 19,
  PauseOnly, rows 18–22 blanked), after the "is cured." message. Core raises them in order (`TempleRules.RemoveCurse`'s
  callback; the venue oracle's START 0x16EC and 0x2B8B counts now compared for cure 7 too), the Client shows them in the
  text box under the heal list (`VenueScreens.Heal`).
  **Handler 22's own lock (original bug, fixed: user decision s51, not an option):** handler 22 (0xF3D0) on removal
  sets DS:681F := 1 (0xF40B), removes 15 and writes DS:681F := 0 (0xF424), inside the cure's own lock. So with two
  records of 15 (and 22 with byte 4 set) the cure's removal of the second 15 runs unlocked: handler 15 re-adds itself
  for 10 minutes and takes a hit point (the party list redrawn; 0x2C44A's message when that drops the character). The
  character leaves the temple still carrying a recurring 15. And with two records of 55, handler 22 finds the second
  and kills the character (status 6, the poison-death text) during the paid cure. Both seen under emulation and in
  DOSBox-X (below). **The engine** (`TempleRules.CurePoison`) removes every record of 55, then every record of 22, then
  every record of 15, setting DS:681F := 1 again before each removal, and leaves it 0 at the end as 0x4D8C does: no 55
  is left when a handler 22 runs, and no handler 15 runs unlocked, so the cure never harms or kills. Handler 22 itself
  is unchanged (it also runs at Slow Poison's end). `VenueProgramTests.Temple_entries_match_the_program_under_emulation`
  expects this fixed result where the program's cure harms (worked out from the input: the three ids gone, record,
  RandSeed and messages unchanged, lock 0); `ShopRulesTests.Poison_cure_removes_every_record_and_never_harms`.
  **Other users of the lock (s51, every write of DS:681F in GAME.OVR and START):** 1 / 0 pairs at 0x48FF / 0x4936
  (cure 1), 0x4B24 / 0x4B55 (raise dead), 0x4D47 / 0x4D8E (cure 5), 0xF40B / 0xF424 (handler 22), 0x292AE / 0x2933D
  (Cure Disease 0x292A2) and 0x29E81 / 0x29E9C (spell 58 0x29E65); 0 at the start-up 0x2A43D. Handler 22 is the only
  one that writes 0 inside another's lock, and only the poison cure and spell 58 remove a 22 under a lock; cures 1
  and 6 and Cure Disease never do, so the lock-clearing touches no other temple cure or camp cast. Spell 58 removes
  the first 55 (no lock), then under the lock the first 22 and writes 0 right after, so the cleared lock changes
  nothing there; but with two records of 55 its handler 22 finds the second and kills the target, as the unfixed
  temple cure did (an original bug, fixed, not an option, user s51: `FightSpells.Spell58` removes every 55 and 22
  under the lock).
  **D s51** (test save G of `por_test_saves.py`, Thrender's `.SPC` = 55, 22, 15, 15 and Bakshi's = 55, 55, 22, all
  byte 4 set, money added; Neutralize Poison paid on each): Thrender "is cured", then HP 11 → 10 and the party list
  (START 0x0767 from handler 15, 0xF28A) drawn over the heal list, which the next pass redraws only row by row, so the
  names and HP stay over its right half; Bakshi "is cured", then "dies from poison" (name row 18, text row 19), HP 0,
  the party list over the list again (0xECB3's end). Row 24 keeps the pay question's line ("PAY FOR CURE YES NO") under
  both messages. The Client (same save, before the s51 fix): the same HP, status and texts in the box, and (once it
  kept the answered pay question's line on row 24 under the entry's pauses, `ExploreScreen.DrawOverPage`) the text box
  and row 24 equal to DOSBox-X pixel for pixel under both messages. Since the fix the Client cures both without harm
  (no hit point lost, no death, no party list over the heal list), a deliberate difference from these DOSBox-X screens.
## Open `[?]`

- None for the temple (s61: raise dead's hit-point rule decided as an original bug, fixed, above).

## The menu routine (0x2CC8C) — P (s15)

Arguments (Pascal order, as WHO's picker START 0x37E8 passes them): prompt, item line, three bytes ([bp+0x12],
[bp+0x10], [bp+0x0E]; colours by their use), keypad flag [bp+0x0C], a flag [bp+0x0A] that enables a timed repeat
(not read further), var extended [bp+6]. Keys (upper-cased by START 0xC7AA): Escape returns **0** (not extended);
Enter returns 13 only when [bp+0x10] or [bp+0x0E] is non-zero (else it is ignored); ',' / '.' move the highlight
back / on (wrapping); space returns ' '; a letter or digit that is an item's hotkey moves the highlight there and
returns it; a zero-prefixed key returns its scan code with extended = 1 only with the keypad flag (without it the
key is ignored and the menu waits on, 0x2CE57–0x2CE68: read s49); with the keypad flag a digit 1–9 or a backslash
returns a cursor-pad scan code, even when the digit was a hotkey (its highlight moved first, 0x2CF60: read s49) (1 End, 2 Down, 3 PgDn, 4 Left, 5 ' ', 6 Right, 7 Home,
8 Up, 9 PgUp, backslash 0x37; table DS:288C) with extended = 1; other keys are ignored. The extended byte is also
stored in DS:6B2C. This is the "output byte" the shop's G / O test does not look at.

**Entries and the lit span (P s39, D s39).** One routine draws every horizontal menu line (36 far callers, START
0x38A5, and the item list 0x2D8AC and yes/no 0x2D9EA in its own unit), so the rule is the same for all of them.
0x2C99A splits the item line: every character in the set at 0x2C97A (the digits and the capitals) starts an entry
and is its hotkey; an entry ends two cells before the next one starts (position − 2), the last one at the line's
length. 0x2CAA9 draws the prompt's length + i − 1 for the i-th character on row 24: inside the highlighted entry's span
in [bp+0x0E] (when non-zero), else any capital or digit (set 0x2CA89, the same) in [bp+0x0E], else in [bp+0x10]; the
row is then blanked from the line's end to column 39. So a lit entry runs from its capital to two cells before the
next capital; where every word starts with its capital and one space separates the words, that is the whole word. Only two lines in PoR v1.3 have a hotkey that is not the first character of its word (every
length-prefixed string in START.EXE and GAME.OVR scanned, the DS lines and the ECL HMENU words too): the rest-time menu
"Rest   daYs …" (camp.md "Rest-time menu": Rest lit also lights the D) and the icon editor's main line "Parts color-1
color-2 Size Exit" (DS:07BF + 41, hotkeys P 1 2 S E): Parts lit lights PARTS and the COLOR of color-1 (the '-' green,
the 1 white as a capital/digit); color-1 lit lights its 1 and the COLOR of color-2 (its "color-" before the 1 green);
color-2 lit only its 2. **D** (s39, camp Alter → Icon, '.' three times): exactly these cells; the area, camp, Alter,
rest-time and yes/no lines (Drop's question: No lit on entry, '.' lights Yes) light whole words, as predicted. The Client drew one frame of
the previous highlight (Yes) before the yes/no routine's No (0x2D9B9); `ExploreScreen.Draw` now takes a request's
opening highlight before drawing it (s39). The
fight's and the shop's lines are predicted the same and were matched as whole frames in earlier captures (shop s22 /
s24, fight s29–s38), not taken again in s39. HMENU (0x1E17) builds its
line as "~WORD ~WORD … ~WORD", and 0x8A40 lower-cases every capital, then deletes each '~' and upper-cases the
character after it, so each choice has one capital (its first letter; a choice of several words
is one entry); no v1.3 HMENU word holds a digit. **0x8A40 read whole (s54):** (var line, out keys); keys := ''; for
i := 1 to the line's length at entry: a character in the set CS:154D (the unit at 0x74D3: '0'–'9' and 'A'–'Z') gets
+0x20, so a digit would become 'P'–'Y', a capital; a '~' is deleted (System Delete), the character now at i is
upper-cased (UpCase) and appended to keys, the hotkey list (at most 40). The loop keeps the entry length, so after a
delete it walks the copies of the last character left past the new length (lower-cased, never drawn). Client
`AmigaView.TildeCase` follows the set and the second '~' since s56 (the Amiga copy, hunk 4 +0x17A0, read s56: the same set and
'~' rule, walking to the current strlen, and a space not before a '~' stored as 0xFF, drawn blank); no v1.3 line shows
the difference (no digit, no "~~"). A one-choice HMENU passes 15 for both colours (all white). Client:
`ExploreScreen.DrawLine` (also behind the party-menu pages' `PartyMenuScreen.Line`, so the camp's icon editor).
