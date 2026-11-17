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
recomputed when the items screen was redrawn; the shop's Buy list shows 7 items per page (the 8th is on the
"Prev" page). Temple, Take and Appraise are not checked in DOSBox.

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
  weight word is **not** recomputed by these routines (`[?]` when the program next recalculates +0x102: START 0x1758
  runs in Buy's copy step, before the payment).
- **Price checks**: Buy (0x7035) and the temple (0x4636) compare the price with the **low word** of the character's
  total; Identify compares the 32-bit total. The pool is compared as a signed 32-bit total. The remaining pool
  total is passed as one word (so a pool above 65,535 gold loses the excess) — settled, was `[?]`.
- **Carrying limit** (0x24C89): START 0x1F4B (the strength allowance, `character-format.md` "Movement
  recalculation") + 1500, as a word. **Overloaded** (0x24CDD, creature, n): weight + n (16-bit sum) > limit; then
  "room" = limit − weight.
- **Add platinum** (0x24E8C, n): if overloaded by n, the character gets `room` platinum (and weight), the pool's
  platinum the rest (an "overloaded, money goes to the pool" message); else all n. Used by the appraiser's sale.

## Shop menu (0x7212)

Buy, View, Take, Pool, Share, Appraise, Exit; Take and Share only while the pool holds money or items (0x25C07).
The temple uses the same Take/Pool/Share/Appraise/View routines.

**Buy** (0x7035, P). An item picker over the treasure list (0x6D04) returns a key; B or Enter buys the highlighted
item, any other key leaves. Price = the item's value word (0x3A).
1. If price ≤ low word of the character's gold, or else ≤ the pool's gold: copy the item (0x6F0D).
2. The copy (0x6F0D) first asks 0x23795: refused (message, nothing paid) when the character has more than 15 items
   (+0xC7 > 15) or weight + item weight × count (count 0 = 1; 16-bit) > the carrying limit. Else a copy with
   `next` cleared goes to the **end** of the character's item list (+0xC8) and the character is recalculated
   (START 0x1758). The shop's item stays: stock never runs out.
3. Then the payer pays (character first) as above. Else a "not enough money" message.

**View → items** (0x21AED, P where marked). In a shop (DS:49F3 = 1) the items menu adds **Sell** and **Id**
(strings at unit 0x2103A + 0xEA8 / 0xEAE; 0x22104–0x2217E). Sell is added for a member with 0x84 < 0x80, and for
others only when in play = 0 or status = 1 (`[?]` intent).
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

**Take** (0x25974 → 0x25711, P): pick a kind held in the pool (kinds 6 down to 0), type an amount (0x24F1D).
If the amount would overload the character: message, nothing moves. Else amount := min(amount, pool[kind]);
pool − amount; the character's word + amount, weight + amount. Afterwards DS:6DC0 = any kind left.

**Pool** (0x25191, P): DS:6DC0 := 1; every party member whose 0x84 is 0 or 0xB3 adds its seven words to the pool,
loses that much weight, and its seven words are zeroed. Other members (NPCs) keep their money.

**Share** (0x252C3, P):
1. n = members with 0x84 = 0 or 0xB3 (0x25269). For each kind with a positive pool: share = pool div n,
   remainder = pool mod n (longint, kept as words); else both 0. `[?]` n = 0 with money in the pool is a division
   by zero.
2. For each member with 0x84 < 0x80 (so 0xB3 members count in n but receive nothing here), kinds 6 down to 0:
   if the share fits (not overloaded), add it, then one coin of the remainder if the **whole remainder** would fit (0x25450 tests the remainder, then adds 1); else add `room` and
   put share − room back into the remainder.
3. For kinds 6 down to 0 with a remainder: every member in list order (no 0x84 test) takes min(remainder, room),
   room = limit − weight as an unsigned word (an already overloaded member wraps to a huge room and takes it all).
4. The pool := the remainders; DS:6DC0 := any left.
A member overloaded before the share gets a wrapped `room` in step 2 as well (16-bit subtraction): parity keeps it.

**Appraise** (0x2663B, P): needs gems or jewellery on the current character (else a message). Menu Gems /
Jewelry / Exit, repeated until Exit or nothing is left.
- Each appraisal takes one gem (+0x92) or jewel (+0x94) and rolls its value: gem **1d100**: 1–25 → 10, 26–50 → 50,
  51–70 → 100, 71–90 → 500, 91–99 → 1000, 100 → 5000. Jewel **1d100**, then Random(n) + b: 1–10 → 100 + R(900),
  11–20 → 200 + R(1000), 21–40 → 300 + R(1500), 41–50 → 500 + R(2500), 51–70 → 1000 + R(5000), 71–90 → 2000 +
  R(6000), 91–100 → 2000 + R(10000) (two draws).
- Choices: Sell, plus Keep when 1 more weight does not overload the character and it has fewer than 16 items. K
  (only when offered) adds an identified item (type 70, word 0x65 gem / 0xD6 jewel, value, weight 1, `hidden` 0)
  at the end of the inventory. Any other key sells: value div 5 platinum through "add platinum" (so a jewel loses
  value mod 5). After each appraisal the character is recalculated (START 0x1758).

**Exit** (shop and temple): with money in the pool a question offers to go back; leaving keeps the pool unspent
(the next TREASURE overwrites it). Pool items alone do not ask.

## Temple (0x5280) — P

Heal list (0x4F4E): 10 names from DS:00F1 + 41·k (k = 1..10), the 10th leaves. Every entry acts on the **current
character** (DS:5D92); prices are immediates in each routine.

Flow per entry: if the condition is absent the priest says so and asks whether to cast the cure anyway (0x458A); on yes, or when
the condition holds, payment (0x4636): the price is shown with a yes/no question; character (16-bit total) or
pool pays, else the not-enough message and the answer becomes no. After payment a "cured" message shows and the effect runs —
even for Raise Dead / Stone to Flesh on a target that does not qualify, which then does nothing (money spent).

| # | Routine | Price | Condition | Effect after payment |
|---|---|---|---|---|
| 0 | 0x47C0 | 1000 | effect 33 (blindness) | remove effect 33 (0x2AF10) |
| 1 | 0x4864 | 1000 | any of effects 31, 34, 43, 44, 50, 57 (DS:0114) | remove all six (DS:681F = 1 meanwhile `[?]`) |
| 2 | 0x4978(1) | 100 | none | heal 1d8 |
| 3 | 0x4978(2) | 350 | none | heal 2d8 + 1 |
| 4 | 0x4978(3) | 600 | none | heal 3d8 + 3 |
| 5 | 0x4CC8 | 1000 | effect 55 (poison) | remove effects 55, 22, 15 (DS:681F = 1) |
| 6 | 0x4A87 | 5500 | status 6 (dead) or 1 | raise dead, below |
| 7 | 0x4DB1 | 3500 | an item with `cursed` ≠ 0, or effect 36 | DS:6C95 := DS:5D92, then the remove-curse routine 0x29558: if the character has effect 36, that effect is removed (0x2BF6B → 0x2AF10) and nothing else; else `cursed` := 0 on the **first** cursed item only |
| 8 | 0x4E9F | 2000 | status 7 (stone) | if still 7: status 0, in play 1, HP 1 |

- **Dice**: only entries 2–4 draw (1–3 × Random(8) + 1 through 0x2BCCD, summed in a byte; the bonus is added
  after; max 27).
- **Heal** (0x2C647, called with flag 0): only for status 0, 1, 4 or 5 (set at OVR 0x2C627); effect 50 blocks it;
  HP 0x11B += amount (byte), capped at max HP 0x32; no check before payment (full HP still pays). Then if in play
  0x10D = 0: status 5 → 4, and a status-4 creature outside combat (DS:49F3 ≠ 5) gets effect handler 0x4E (0x2AEEA)
  `[?]` (whether that makes it status 0 is not read).
- **Raise dead** (0x4B22–0x4C9D), literally: remove effects 32 and 55 (DS:681F = 1); HP 1, status 0, in play 1;
  Con (0x14) − 1 if > 0; d = max HP − 0xB1 (rolled HP) if positive, else 0 (byte). If the new Con ≥ 14: s = Σ over
  level bytes 0x96 + k (k = 0..7) that are > 0: k = 2 (fighter) → level × (Con − 14); else 2 × level when Con > 15,
  else level (byte sum); d := d div s when s > 0; max HP −= d unless Con ≥ 17 and the fighter level ≤ 0. No dice,
  no survival roll, no ageing. `[?]` what this is meant to compute.
- **Effect removal** (0x2AF10): the code removes one record per call (the model drops the first match); the
  handler call on removal follows `item-format.md` "Effect record"; the role of DS:681F there is `[?]`.

## Open `[?]`

- When the weight word is recomputed after a payment (coins rewritten, weight not).
- Sell's extra condition for members with 0x84 ≥ 0x80; the menu routine 0x2CC8C's output byte.
- Temple: effect handler 0x4E on a revived character, DS:681F, the intent of the raise
  dead hit-point rule.
- Share with n = 0.
