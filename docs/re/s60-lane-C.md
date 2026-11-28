# s60 lane C: DOS combat, spells, items and temple leftovers

Scope: the s49 / s51 / s52 [release] leftovers of PLAN.md "Next" in the fight, spell, item and temple code, and the
W-by-citation rows of the fight units. Every routine below was disassembled whole (capstone on the EXEPACK-expanded
image; GAME.OVR file offsets) and compared branch by branch with the C# that names it. Statuses as in the lane rules:
`seen`, `exec`, `read`, `fixed`, `bug?`, `open`.

## Items

| Item | Status | What was done |
|---|---|---|
| Cure 7's messages (0x29558) | fixed, seen | Read whole: with effect 36, 0x2BF6B says its text (unit CS:1078) by START 0x16EC(member, text, 10, 1) **before** removing the record, then 0x29558 says CS:251E by START 0x2B8B(member, text, 1) and stops; else the item walk zeroes byte 0x36 of the **first** cursed item only and says CS:252B by START 0x2B8B; nothing cursed, nothing said. Core `TempleRules.RemoveCurse` now raises them in that order (a callback through `TempleRules.Apply`); the Client shows them in the text box under the heal list after "is cured." (`VenueScreens.Heal`). The venue oracle already counted START 0x16EC and 0x2B8B per case; `VenueProgramTests.Temple_entries_match_the_program_under_emulation` now compares both counts for cure 7 too (100 cure-7 cases: 58 silent, 35 item, 7 effect 36), `ShopRulesTests.Remove_curse_clears_effect_36_or_the_first_cursed_item` the order. **Seen** (DOSBox-X and the Client `--art dos` on Xvfb, save G of `por_test_saves.py` with Thrender's `.SPC` + effect 36 and 20000 gold): after paying, "is cured." typed (name row 18 cyan, text row 19 green, pay line kept on row 24), the pause, the rows blanked, then "is Cured" (0x2BF6B) typed, the pause, then "is un-cursed" (0x29558), then the heal list again; the Client shows the same three texts in the same places and order (DOSBox-X's typing and pauses at 3000 cycles are longer: machine time). In DOS the heal list moves its row with Home / End only (Down falls through, `camp.md`). |
| Raise dead's hit-point rule | fixed (user s61) | Read whole (0x4B55–0x4CA1), see question 1: built as (c), not an option. |
| The AI's weapon pick and 0x2250A's messages | fixed | 0xBF8A calls 0x2250A five times (0xC352, 0xC3BE, 0xC449 / 0xC461, 0xC499, 0xC4B7); 0x2250A works on DS:5D92 (the AI creature) and shows its refusal by START 0x1693. Core's `MonsterAi.Toggle` dropped the result. Which refusals can come in an AI turn: only "already using" + the item in slot 0, when a cursed weapon that is not the choice keeps slot 0 (the readying branch is skipped), 0x100 > 2 and the off-hand item is cursed or missing (0xC449 readies the choice). Not "It's Cursed" (worth 0 never beats a start worth, so a cursed item is never chosen; the other calls skip cursed items), not "Wrong Class" (the pick filters by the same class-mask test), never hands-full (0x10F ≠ 0). Core raises `MonsterAi.ReadyNotice`; the Client shows it as the Items page's notice (`CombatChooser.Notice`, START 0x1693). Test `MonsterAiTests.Readying_says_already_using_when_a_cursed_weapon_keeps_the_slot`. The dead `MonsterAi.ToggleReady` removed. **Seen** (s60, second pass): save A + 4 orcs (`fight_save.py … 1C0B0004000400042400 --hp 200`), Bakshi's two-handed sword made cursed, a cursed shield readied and a dagger in his pack, 0x100 = 3; All-Quick (Alt+Q): on Bakshi's AI turn DOSBox-X shows "already using two-handed sword" on row 24 in green (every turn of his), the Client the same text in the same place. |
| 0x99FD draws before trigger 0x0F | fixed (read) | 0x99FD: trigger 7; initiative ≤ 0 → out; 20 → 19; DS:5D92; 0x319D2(c, 2, 1) (view, cursor box); START 0x1758; START 0x1583 (panel); trigger 0x0F; initiative > 0 → AI 0xABCF / menu 0x9B21; then the cell redraw 0x30A0F for every creature past the first test. Core raised `TurnStarted` after trigger 0x0F and only when initiative stayed above 0, and `TurnEnded` likewise; now `TurnStarted` comes after the recalculation and before trigger 0x0F, `TurnEnded` always (`Fight.Turn`). The Client's AI-start clears (row 24, then the message area: 0xABCF's 0x2CFCA and START 0x2383 at 0xABE2 / 0xABE7) moved from `TurnStarted` into `MonsterAi.Starting`, so they come after trigger 0x0F as in the program (and also when the menu's Quick hands over, as 0xABCF does). Not seen in DOSBox-X. |
| Handler 30's panel redraw | fixed (read) | 0xF749–0xF761: START 0x1583 when the coughing creature is DS:5D92. Core raises `EffectEvent.PanelRedrawn`, the Client draws the panel (`ShowPanel`). Test `HandlerMessageTests.Coughing_redraws_the_panel_only_for_the_current_creature`. |
| Ten effect-handler texts never shown (found on the way) | fixed (read) | Handlers 4 (0xEDFB), 7 (0xEEAF), 21 (0xF39A), 23 (0xF55D), 28 (0xF668), 30 (0xF6F0), 39 (0xF8E7), the missile dodge (0xFC5F), 43 (0xFD81) each say a text of their own by START 0x16EC(creature, text, 10, 1); the paralysing hit 0x10246 by START 0x2B8B(target, 1, text) at 0x10294 (no START 0x2383 after). Core raised an event at each (s52 lane B matched the sites), but neither the combat screen nor the out-of-fight message class drew any of them. Now both do (texts read from GAME.OVR, `PorTextCatalog`; our stand-in words in `CombatText`), `spells-effects.md` "Handler messages". The importer version is bumped to **0.7.41** (after the merge of main, which had 0.7.40) (the module's text table gained the ten keys; `ModuleResourcesTests` compares the module's texts with the folder's). Not seen in DOSBox-X. |
| Handler 135's text outside a fight | seen | DOSBox-X and the Client `--art dos` (save A, Darkstar given an unreadied item of type 66 with kind 0x3E = 0x87; 3D screen, View → Items, End, R): row 24 the strength refusal in green (START 0x1693) with every row drawn unlit, the item left unreadied, then the page's menu again; the Client equal. Also counts as the Items page's Ready seen from the 3D command line's View (one of the non-camp callers). |
| Items commands from the other callers | seen (loot), open (party menu) | Ready seen from the 3D View (above). The loot menu's View → Items (`por_test_saves.py` B=loot-full, the summary, V, I): the entries Ready Trade Drop Halve Join Exit (no Use, as DS:49F3 = 6), Ready toggling the flail to NO, Halve's refusal on row 24, Drop's yellow warning line on row 21 and the Drop yes/no, all four screens equal in DOSBox-X and the Client. The party menu's View → Items not run on screen. |
| The Client's Alt-X | seen | Client on Xvfb (`--save D --param` with the program's word, one step into the goblin fight, Alt+X at the first menu): the Alt-X line on row 24 in green, then the goblins gone, the cursor box gone and the continue-battle question; DOSBox-X (`START` + the word, the same save) shows the same sequence (its own random placement). Two small icons on the ground where goblins had stood in the Client are ground under them (no body is drawn by 0x1638E's path; DOSBox-X's placement differed, so not compared square for square) (s60 second pass: the same two icons show at the fight's start with the orcs, in DOSBox-X too: ground, settled). |
| Wand "Use it?" over the Items page | fixed, seen | The item's message is START 0x16EC, which redraws the text box's frame over the page; the Client now frames the box over the Items page (`FrameOverItemsPage` from `CampScreens.UseItem`) so the two combat-only lines show in it, and the party-menu screen's yes/no line now takes the caller's word colours (`ChoiceRequest.MenuColours`, 0x27D30's 13 / 15 / 10: Y green, ES white, NO green). DOSBox-X and the Client (save A + a wand of magic missiles and a 3-spell clerical scroll on Thrender, `fight_save.py --item`, camp View → Items, U): equal screens. |
| The scroll's combat-only lines framed | fixed, seen | New `TradeContext.ShowPageBare`: the scroll's list stays and the box's texts go straight over it, no frame and nothing blanked, until START 0x16EC frames it (`FrameTextBox(true)` ends the bare mode). DOSBox-X and the Client: the list, the two combat-only lines on rows 19–20 over it, "Use it?" on row 24, no frame, equal. |
| Party list over the heal list (handler 15's damage, a poison death) | read | Unreachable since the s51 fix. In the program it comes from cure 5's handler 22 clearing the lock (a second 15 removed unlocked takes a hit point, START 0x0767 at 0xF28A) or killing on a second 55 (0xECB3's end, START 0x0767). With the fixed `TempleRules.CurePoison` no 55 is left when a 22 runs and every removal is locked, so no handler 15 / 44 harms and nobody dies. The other entries: cure 0 removes 33 (stats only) unlocked; cure 1 removes 31, 34, 43, 44, 50, 57 under the lock (no re-add, so no harm and no heal: no START 0x0767 or 0x2EEA); cures 2–4 heal through 0x2C647 (the revive 0x2C753 draws no party list); cure 6 removes 32 and 55 under the lock (no death); cure 7 removes 36 (stats); cure 8 removes nothing. So nothing in the fixed engine draws the party list during the heal list; the program's redraw (the list drawn over the page, which the next pass redraws row by row so names and HP stay over its right half) is described in `shop-format.md` and not built. |

## Second pass (s60, after the merge of main)

| Item | Status | What was done |
|---|---|---|
| DS:4844 / DS:4845 start value | fixed (exec + seen) | Both lie past the end of START.EXE's expanded image (linear 0x11004 from the load segment; the image ends at 0x10BF0), in memory neither the EXEPACK unpacker nor the Turbo Pascal start-up (0AF8:0000: no fill) clears. The v1.3 unpacker run under unicorn on zeroed and on 0xFF-filled memory leaves **100 / 11** there both times (its own leftovers), and a DOSBox-X state at the party menu after a plain start holds 100 / 11 (`dosbox_state.py fight` → ds.bin). Core starts `SpellGlobals` with them (was 0); test `CampCastTests.Spell_globals_start_as_the_unpacker_leaves_them`. Reached by the first Enlarge at a casting level that leaves DS:4845 unwritten, and by Sleep / Dispel before any writer. |
| Handler 84's 0x3E58 residue | read | Frame arithmetic over the AI path (0xABCF BP 0x3EB6 → 0xB6FC 0x3E9A → 0x2B1E4 0x3E7A → 0x2B04A 0x3E6A → 0x2AEEA 0x3D74 → handler 0x3D64, DOSBox-X's BP): [bp−2] is SS:0x3D62. Earlier in the same turn the target search 0x15FDF (BP 0x3E94) calls the may-target test 0x13880 (BP 0x3E7A), whose triggers (class 1 on the target, class 2 on the attacker) put 0x2B04A at BP 0x3E58; when the creature carries an effect of that class 0x2B04A calls the dispatch 0x2AEEA, whose `push bp` writes 0x3E58 at SS:0x3D62. Nothing in 0xB6FC's own path writes that cell before the handler. Not run; when no class 1 / 2 effect is found the cell's value is older `[?]`. `spells-effects.md`. |
| Handler 23's text in DOSBox-X | seen (DOSBox-X) | Save A + 4 orcs, Brother Sean with two Spiritual Hammers memorised (`fight_save.py … --spells 4:28,28`): his cast "begins casting", and at his next turn "casts a spell", then "gains an item" (handler 23, 0xF55D) at row 10 of the panel, typed, the pause, the rows cleared: the text and placing the Client's `HandlerMessage` path gives (row 10, pause). The Client side of this scene was not reached on Xvfb (the scripted Quick presses ended the fight before Sean's second turn); the other nine texts not set up. |
| The cursor box on an orc after a step (s49 P2) | open | Not re-run (the `fight_frames.py` seed rig was not set up again). Read: the Client's box comes only from `TurnStarted` (0x319D2 with the cursor) and the aim; a step's first half clears it from the old square (`StepDrawing`), every view rebuild drops it (`Scroll`), and `TurnEnded` now clears it for every creature past the first test, so a box left on an orc needs a frame between the orc's `TurnStarted` and its first step, or a `TurnStarted` whose creature then has no turn (now possible when trigger 0x0F ends its actions, as in the program). |
| Items commands from the party menu's View | open | Not run on screen. |
| Handler 40's body branch, the AI caster | open | Not set up in DOSBox-X this session. |

## Not done (open)

- The cursor box on an orc after a step (s49 P2): not re-run (see the second-pass row).
- Handler 40's body branch and the AI caster in DOSBox-X.
- The party menu's View → Items on screen.
- Nine of the ten handler texts (all but 23) and 0x99FD's new order not watched in DOSBox-X; the AI notice is seen.
- The W-by-citation sweep of units 35, 38, 3B, 41, 68, 85, 8F, 96, AC, B0, 22, 25, 28: not started beyond the rows
  above (the ten handler texts were found while reading unit 41's display sites).

## Questions for the user

1. **Raise dead's hit points (temple 0x4A87 → 0x4B22–0x4C9D).** After the two effect removals the program sets HP 1,
   status 0, in play, and Con (0x14) − 1 when above 0. Then: d = max HP (0x32) − rolled HP (0xB1) when positive, else
   0 (the hit points the Constitution bonus added over the levels, plus any Modify change). **When the new Con is below
   14 the routine ends there (0x4BCE jumps to the exit): max HP is not touched.** Else s = Σ over the eight class
   levels 0x96 + k that are > 0: the fighter (k = 2) adds level × (Con − 14), any other class 2 × level when Con > 15,
   else level (Con 14 or 15); d := d div s when s > 0; max HP −= d, except when Con ≥ 17 and the fighter level ≤ 0.
   The level-up bonus the hall uses (0x1D8B0, DS:408E by Con: 3 → −2, 4–6 → −1, 7–14 → 0, 15 → +1, 16+ → +2, a
   fighter +1 more above 16 and above 17) shows what the rule seems to aim at: take off what one point of Con is
   worth. Worked cases (level 5, d = the bonus the old Con gave):

   | Character, Con old → new | d | s | Program's loss (d div s) | Bonus the new Con gives less |
   |---|---|---|---|---|
   | fighter 15 → 14 | 5 | 0 (no division) | 5 | 5 |
   | fighter 16 → 15 | 10 | 5 | 2 | 5 |
   | fighter 17 → 16 | 15 | 10 | 1 | 5 |
   | fighter 18 → 17 | 20 | 15 | 1 | 5 |
   | magic-user 15 → 14 | 5 | 5 (Con 14 counts as level) | 1 | 5 |
   | magic-user 16 → 15 | 10 | 5 | 2 | 5 |
   | magic-user 17 → 16 | 10 | 10 | 1 | 0 |
   | magic-user 18 → 17 | 10 | — (Con ≥ 17, no fighter level: skipped) | 0 | 0 |
   | any 14 → 13 | 0 | — (Con < 14: skipped) | 0 | 0 |

   So s is the bonus the **new** Con gives (except that Con 14 counts for non-fighters, where the hall gives 0), and
   d − s would be the bonus lost in every row but the magic-user's 15 → 14; the program divides instead, so the loss is
   1 or 2 points where it "should" be 5, except where s is 0. Options: (a) keep the program's division (parity, the
   current build); (b) take off d − s (what the shape suggests, with the Con 14 slip kept or also fixed); (c) take off
   the difference of the hall's own bonus (DS:408E) for old and new Con per level. **Decided (user s61): (c), fixed,
   not an option** (`shop-format.md` "Raise dead", `TempleRules.RaiseDeadLoss`; the venue oracle test expects the
   fixed max HP, every other byte as the program).

## Ledger evidence

- 0x29558 → W, exec (venue oracle START 0x16EC / 0x2B8B counts compared for cure 7; read whole s60: messages and their order)
- 0x2BF6B → W, exec (caller; read whole s60: the message before the removal)
- 0x4DB1 → W, exec (cure 7's messages now raised and compared)
- 0x4A87 → W, read (s60: the max-HP rule read whole; s61: fixed as (c), user decision)
- 0xBF8A → W, read (s60: 0x2250A's refusal messages shown; only "already using" reachable)
- 0x2250A → W (s60: its messages in the AI's calls too)
- 0x99FD → W, read (s60: the view and panel before trigger 0x0F, the cell redraw for every creature past the first test, ported)
- 0xABCF → W (s60: its start's row-24 and message-area clears after trigger 0x0F in the Client)
- 0xF6C0 → W, exec (handler 30: the panel redraw raised, `PanelRedrawn`; its text shown)
- 0x11D8F (handler 135) → seen outside a fight (s60)
- 0x1638E (Alt-X) → seen in the Client (s60)
- 0x222F0 / 0x22AC4 / 0x27C38 (item Use outside a fight) → seen (s60: the wand's framed box and yes/no colours, the scroll's bare lines fixed)
- 0xEDDA, 0xEE8E, 0xF36A, 0xF43B, 0xF61B, 0xF8A6, 0xFC07, 0xFD39, 0x10246 → W, exec (s60: their own texts now shown by the Client, in and out of a fight)
- 0x10AD2 (handler 84) → read (s60: the range residue's writer traced by frame arithmetic: 0x2AEEA's saved BP from the may-target test 0x13880's trigger)
- 0xBF8A → seen (s60: "already using" on an AI turn in DOSBox-X and the Client)
- 0xF43B (handler 23) → seen in DOSBox-X (s60: "gains an item" at row 10)
- 0x6605 / the loot menu's View → Items (0x21F32 with DS:49F3 = 6) → seen (s60: Ready, Halve's refusal, Drop's question)
- DS:4844 / DS:4845 (spell globals; writers 0x282BD, 0x28533, 0x29299) → start values 100 / 11 (s60: EXEPACK leftovers, emulated and seen in a DOSBox-X state)
