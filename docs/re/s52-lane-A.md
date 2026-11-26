# s52 lane A: after combat, temple, shop, money / loot, items

Units: stub 25 after combat (0x557E–0x6BF3), stub 22 temple (0x4577–0x54AD), stub 28 shop (0x6CD4–0x7470), stub 8F
money, treasure and loot (0x24C89–0x26EE3), stub 85 items (0x2103A–0x23B4B).

Statuses: `exec` (oracle + Core test), `read` (branch by branch, port matches), `fixed` (difference fixed), `bug?`
(original bug, question for the user), `open` (not finished).

## 1. The last ranked play gap: 0x6184 on an empty list

Settled: not a bug. With neither money nor items, Take (0x62B6) runs the item take 0x6184, because its first test is
only the money flag (0x62C3). 0x6184's page 0x6092 draws its window, then the list routine 0x2D669 takes its nil-head
exit (0x2D69C): position 0, no item, key 0, no menu, no key wait, and the shared cell DS:6B2B left alone. The key is
not T or Return, so the take ends and START 0x3379 redraws the loot screen. There is no hang and no state change.

- Oracle: `por_venue_oracle.py` "lootTake" (400 cases, 80+ with an empty list) runs 0x62B6 → 0x6184 → 0x6092 →
  0x2D669 for real. The empty cases log exactly the window, the empty page and the redraw, with DS:6B2B unchanged.
- DOSBox-X (save E, all coins taken, "View Pool Exit", Shift-F1 sent by keycode, with Shift-F3 → View as the
  control): a 60 fps capture shows the loot screen redrawn over about 20 frames. Row 24 and the lit word stay as they
  were, and the "Loot" title never shows.
- Client: no page, then `VenueRedrawn`. The `[?]` is cleared in `combat-format.md`.

Fixed in the same pass, from reading 0x62B6 whole. These are real differences in the Client's `LootTake`:
- After each "Take:" menu key, the program asks 0x25C07 again and ends Take as soon as the money or the items are
  gone (0x6370–0x6387). The Client looped instead: after taking the last item it went straight into the coin take
  (and the other way round). It now returns to the loot menu.
- The coin take is followed by START 0x3379 (0x631E / 0x639B), and the item take ends with it. The Client redrew
  after neither; both now call `VenueRedrawn`.
- Home / End in the "Take:" menu go to 0x23819 (0x633D / 0x634C). The party list is drawn after every key
  (START 0x1307). The Client's Take menu had no member keys.
- Core now holds the port (`LootRules.Take`, `LootRules.TakeItems`), and the Client drives it.

**Question 1 for the user (Take: menu extended keys).** The menu routine runs with the keypad flag, and 0x62B6 compares
the returned byte without looking at the extended cell. So Right (scan 0x4D, keypad 6) acts as M, Money, and PgUp
(0x49, keypad 9) acts as I, Items. In the Client, Right moves the lit word, and PgUp does nothing in this menu. Should
the s50/s51 rule apply here: keys the Client gives no meaning to (PgUp, keypad 9) as the program, while the arrows
keep the Client's meaning? Not built; the Client keeps its current keys.

**Question 2 for the user (Appraise: a gem kept with an empty pack is lost).** The appraiser 0x2663B, Keep branch,
at 0x26AFB (gems) and 0x26E4E (jewellery). The routine walks the character's item list to its end to append the new
record. When the list is empty (+0xC8 nil), it stores the new record in its local "last item" variable and never in
+0xC8. The gem or jewel is gone: its count has already gone down, no item appears, and no platinum is paid. To reach
it in play: a character with no items at all and a gem or jewellery (from the pool's Take or a share) chooses Appraise
→ Gems → Keep in a shop. Emulation confirms it on every such Keep (42 of 500 oracle runs). Core now does the same
(`ShopRules.Appraise`, faithful); the old Core added the item. Fix (add the item as the first one) or keep?

**Noted, not a question (Appraise's first menu).** It runs with the keypad flag, so Home (scan 0x47 = G) appraises a
gem, as Home picks Gems. This is the same family as the s50 extended-key rule; the Client does not give Home that
meaning here. It goes with question 1 if the user wants these keys handled.

## 2. Routines

| Routine | Status | Note |
|---|---|---|
| 0x6184 | exec | loot item take, run whole under emulation (empty list included); Core `LootRules.TakeItems` |
| 0x62B6 | exec, fixed | Take: exit after either flag clears, redraws, Home / End; Core `LootRules.Take` |
| 0x6092 | exec (caller) | the item page, run inside 0x6184 (window, names and list routine trapped / run) |
| 0x25C99 | exec | TREASURE's item maker: every type 0..255 and 1744 random, RandSeed set (`por_trade_oracle.py` treasure); `TreasureMaker` matches. Dead `ClientPlayer.MakeRandomItem` with a stale `[?]` removed |
| 0x25C5B | exec (caller) | the plus roll, inside 0x25C99 |
| 0x25191 | exec | Pool (`por_trade_oracle.py` money) |
| 0x252C3 | exec | Share, including the division-by-zero halt (money) |
| 0x25269 | exec (caller) | Share's member count |
| 0x25711 | exec | the coin take's step (takeCoins); amounts that would wrap are the s50 fix, not compared |
| 0x24E8C | exec | AddPlatinum (takeCoins) |
| 0x2663B | exec, bug? | Appraise run whole with its menus scripted (appraise); see question 2. The value bands and the Keep / Sell test match |
| 0x25C07 | exec (caller), read | the money / items flags, run inside 0x62B6 |
| 0x58D3 | exec | the award (`por_venue_oracle.py` award); `CombatRules.AwardExperience` matches |
| 0x557E | read, fixed | the share, read whole. Fixed: pool experience as signed longints (`CombatRules.PoolExperience`, was unsigned). Noted: the program names each item it copies to the treasure (START 0xFE2 writes the name field) before copying; Core copies the record as it is. The Client renames on every page, so nothing changes in play. A divisor of 0 halts the program (run-time error 200); Core returns 0 with a diagnostic, as before (`[?]` kept) |
| 0x7035 | read | Buy: the position starts at 1, B / Return, gold and price compared before the copy step, then the character pays (low word) or the pool (signed total, the remainder as one word); matches `ShopRules.Buy` / `Money.Pay` |
| 0x4636 | read | the temple pay question: yes/no 0x2D9A0, the same pay rule as Buy (`Money.Pay`, narrow); not enough → the message and the answer N; Y → START 0x2383 and the paid message |
| 0x6866 | read | the NPC share: the counts, cut = low byte of pool div total, the 16-bit product sign-extended; matches `FightRules.NpcTreasureShare` |
| 0x644B, 0x47C0, 0x4978, 0x4A87, 0x4CC8, 0x4DB1, 0x4E9F | exec (already) | already run by `por_venue_oracle.py` (loot, templeEntries) and compared in `VenueProgramTests`, but missing from `cite.py` ORACLE_O: register them |
| 0x458A | read | the "cure anyway" question: START 0x228C (member, text, 0, 0), the yes/no with an empty prompt (13 / 10 / 15), then START 0x2383 blanks rows 18–22 whatever the answer (the heal list blanks the box on its next pass anyway); matches `VenueScreens.Heal` |
| 0x4F4E | read | the heal list: ten names from DS:00F1 + 0x29·k, row 24 blanked, windows, then each pass the title, the box blanked, the list (2, 4)–(38, 15) with "Heal"; H or Return run entry [position] 0..8, row 9 and the list's 0 (Escape, E, 0 all give 0 from 0x2D669, 0x2D92B–0x2D947) leave, any other key loops; at the end the list freed, row 24 blanked, START 0x3379, the party list. Matches `VenueScreens.HealList` (the unwritten position word is already documented there) |
| 0x5280 | read | the temple menu: DS:49F3 = 1, START 0x3379, party list, pool and DS:6DC0 cleared; the two lines (checked byte for byte: "Heal View Take Pool Share Appraise Exit" / "Heal View Pool Appraise Exit"); H and P only with the extended cell 0, V / T / S / A / E / G / O compared without it; redraws after T (and B) and after A with its flag; the exit question as `MoneyScreens.StayForMoney` (s48 fix kept). Matches `VenueScreens.Temple`. Extended keys: see note E |
| 0x7212 | read | the shop menu: as the temple, plus the naming routine over the whole stock on entry; B compared without the extended cell (F8, scan 0x42, buys), P with it. Matches `VenueScreens.Shop`. Extended keys: see note E |
| 0x6D04 | read | Buy's page: a value of 0 becomes 1, each stock name is rewritten in place as the 30-cell price row (name over the right-aligned value), the list routine runs, then the naming routine renames every stock item (so the stored names come back). The Client builds the same rows (`PriceRow` over `StoredItemName`) without touching the records; nothing else reads those names |
| 0x5A47 | read | the after-combat results: every branch (fled scan, the defeated test with $6DE6, won, the not-in-play count, share and award, the not-fled / fled / defeated party loops, the duel branch) matches `AfterCombat.Results`. VM cells are words at $6B00 + offset / 2 ($6DC7 = +0x58E, $6DE3 = +0x5C6, $6DE6 = +0x5CC, $6E3E = +0x67C) |
| 0x5E94 | read | the summary: result choice (duel lost / duel won / party won / party fled / treasure found) and the share line match `VenueScreens.Summary`; the program zeroes the shown share for a lost duel and a party that fled, and otherwise prints DS:84D6 as left, which is stale only when no one won, no one fled and $6DE6 = 1 (not reachable in a normal fight). Core's share is 0 whenever no one won |
| 0x672A | read | the removal: $6DC8 := 0; every not-party or hostile creature leaves (counted when out of play; the party-size decrement only for party members), every other one loses its combat record; the current member := the head. Matches `AfterCombat.Cleanup` |
| 0x6AA5 | read | the top level: $6DC7 := 0, DS:443A := 0, DS:49F3 := 6, results, removal; defeated and not a duel: $6DC7 := 0x80 and the destroyed window (cursor DS:5E24 / 5E25 := 2 / 6, a presentation cursor); else the NPC share, summary, loot menu and the treasure list freed; then DS:4A00 := 1 and $6E70–$6E72, $6DE3, $6DE6 cleared. Matches `AfterCombat.Run` / `Finish` and the Client's caller |
| 0x24F1D | read | the amount input: row 24 blanked, the prompt, digits appended to a string[6] and checked with Val against the maximum (above it: the maximum's digits replace the input), Backspace, Return / Escape (Escape 0, else Val). Matches `TextRequest.Maximum`. Noted: the program's buffer holds 6 characters, the Client's 5, which differs only for a leading zero (typing "012345"); a seventh digit after a leading zero moves the cursor without changing the text (a display slip). Not changed |
| 0x2568A | read | drop coins: coin word and weight down, the pool up when DS:49F3 is 1 or 6. Matches `FightItemScreens.DropCoins` |
| 0x25974 | read, fixed | the coin take loop: the list of kinds whose pool longint is above 0 (signed), copper first; the position word 0 once; nothing picked or the list's 0 ends; the kind (0x2581C), the input (maximum the pool's low word: the Client's s48 / s50 caps stand), 0x25711, the list freed, DS:6DC0 and the loop while a kind is above 0. Fixed: the Client listed and looped on any non-zero kind, the program on kinds above 0 as signed longints (`MoneyScreens.Take`) |
| 0x21AED | read | the View menu: Items (list not nil), Spells (a byte 0x17–0x2B above 0), Trade (`MayDeal`, money, DS:49F3 ≠ 5), Drop (money), Exit; leaving on 0 or E (set CS:0A4C) or done; the page drawn again after I / S / T (CS:0A93); START 0x3379 at the end. Matches `FightItemScreens.ViewEntries` / `RedrawsViewPage` and `CharacterView.Run` |
| 0x23190 | read | the money Trade: the outer loop (START 0x3379, the pick from DS:471A, nobody → end) and the inner loop (page, money lines, coin list of DS:5D92 in (13, 7)–(25, 12), amount at most the held count, 0x250D5; with money left the inner loop again, with none the end; leaving the list goes back to the pick when a transfer cleared [bp−0x131]). Matches `CharacterView.TradeMoney`, including its `[?]` on the unwritten flag when the first list is left with no transfer |
| 0x234E1 | read | drop money: each pass the money lines, the coin list, the amount (at most held), 0x2568A, until nothing is picked or no coin is left. Matches `FightItemScreens.DropMoney` |

**Note E (extended keys in the shop and temple menus, for the user with question 1).** Both menus run the menu routine with the keypad flag and compare the returned byte without looking at the extended cell, except H / P (temple) and P (shop). So Shift-F3 (0x56) is View, Shift-F1 (0x54) is Take even with no money in the pool, Del (0x53) is Share, F7 (0x41) is Appraise, Home / End pick the member, and in the shop F8 (0x42) is Buy. The s51 decision covered only the loot menu. The Client gives these keys no meaning in the shop and temple. Not built.

## Proposed overrides.py entries

```python
'O:6184': dict(status='W', why='read whole s52; run under emulation (por_venue_oracle.py lootTake): empty list ends at once, screen redrawn'),
'O:62B6': dict(status='W', why='read whole s52; run under emulation (por_venue_oracle.py lootTake); exit test, redraws and Home / End fixed'),
'O:557E': dict(status='W', why='read whole s52: matches AfterCombat.Share; pool experience made signed longints'),
'O:7035': dict(status='W', why='read whole s52: matches ShopRules.Buy / Money.Pay'),
'O:4636': dict(status='W', why='read whole s52: matches Money.Pay (narrow) and the answer rule'),
'O:6866': dict(status='W', why='read whole s52: matches FightRules.NpcTreasureShare'),
'O:25C07': dict(status='W', why='read whole s52; runs inside 0x62B6 under por_venue_oracle.py lootTake'),
'O:458A': dict(status='W', why='read whole s52: matches VenueScreens.Heal'),
'O:4F4E': dict(status='W', why='read whole s52: matches VenueScreens.HealList'),
'O:5280': dict(status='W', why='read whole s52: matches VenueScreens.Temple (extended keys: note E)'),
'O:7212': dict(status='W', why='read whole s52: matches VenueScreens.Shop (extended keys: note E)'),
'O:6D04': dict(status='W', why='read whole s52: matches VenueScreens.Buy / PriceRow'),
'O:5A47': dict(status='W', why='read whole s52: matches AfterCombat.Results'),
'O:5E94': dict(status='W', why='read whole s52: matches VenueScreens.Summary'),
'O:672A': dict(status='W', why='read whole s52: matches AfterCombat.Cleanup'),
'O:6AA5': dict(status='W', why='read whole s52: matches AfterCombat.Run / Finish'),
'O:24F1D': dict(status='W', why='read whole s52: matches TextRequest.Maximum (6- vs 5-character buffer noted)'),
'O:2568A': dict(status='W', why='read whole s52: matches FightItemScreens.DropCoins'),
'O:25974': dict(status='W', why='read whole s52: coin kinds above 0 as signed longints fixed in MoneyScreens.Take'),
'O:21AED': dict(status='W', why='read whole s52: matches FightItemScreens.ViewEntries / CharacterView.Run'),
'O:23190': dict(status='W', why='read whole s52: matches CharacterView.TradeMoney'),
'O:234E1': dict(status='W', why='read whole s52: matches FightItemScreens.DropMoney'),
```

New oracle `e.call` targets for `cite.py` ORACLE_O: 0x62B6 'venue' (loot_take), 0x58D3 'venue' (award),
0x25C99 'trade' (treasure), 0x25191 'trade' and 0x252C3 'trade' (money), 0x25711 'trade' and 0x24E8C 'trade'
(take_coins), 0x2663B 'trade' (appraise). Already called but not registered: 0x644B 'venue' (loot), and the temple
entries 0x47C0, 0x4978, 0x4A87, 0x4CC8, 0x4DB1, 0x4E9F 'venue' (templeEntries, called through a variable).
