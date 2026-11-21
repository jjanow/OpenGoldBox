# Effects and spells (Pool of Radiance, DOS v1.3)

The effect engine (triggers, handlers, saves, non-weapon damage, healing, terrain) and casting in a fight. Read
from the program in session 12 (2026-11-17); addresses are GAME.OVR file offsets unless marked START (offsets in
the unpacked `START.EXE` image). Far calls resolve through the overlay stubs (`ecl-format.md` "Program
references"). Status marks: **P** read in the program, **D** checked against DOSBox-X, `[?]` not settled. The
reconstruction (talonius) was used only to find routines; every rule below is from the disassembly. Game messages
are paraphrased; the code raises `EffectEvent`s instead of printing.

Code: `src/GoldBox.Core/Game/EffectHandlers*.cs` (`IFightEffects`, the DAMAGE hook), `FightSpells.cs`
(`IFightSpells`), `src/GoldBox.Data/Por/PorSpellData.cs` (tables read from the player's program).

## Data-segment cells

| DS | What |
|---|---|
| 5D92 | current character (the attacker, the caster) |
| 6816 | saving-throw roll being made |
| 6817 | effect being added (an immunity handler clears it) |
| 6818 | damage being dealt (byte) |
| 6819 | element mask of that damage (bit 0 fire, 1 cold, 2 electricity, 3 magic, 4 acid/other; spells pass 8, 9, 0x0C) |
| 681A / 681D | rate being computed (attacks or movement, halves) / 1 while it is the movement |
| 681B | spell being cast (0 none) |
| 681C | dice count of the last damage roll (0x2BD18) |
| 681E | "may not be targeted" (invisible, blinking) |
| 681F | effect lock (handlers do not re-add effects) |
| 6820 | the spell has several targets |
| 6822 | attack roll (signed byte; natural 20 = 100) |
| 6823 | hits made in phase 1 |
| 6825 | morale being checked |
| 6826 | "other" creature a handler looks at (usually combat record +0x0A of the creature) |
| 682A | save type being rolled |
| 6C94 / 6C95 + 4·(i−1) | spell target list: count / far pointers (nil = dropped) |
| 6DBF | an item is being used |
| 6828 + 4·id | effect handler table (ids 1..127 effects, 128..139 item codes; built at 0x11DFD, except id 88 = DS:6988, written by the spell table builder at 0x2A7B9) |
| 6B84 + 4·id | spell routine table (id 0 = target pick: 0x27AA9 outside a fight, 0x148BE in one; built at 0x2A42B) |
| 6DB9 / 6DBA | the aimed square (the caster's at the pick start, then the last aim; Lightning Bolt's rebound moves it) |
| 6DBB | stinking-cloud list (19-byte GetMem nodes, see spell 34 / handler 40) |
| 4845 | Enlarge's exceptional Strength; Dispel Magic writes each examined effect's level (param AND 0x0F) there |
| 409B + 0x38·class + 4·level | experience table (8 classes × 14 signed dwords; levels 0/1 are other data) |

## Triggers — P

**Dispatcher** (0x2B1E4, class byte, creature): a compare chain on the class; each class runs a fixed list of
effect ids, in this order, each through 0x2B04A:

| Class | Called from | Ids |
|---|---|---|
| 0 | target veto (attacker) | 126 |
| 1 | target veto (target) | 63, 37, 25, 71 |
| 2 | a hit in phase 1 (attacker) | 85, 86, 87, 68, 79, 80, 76, 73, 77 |
| 3 | a hit in phase 2 (attacker) | 64–70, 79, 85, 86, 87 |
| 4 | weapon damage, attacker | 29, 3, 6 |
| 5 | weapon damage, defender | 28, 41, 104, 120, 101, 115, 116, 119, 123, 96, 94, 60, 122, 117 |
| 6 | non-weapon damage (0x2C22E) | 113, 61, 122, 60, 91, 10, 20, 105, 106, 112, 114, 118, 17, 93, 101, 28 |
| 7 | turn start (0x99FD) | 51, 52, 53, 31 |
| 8 | fight start | 99, 81, 82, 89, 72, 56 |
| 9 | effect being added (0x2C540) | 105–112, 124, 125 |
| 0x0A | attack roll, attacker | 1, 2, 33, 36, 49, 3, 6, 18, 26 |
| 0x0B | defender's AC | 33, 17, 8, 9, 45, 46, 30 |
| 0x0C | saving throw | 8, 9, 10, 17, 20, 33, 36, 45, 46, 49, 61, 111, 125, 90, 97 |
| 0x0D | HP reached 0 | 99, 100, 103, 75, 74 |
| 0x0E | monster special (AI) | 83, 84, 88, 121 |
| 0x0F | action start | 21, 30, 74, 75, 11 |
| 0x10 | attack roll, defender | 25, 71, 37, 47, 48, 89 |
| 0x11 | morale | 1, 2, 11 |
| 0x12 | attack / movement rate | 39, 42, 58 |
| 0x13 | end of round | 98, 23, 72, 56, 11 |

**One effect** (0x2B04A): when the creature carries the id, its **first** record (START 0x2D39) goes to the handler
(two Blesses give +1, not +2). Otherwise, for the four aura ids in the set constant at unit 0xB0 CS:0x140 — 21, 45,
46, 49 — the creature list is walked from its head while nothing is found: outside a fight any carrier counts; in a
fight (DS:49F3 = 5) the program builds the target list around the carrier (0x3045B: carrier square 0x31321 /
0x31349, range 6 for Prayer 49 else 1, carrier icon size 0x31371) and looks for the creature's combat slot
(0x31399) in it, saving and restoring the target list DS:671B around the build. The handler then runs on the
creature with the **carrier's** record. Handler call 0x2AEEA: (id, creature, record, removed).

**Removal** (0x2AF10, creature, id, record or nil): nil → the first record with the id; none → nothing. When the
record's byte 4 is set the handler runs with "removed" **before** unlinking; then it is unlinked and freed.
`FightRules.RemoveFirstEffect` unlinks first and then calls `IFightEffects.Removed`; the handler only runs when byte 4
is set (`[?]` the order differs for handlers that look at the list).

**Add** (0x2BD3C): a new record at the **end** of the list: id, minutes, param, byte 4 = "removal runs the handler".
**Add with message** (0x2C540, creature, id, minutes, param, removal flag, "save negates", saved, message): DS:6817 =
id, class 9 runs; unaffected (message only) when DS:6817 was cleared or (saved and "save negates" = 1). Else an
existing record with the id leaves first when it has minutes left and the new duration is longer, or the new one is
permanent (0); otherwise both stay. Then the record is added and the message shows.

**Dropped creature** (0x2BEF4): the first record of each id in DS:0C14 (7, 11, 30, 31, 32, 51, 52, 53, 54, 58, 59,
95, 98, 137, 74, 75) is removed. **Invisibility** (0x2BEBC): **every** 0x19 record is removed, one after the other
(`FightRules.AttackRoll` removes only the first `[?]` — the handlers' own path removes all).

## Rolls, damage, healing — P

**Saving throw** (0x2BC4D, creature, type, modifier): 1d20 into DS:6816; 1 fails, 20 saves; else DS:6816 = roll +
0x101 + modifier (byte sum, signed parts), DS:682A = type, class 0x0C runs, and it saves when the save byte 0x6D +
type ≤ DS:6816 (unsigned). One draw.

**Non-weapon damage** (0x2C22E, creature, damage, reduction, saved): DS:6818 = damage, class 6 runs; when saved,
reduction 1 cancels and 2 halves (shift); nothing at 0. Else (message) START 0x2DC8 applies it (`FightRules.InflictDamage`;
in a fight the side's team count drops). In a fight the target's cast allowed (+1) = 0, and a spell it was
casting (+0 > 0) is lost: message, START 0x202B removes **the current character's** first memorised copy of that id
(DS:5D92, not the target's — the attacker or caster, a quirk), +0 = 0. A target now out of play in a fight: the
dropped-creature removal, class 0x0D, and still out of play it leaves the map (0x3154B). A target still in play
(0x2C44D) jumps to the end 0x2C527: START 0x17E3 clears the message area (read s29; **D**: a lightning bolt's damage
message gone before the bolt's next flight). The message itself goes through START 0x2B8B, which copies its text into
a string[40] first (0x2B9B): a longer line is cut at 40 characters (**D** s29: a lightning damage line with the
damage type's word, shown to its 40th character). Client: `EffectEvent.DamageDone`, the cut in `CombatChooser.EffectMessage`.

**Status** (0xECB3, creature, status, message): message; a creature already dead, stone or gone (6, 7, 8) is left;
else status, out of play, HP 0, the dropped-creature removal, class 0x0D, and still out of play it leaves the map
(0x3154B, called in and out of a fight); then PauseOnly, START 0x17E3 and, outside a fight, START 0x0767 (s29).
Team counts are not touched (the next count does it). Callers, texts and display: "Status changes" below.

**Healing** (0x2C647, creature, amount, bypass): only for status 0, 1, 4, 5 (set at CS:0x173D). Below full HP with
bypass: healed. Otherwise rot (0x32) blocks it, and so does bypass at full HP. HP + amount (byte), at most 0x32; an
out-of-play creature that is dying becomes unconscious, and an unconscious one outside a fight runs handler 0x4E
(removed, no record). START 0x2EEA (00BA:234A, after the heals of handlers 50 and 62 and Cure Light Wounds 0x28083):
the fully-healed words when HP 0x11B equals the maximum 0x32 (00BA:2353), else the partially-healed words (START
strings 00BA:2326 / 00BA:2336), through START 0x16EC with the pause flag, then outside a fight (DS:49F3 ≠ 5) the
party list (00BA:23AE). **D** (s39, a test save: Rhiannon at 2 of 7 HP carrying 0x3E for 6 minutes with the removal
call, Thrender carrying 0x0F for 11 minutes, Darkstar revived by 0x4E; camp Rest 15 minutes): the revive, then
Rhiannon's name and "partially healed" typed, the pause, the list drawn again (Rhiannon 3 HP, Darkstar back to cyan),
then Thrender's 1 point of damage from magic and the list again (10 HP); then Brother Sean's Cure Light Wounds on
Phineas (5 of 6): his name and "fully healed", the pause, the list (Phineas 6 in green). Client:
`OutsideEffectMessages.Healed` (texts `CombatMsg.FullyHealed` / `PartiallyHealed`, importer 0.7.27); the rest run
has 136 of 136 frames equal (help lines and camp picture masked) since the party list shows the values of its last
drawing (view3d.md "Where the party list is drawn"), and the Cure Light Wounds run (Magic, Cast, the spell, START's
member pick End, End, S) 43 of 43 with the program's pick (camp.md "Cast's target"). In a fight START 0x2EEA prints
through START 0x16EC from row 10 with the pause and draws no list (DS:49F3 = 5): Client `CombatChooser.HealedWords`
for handlers 50 / 62 and Cure Light Wounds (not taken in DOSBox-X in a fight `[?]`).

**Terrain** (0x2B9D1, end of round after class 0x13, per creature in play): the terrain of its own square (0x313F3,
direction 8) = 0x1E (stinking cloud) and none of 0x1E, 0x1F, 0x20, 0x6F, 0x7D: save vs. type 0 (no modifier); saved
→ coughing 0x1E for 1 minute; failed → helpless 0x1F for **1d4 + 1** minutes (rolled after the save); both through
0x2C540 (param 255, no removal call, save does not negate), then their handler runs. `FightRules.EndOfRoundCreature`
calls it through `IFightEffects.Terrain` (added, default empty).

**Dice** (0x2BCCD): count × (Random(sides) + 1) summed as a byte, count 0 → 0. 0x2BD18 also stores the count in
DS:681C.

## Handlers 1–72 — P

Handler arguments: the creature (the one the class runs on), its record (or the aura carrier's), removed. "Target" =
the creature's combat record +0x0A. Ids 5, 13, 19, 24, 27, 60, 63, 92, 127 share an empty handler (0x11DF6); 16, 54,
55 have empty handlers of their own.

| Id | Address | Rule (draws) |
|---|---|---|
| 1 Bless | 0xED68 | morale + 5, attack + 1 |
| 2 Curse | 0xED78 | morale − 5 (not below 0), attack − 1 |
| 3 | 0xED96 | target undead (0x9F = 4): attack + 2, damage + 2 |
| 6 | 0xEE1E | by target type: 0x0A → 1, 9 or 0x0C → 2, 4 → 3, else 0, added to attack and damage; element 9 |
| 8/45, 9/46 | 0xEFD2, 0xF007 | protection: current character's alignment 2/5/8 (evil) resp. 0/3/6 (good) → save + 2, attack − 2 |
| 10, 20 | 0xF03C, 0xF33F | resist cold / fire: element bit 1 / bit 0 → damage halved, save + 3 |
| 11 Charm | 0xF05B | removed: hostile = param bit 6, NPC byte 0xB3 → 0. Else the first time (param bit 5 clear): param += 0x20 + hostile × 64, hostile = param bit 7, computer-controlled, NPC byte 0xB3 unless > 0x7F, no target, teams counted; morale = 100 |
| 14 Friends | 0xF231 | Charisma 0x15 = param |
| 15 | 0xF246 | re-added for 10 minutes (lock permitting); HP > 1 → 1 point non-weapon damage, element 0 |
| 17 Shield | 0xF2A9 | AC byte at least 0x39; save + 1; spell 15 (Magic Missile) → damage 0 |
| 18 | 0xF2D4 | target type 1, size 1, named in a 4-name list (DS:0332 + 16·i) → attack + 1 per match |
| 21 Silence | 0xF36A | (message when item use was allowed) item use and casting not allowed this round |
| 22 | 0xF3D0 | poisoned (0x37) → dies (status 6); 0x0F removed under the lock |
| 23 Spiritual Hammer | 0xF43B | find the hammer item (type 0x14, word 0x31 = 0xF3). Removed: it goes. Else without it and < 16 items: a new one (plus 1, props 23 / 137); stats recalculated |
| 25 Invisible | 0xF573 | no target unless the current character sees invisible (0x18); attack − 4 |
| 26 | 0xF5A2 | as 18 with an 8-slot list (DS:0372 + 16·i; the program reads 8 slots of a shorter list) |
| 28 Mirror Image | 0xF61B | **1d(param + 1)** always; > 1 with a spell being cast and one target → damage and effect nullified, an image lost (param − 1; at 0 the effect goes). DS:681B is written only by the cast (0x27EC7 / 0x27ED9) and handler 84 (0x10BCB), so as read a weapon hit is never absorbed (DOSBox `fight_mirror`, s13: 1d5 rolled, images stay at 4) |
| 29 | 0xF698 | damage − damage ÷ 4 |
| 30 coughing | 0xF6C0 | item use and casting not allowed; stats recalculated; rear AC byte − 2 (at most down to 0x32), AC = rear AC |
| 31, 51–53 | 0xFF9A | helpless, snake charm, paralysis, sleep: actions end (START 0x31D7) |
| 32 Animate Dead | 0xF776 | byte 4 cleared; unless locked the creature dies (0xECB3); hostile = param ÷ 16, computer-controlled, 0x76 = 0, 0x6B = fighter level, base movement 12, NPC 0xB3 → 0, type 0 |
| 33 Blind | 0xF807 | attack − 4, AC and rear AC − 4, save − 4 |
| 34 | 0xF82A | handlers 43 and 44 |
| 35 | 0xF861 | save + 1, attack + 1 |
| 36 Bestow Curse | 0xF870 | attack − 4, save − 4 |
| 37 Blink | 0xF881 | initiative > 0 → no target, attack roll −1 |
| 39 Haste | 0xF8A6 | first time (param bit 4 clear): param + 0x10, (message) ages one year (word 0x30); rate × 2 |
| 41 | 0xFCE1 | the current character's missile (or weapon) with plus 0 → missile dodge 100 % |
| 42 Slow | 0xFD1C | rate ÷ 2 |
| 43 | 0xFD39 | re-added for 60 minutes; Str > 3 → Str − 1 (message), else helpless (0x1F) when not yet |
| 44 | 0xFDC3 | re-added for 10 minutes; HP > 1 → 1 point, else helpless when not yet |
| 47 | 0xFE54 | current character type 1, size AND 0x7F = 2; the carrier's target named in an 8-slot list (DS:03F2) → attack − 4 per match |
| 48 | 0xFED1 | current character type 1 named as one of two names in the code segment → attack − 4 |
| 49 Prayer | 0xFF0D | carrier's side (param bit 4) = the creature's → handler 35; else attack − 1, save − 1 |
| 50 | 0xFF56 | re-added for 14400 minutes; heal 1 with bypass |
| 56 | 0xFFBE | invisibility 0x19 for 1 minute, param 12 |
| 57 | 0xFFF4 | unless locked: Charisma − 2 (floor 0, then at least 3); param ÷ 16 > 1 → param − 0x10 and 0x39 re-added for 43200 minutes; else status 8 |
| 58 | 0x100AD | movement +6 = 0; while the movement is computed the rate is 0 |
| 59 | 0x100CD | adds regeneration 0x62 (permanent) |
| 61 | 0x100EB | element bit 0: damage − 2 per die (DS:681C), not below the dice count; save + 4; without element bit 3 nullified |
| 62 | 0x10146 | re-added for 60 minutes; heal 1 with bypass |
| 64, 65, 66, 70 | 0x101A0 | poison: the target saves vs. type 0 with modifier 0, +4, +2, −2; failing: poisoned (0x37) and dead |
| 67, 68, 69 | 0x10246 | paralysis: the target saves vs. type 0 (**modifier 0**: the −2 handler 69 passes is not used); failing: 0x34 for 2d8 / 63 / 1d9 + 10 minutes, param 12. 68 skips a target with race byte 2. The minutes are rolled **before** the save |
| 71 | 0x1037A | no target, attack − 4 |
| 72 | 0x1038B | 1d100 ≤ 95 → invisibility 0x19 for 1 minute |
| 40 | 0xF91E | stinking cloud cells of the arena `[?]` (map) |

**Missile dodge** (0xFC07, chance): the current character has a weapon and stands more than 1 square away (START
0x30FB): 1d100 ≤ chance → (message) damage 0, attack roll −1, DS:6823 − 1. The missile (0xFC78): the ammunition
START 0x3A2B finds, else the weapon.

**Name lists**: handlers 18, 26, 47, 48 compare the target's (or current character's) name with monster names in
the program's data; `PorSpellData.Names` reads them from the player's program.

## Handlers 73–139 — P

| Id | Address | Rule (draws) |
|---|---|---|
| 4 | 0xEDDA | (message) effect 7 for 43200 minutes, param 255, removal on |
| 7 | 0xEE8E | Con + 1; at ≥ 20 adds 0x3E (60 min, removal on) and stops. From Con 15: gain = (max HP − rolled HP 0xB1) ÷ Σ per class level (fighter: level × (Con − 15); others level × 2 above 15, level at 15; 0 → 1); Con < 17 or fighter levels → max HP and HP + gain |
| 12/38 | 0xF115 | Enlarge / Strength: param > 0x7F nothing; Str/Exc from the record's code (0x2BFF7: ≤ 101 → 18/(code − 1), else code − 100); the best other 12/38 record takes the current code (0x2BFCE) and the creature takes that best value |
| 78 | 0x10634 | revive with current HP (0x2C753, its message: "Revive" below); failing, re-add 0x4E for 1 minute (lock) |
| 79 | 0x10674 | element 9; target takes 2d10 non-weapon damage (count to 681C) |
| 80 | 0x106A7 | element 0x10; target takes 1d4 |
| 87 | 0x11047 | target without rot gets 0x32 (14400 min) and 0x39 (43200 min, param 1d6 × 16 + 15), removal on (1d6); then its text on the target (s30, "Handler messages") |
| 89 | 0x110EB | round 0 and attack roll 0 → param AND 0x0F; else first attack (bit 4 clear) → attack roll − 1, bit 4 set |
| 90 / 97 | 0x11134 / 0x112E5 | save type 0 resp. 4 or 2 → save + Constitution bonus (4–6: 1, 7–10: 2, 11–13: 3, 14–17: 4, 18–20: 5; else an uninitialised byte `[?]`) |
| 91 | 0x111B1 | spell 15 → nullified (message); else element bit 2 → nullified |
| 93, 114, 118 | 0x111FD, 0x116C8, 0x117BE | element bit 0 / 2 / 1 → damage ÷ 2 |
| 94, 115 | 0x11217, 0x116E2 | attacker's missile with base byte 7 AND 0x81 (resp. byte 7 = 0 or bit 0) → damage ÷ 2 |
| 95 | 0x1126B | byte 4 cleared; in play → dies |
| 96, 119, 123 | 0x112A6, 0x117D8, 0x119E3 | need magic / silver weapons: damage 0 (or ÷ 2 for silver in 123) by the missile's plus and word 0x31 |
| 98 | 0x1135B | HP + 3, at most max |
| 99 | 0x1138B | dying with +0x0E < 6 → n = 6 − it; unconscious → 6; revive with n HP → 0x5F for 1d4 + 1 min, then this record removed (1d4 after the revive). Carried only by the wild boar (MON6SPC / MON8SPC block 120, permanent, s30) |
| 100 | 0x11434 | element bits 0 and 4 clear → 0x66 for 3d6 minutes (removal on) |
| 101 | 0x1146F | no 0x62 and no 0x3B → 0x3B for 3 minutes (removal on) |
| 102 | 0x114C1 | revive with max HP; failing, re-add 0x66 for 1 minute (lock) |
| 103 | 0x1151A | byte 4 cleared; 0x319D2(creature, 3, 0); out of play, status 3, with its own text (0x2BDF0, s30: "Revive and put out of play") |
| 104 | 0x1155C | missile dodge 60 % |
| 105/106 | 0x115D1 / 0x115DF | magic resistance: chance = p − 5 × (11 − casting level of DS:681B), byte (p = 50 / 15); with DS:6817 ≠ 0 or element bit 3, 1d100 ≤ chance → nullified |
| 107 | 0x115ED | 1d100 ≤ 90 → sleep and charm nullified |
| 108–112, 124, 125 | 0x11611… | immunities (charm/sleep, paralysis, cold, poison+paralysis with save roll 100 for type 0, fire; 30 % half-elf) |
| 113 | 0x11683 | element bit 0 → damage − 1 per die, not below the dice count |
| 116 | 0x11741 | attacker's missile with plus > 0 → ÷ 2 |
| 117 | 0x1177B | attacker's weapon type 0x55 → damage = 1d6 + 1 (count 1 to 681C) |
| 120 | 0x11823 | attacker's weapon type 0x57/0x58 → missile dodge 50 % |
| 122 | 0x11993 | weapon type 0x56 → damage = 3d8; then element AND 9 → damage + DS:681C |
| 126 | 0x11A92 | the target carries a readied item with name word 0x98 (and word 0x31 = 0xFC) or 0x76 → no target |
| 128–130, 133, 134, 136, 138, 139 | 0x11B35 | item ready → effect 0x3D of the item (permanent, param 12) and its handler; unready → that effect removed |
| 131 | 0x11BB2 | gauntlets: raise to 18/00 (0x2C046), DS:6821 = 1, effect 0x26 with the returned code |
| 132 | 0x11D00 | wrong alignment → unreadied, element 8, non-weapon damage 0x3D ÷ 16 |
| 135 | 0x11D8F | Str < 19 → unreadied, its text through START 0x1693 (row 24; s30) |
| 137 | 0x11DD5 | removed → effect 0x17 removed |
| 40 | 0xF91E | (only as a removal call) the caster's cloud numbered param ÷ 16 (first match in DS:6DBB); none → nothing. (Message) each clouded cell gets 0x1F when a body (DS:66D6 + 7k, k ≤ DS:6715, pointer set) lies on it, else the terrain it covered; the node is unlinked and freed; every remaining cloud's cells are set to 0x1E again |
| 73 | 0x161CD | rake: DS:6823 = 2 hits in phase 1 and the last target in play → combat +4 = 2, 0x114 = 2, 0x116 = 2, 0x118 = 4, 0x113 = 0 (two 2d4 attacks in phase 2), message, MakeAttack (0x1407F, defender turns), actions end |
| 74 | 0x16281 | hold (from 77), victim = DS:65B9[param]: removed or either out of play → victim's 0x3A goes, and unless removed this record goes (byte 4 cleared first). Else one 3d4 attack (0x113 = 1, 0x114 = 0, 0x115 = 3, 0x117 = 4), MakeAttack with the defender not turning, actions end; victim out of play → the record goes |
| 75 | 0x103CA | blood drain (from 76), victim = DS:65B9[param]: removed or either out of play → victim's 0x3A goes, unless removed the record goes (byte 4 cleared), and with both in play combat +0x10 = 1. Else (message) 1d4 (count to 681C), element 0, non-weapon damage, actions end; victim in play and minutes ÷ 16 > damage → minutes − 16 × damage, else the record goes |
| 76 | 0x10510 | attach: 0x4B on the creature (207 minutes, param = target's slot 0x31399, removal on); 0x3A on the target (permanent, param 255) and handler 58 on it |
| 77 | 0x10590 | target Str < 19: 0x4A on the creature (permanent, slot, removal on), 0x3A on the target, handler 58, message |
| 81 | 0x10705 | fight start: every creature in play on the other side (START 0x2F57) saves vs. 4 (one draw each); hit dice 0x73 0..3 → paralysis 0x34, 4–5 → Curse 2; permanent, param 12, save negates |
| 82 | 0x10803 | fight start: each creature's first 0x52 goes (the carrier's too); in play on the other side: save vs. 0 with +1 when DS:6814 + its side ≥ 6 and +2 for race 7, then 0x34 for 1d4 min (after the save), param 12, save negates |
| 83 | 0x10908 | gaze at the last target (message: gazer, row 10, no pause); gazer with 0x7F and a target's readied item with name word 0x76 (bytes 0x2F–0x31) → reflected (message: target, row 12, no pause), the gazer is the victim; victim fails a save vs. 1 → stone (0xECB3, status 7, its message: victim, row 10, no pause). **D** (s27, a medusa, MON5 49): the gaze message, the flight and the stone message on a member with no mirror; on Rhiannon with word 0x76 only on her armour and shield (not on the sword, her first readied item) the reflect message and the flight back, so any readied item counts (Core walks them all); the medusa then resisted |
| 84 | 0x10AD2 | charm gaze: target check 0x13880 and sight 0x2FF74 (`[?]` range = an uninitialised stack byte; 255 taken); message; DS:681B = 10 and **left so** (a later weapon hit on a Mirror Image creature then counts as a spell, see 28); save vs. 4 with −2; charm 0x0B (permanent, param = hostile × 128 + 12, removal on, save negates); charm handler on it. Its own message (gazer, row 10, no pause) comes after the sight check and before the flight |
| 85, 86 | 0x10FFF, 0x1101D | level drain (0x10D7D) of 1 / 2 on the last target: animated → gone (status 8), max HP 0. Else per level unless gone: 0x74 + 1; n = max HP ÷ Σ class levels (0 → divide error `[?]`); n × (max HP, 0xB1, HP − 1 floor 0; 0x75 + 1); 0x73 < 2 → gone, max HP 0, and draining 2 zeroes the top class's level; else the top class (0x10C5D: highest level, threshold ≥, last wins) − 1, HP 0 → dead, level stats 0x2AA87. Then 0x73 = top class level, XP 0xAC = its threshold (DS:409B, 0 for level ≤ 1), recalculation START 0x1758 |
| 88 | 0x2A0F1 | lightning breath (read s26; the entry DS:6988 is written at 0x2A7B9 by the spell table builder 0x2A42B, not by 0x11DFD; MON5 id 66 carries it with byte 3 = 0xFF): round counter DS:6DE3 ≠ 0 and 1d100 ≤ 50 → nothing. Else message (CS:0x30CF, START 0x16EC from row 10 with the pause), the target pick DS:6B84 (0x148BE) for spell 0x33 with ai = 1 (its picked flag DS:70C6 not read); the aim DS:6DB9/6DBA := the breather's square + sign(aim − square) (0x2FB52) per axis, then + 1 more on an axis where it is square + 1 (so two squares toward +x / +y, one toward −x / −y, as read); invisibility off (0x2BEBC); the moving icon 0x257E(cell 0x13), breather → aim, 4 frames, 50 ms (0x2A1E4); bolt hit 0x298D4 on the aim and the bolt 0x29973 (length 10, no rebound bonus), damage = the breather's byte 0x32, save type 3; byte 3 > 0xFD → byte 3 − 1, else the record goes (so 0xFF breathes three times); actions end (START 0x2637). When the pick aims nowhere the aim stays on the breather's square: the flight draws nothing, 0x298D4 hits the breather's own slot and the bolt does nothing (as read, `[?]` not seen). **D** (s27, DOSBox-X): Tyranithraxus breathed on his first turn of round 0 (message, the pause, the flight to the nudged aim, the bolt's flights, 80 damage to the first member hit), the Client matched it to the next menu with the same seed. Its fear aura 81 ran at the fight start before it: the paralysis / unaffected messages on the combat screen, one per party member with the effect frames. Core: `EffectHandlers.LightningBreath`, `FightSpells.LightningBreath` |
| 121 | 0x11877 | acid breath: 1d100 ≤ 25 and the target nearer than 4 (START 0x30FB): actions end, message (breather, row 10, with the pause), 8d4 (count to 681C), save vs. 3 (after the dice), non-weapon damage halved when saved (element left as it was); this record and the first 0x50 go |

## The stat recalculation — `[?]`

The program recalculates a creature's derived bytes (START 0x1758) right before class 0x0B (the attack routine and
the spell resolution) and inside handlers 23 and 30; handlers 30 and 33 then lower the AC bytes. The recalculation is
modelled (`StatRecalc`, set by `FightKit`); without it `EffectHandlers.Recalculate` restores the THAC0 / AC bytes (0x110–0x112) the creature had
when the handlers first saw it (for tests with hand-made records).

## DAMAGE (ECL 0x2E)

`EclPartyRules.EffectTrigger` runs class 0x0C (save cell DS:6816, save type DS:682A from `LastSaveType`) and 0x10
(attack roll DS:6822) through `EffectHandlers.EclTrigger` outside a fight (auras from any carrier, the current
character = the party's current one). Wired in `EclVm` (`Effects`).

## Spells — P

**Table** (16 bytes at DS:31FA + 16·id, ids 1..67; found through START 0x3262): class (0 cleric, 1 magic-user,
2 item), level, range fixed (255 = attack roll), range per level, duration fixed, per level, area (low nibble mode,
radius bits), target mode (outside a fight), save (0 none; also the non-weapon reduction and the "save negates" flag),
save type, effect id, when (0 not in a fight), casting time, AI priority, offensive, AI radius.

**Casting level** (START 0x3262): class 0 → current character's cleric level 0x96, 1 → magic-user level 0x9B, 2 →
12; while an item is used (DS:6DBF) any class but 2 → 6. `[?]` another class leaves it unset.
**Range** (0x27741): fixed + per level × level (byte), with an item per level × 6 + fixed; 0 with an area → 1; 255 → 1.
**Duration** (0x277EE): spell 40 → 1d6 × 10; 57, 61 → 5d4; 59 → 1d4 × 10 + 40; 63 → in a fight 2d10 × 10, else
(1d10 + 10) × 10; 67 → 1440; else fixed + per level × level.

**Combat cast** (0x14C0F, spell, AI flag): spell 0 → the spell menu (0x239D6 `[?]`); a spell with "when" = 0 is
refused. Delay = casting time ÷ 3. Delay 0: cast now (0x27C38) and the actions end (START 0x31D7). Else the caster
begins casting: combat record +0 = spell, initiative − delay when above it, else 1; the turn ends with the actions
not over, so the creature is picked again in the same round (D: `fight_casts` q29 → q30, the charmed Sean's AI Bless).
On its later turn the menu (0x9B21) and the AI cast +0 (0x27C38) and the actions end. Damage before that clears +0 (0x12AF4 / 0x2C22E).

**Cast** (0x27C38, in a fight): the target pick (DS:6B84 = 0x148BE). Not picked: the AI loses the spell at once
(the menu asks first): message, memorised copy removed. Picked: the caster faces the aimed square (0x27D9C–0x27E00:
the first direction whose cone 0x300CD holds it, 0x31232; its own square gives 0), animation, the caster's invisibility goes
(0x2BEBC), the current character's first memorised copy is removed (START 0x202B; slots 0x17..0x2B, exact byte;
not while an item is used), DS:681B = spell, the routine runs (DS:6B84 + 4·spell), DS:681B = DS:6820 = 0.

**Target pick** (0x148BE): DS:6820 = 0, list cleared, caster's square to DS:6DB9/6DBA. Mode = area AND 0x0F:
- 0: the caster.
- 0x0F: aim at a creature (0x14611, no bare square); the caster's +0x0A creature, else the list around the aimed
  square with radius `(area AND 0x0F) shr 4` = 0; DS:6820 = 1.
- 8..0x0E: aim (bare square allowed); the target list around it with radius `area AND 7`; DS:6820 = 1.
- 1..7: (area AND 3) + 1 aims, each a creature not already taken (a repeat is refused with a message, or skipped
  for the AI); an aborted aim uses up a pick. Empty list → not picked.
The aim itself and the range check are the menu's / AI's (0x14611: player side in combat-menu.md, AI side in
combat-ai.md "Casts by the AI").

**Resolution** (0x278DB, spell, strength, removal flag, damage, element, message): element mask = element (0
without damage); nothing without targets; strength 0 → the casting level. Per target in list order: a save (type
from the table, modifier 0) when the save byte is set; a range-255 spell recalculates the target, runs its class
0x0B and needs the caster's attack roll (0x2BBA1) against AC byte 0x111 — a miss zeroes the damage **for this and
every later target** and counts as saved; damage > 0 → non-weapon damage with reduction = the save byte; an effect
id → 0x2C540 for the duration (dice rolled per target for the dice-duration spells). Element mask 0 at the end.
Draw order per target: save 1d20, attack 1d20, duration dice; and inside: handler draws (mirror image).

**Effect messages** (read s25, P): the resolution's last argument is a text (far pointer, copied to a 40-byte local at
0x278E1) that it passes on to 0x2C540 for each target. 0x2C540 (stub 0xB0:0x84) adds the effect and then, only when
the text is not empty (0x2C603), shows START 0x2B8B(creature, 1 = effect frames, text) and START 0x2383 (rows 10–21
cleared); the unaffected path shows its own string (0x2C532) instead. Every spell routine passes a Pascal string that
sits just before its own code (`mov di, off; push cs; push di` + the string copy 0xAF8:0x0B0B before `call 0x8C3`);
Bless/Curse (0x27F67) and Haste/Slow (0x2977C) take it from their wrappers. Spell → string (GAME.OVR offset; call
site): 1 0x2801C, 2 0x2804E (via 0x28013); 5, 11, 18, 22, 29 0x280F5 (0x28127); 6, 7, 16, 17, 52–54 0x2812E
(0x28161); 8 0x28168 (0x281A0); 10 0x281EA (0x2827A); 14 0x283E4 (0x28449); 19 0x284A5 (0x284D7); 21 0x28526
(0x28661); 24 0x287EF (0x28827); 25 0x2882E (0x28860); 26 0x28867 (0x288E6); 27 0x2891C (0x289D3); 30, 50 0x28A21
(0x28A54); 31 0x28A5B (0x28A8D); 32 0x28A94 (0x28AD1); 33 0x28AD8 (0x28B0A); 38 0x2926C (0x2929B); 40 0x29358
(0x2938A); 42 0x294E3 (0x2952F); 44 0x2960B (0x29642); 45 0x29649 (0x2967B); 48 0x298A0, 55 0x29C17 (via 0x2984A);
57 0x29E10 (0x29E54); 61 0x29FA4 (0x29FD7); 63 0x2A035 (0x2A068); 67 0x2A0AF (0x2A0E0). An **empty** string (no
message): 4 (0x280EE), 9 (0x281D5), 15 (0x2849E), 20 (0x2851F), 28 (0x28A01), 47/64 (0x29775), 65 (0x2A0A3). That is
the 33 call sites of 0x278DB. The other callers of 0x2C540 pass their own: Hold Person 23/49 0x28668 (0x287DD),
Animate Dead 0x2907B (0x291FF), the fear aura 0x106DA (paralysis 0x34, 0x10794) / 0x106F1 (Curse 2, 0x107CB), the
paralysing aura 0x107EC (0x108C7), the charm gaze 0x10AC7 (0x10C20), the stinking-cloud terrain check 0x2B9A5 (0x1E
on a save, 0x2BACB) / 0x2B9B5 (0x1F, 0x2BB1A). Spells 12, 13, 35, 59 and the cures use the messages below.
Code: Core `EffectTexts` (keys), `PorTextCatalog` `EffectMsg.*`
(offsets), the Client's `CombatChooser.EffectTextMessage`. Outside a fight START 0x2B8B is START 0x16EC with the pause and no picture (read s27; layout in camp.md "Cast",
Client `CampScreens.EffectMessage`).

**Spell routine messages** (s32, DOS v1.3 program read). Direct messages use START 0x16EC with row 10 and the
game-speed pause. Animated messages use START 0x2B8B (far entry 00BA:1FEB) with effect frames. They use the same
paused text window in camp. The importer reads the strings from the player's program.

| Routine | Calls that show text | GAME.OVR string | Display |
|---|---|---|---|
| Enlarge, Strength, Giant Strength | 0x2834C, 0x29054, 0x29F3C | 0x282B1, 0x28F4D, 0x29EF1 (same as handler 131) | Direct after a successful raise |
| Effect removal (including Cure Disease and Heal) | 0x2BFA8 | 0x2BF62 | Direct before the effect leaves. Cure Disease has only these messages |
| Reduce | 0x283DB | 0x28375 | Direct after removing Enlarge |
| Stinking Cloud | 0x28EEC | 0x28B11 | Direct without the pause flag, redraw centred on the aimed cell (0x28EFF, delta 255), pause and clear, then terrain checks |
| Cure Blindness | 0x29263 | 0x29227 | Animated after the removal message |
| Dispel Magic | 0x294CF | 0x29391 | Animated after a pass that removed an effect |
| Remove Curse (43) | 0x2958C, 0x29602 | 0x29536, 0x29543 | Animated after removing Bestow Curse or uncursing the first cursed item |
| Restoration | 0x29E07 | 0x29C4F | Direct after level stats |

**Handler messages** (read s27, P): the monster specials print through START 0x16EC(creature, text, row, pause
flag) directly, not through 0x2B8B (no view scroll, no effect frames). Each text is a Pascal string in the handler's
own code segment, copied to a local first (0xAF8:0x0B0B). Without the pause flag the text stays in the panel through
the moving icon that follows until the next 0x16EC call clears its rows; with it, PauseOnly then START 0x2383.

| Handler | Call | Creature | Text (GAME.OVR) | Row | Pause |
|---|---|---|---|---|---|
| 83 gaze | 0x10940 | gazer | 0x108E8 (CS:1C8D of unit 0xEC5B) | 10 | no |
| 83 reflected | 0x10A1A | target | 0x108F1 (CS:1C96) | 12 | no |
| 83 stone | 0xECDD in 0xECB3, text from 0x10AAA | victim | 0x108FE (CS:1CA3) | 10 | no (0xECB3's end follows, "Status changes") |
| 84 charm gaze | 0x10B7F | gazer | 0x10ABE (CS:1E63) | 10 | no |
| 88 lightning breath | 0x2A147 | breather | 0x2A0E7 (CS:30CF of the spell unit) | 10 | yes |
| 121 acid breath | 0x118E9 | breather | 0x1186C (CS:2C11) | 10 | yes |
| 87 rot touch (s30) | 0x110E0 | target (DS:6826) | 0x1103B (CS:23E0) | 10 | yes |
| 77 hold (s30) | 0x10629 | target (DS:6826) | 0x10583 (CS:1928) | 10 | yes |
| 75 blood drain (s30) | 0x10485 | drinker | 0x103B9 (CS:175E) | 10 | yes |
| 73 rake (s30) | 0x16240 | raker | 0x161C4 (CS:39C9 of unit 0x127FB) | 10 | yes |
| 131 gauntlets (s30) | 0x11CB1 / 0x11CD8 | wearer | 0x11BA6 (CS:2F4B) | 10 | yes; only when 0x2C046 raised the Strength; in a fight the handler sets DS:49F3 = 2 around the call (0x11C91 / 0x11CB6), so the text-window layout is drawn over the Items page |
| 40 cloud ends (s30) | 0xF9A4 | — | 0xF903 (CS:0CA8) | START 0x1693: row 24, green | yes |
| 135 too weak (s30) | 0x11DCA | — | 0x11D76 (CS:311B) | START 0x1693: row 24, green | yes |

s30 (P): those seven were raised as `EffectEvent.Other` and not shown; now `Diseased`, `HeldFast`, `SucksBlood`,
`Rakes`, `Stronger`, `AirClears`, `NeedsGiantStrength` with the texts `CombatMsg.Handler*` (catalog entries added; the
importer copies texts, so they reach a module only after an importer bump). Client: the panel from row 10 with the pause
(`CombatChooser.HandlerMessage`), row 24 for 40 and 135 (`PageSay`); 131 in a fight puts the name on row 18 and the text
on row 19 during the pause, on a framed text window over the Items page that clears rows 17–22 (`ItemEffectTextWindow`,
s32). Item handlers in a fight: Ready / unready (0x2250A, the menu's Items page and the AI's) now runs
the item's handler 0x3E through 0x2AEEA (`FightKit` wires `FightItemScreens` and `MonsterAi.ItemHandler`, s30; before,
nothing ran). Outside a fight the Client has no Ready yet (`CharacterView` TODO), so 131 / 135 there are `[?]`.
**D** (s33, Darkstar Str 12 readying the gauntlets of ogre power from View's Items, seed 12345): the frame is drawn
again around rows 17–22 over the Items page, the name on row 18 and the text typed on row 19, the pause (~0.95 s),
then the page again with the item readied; while the text shows the page's rows are unlit and row 24 keeps the Items
menu with Ready lit. Fixed both (`CombatScreen`); 28 of 29 frames equal (the help lines before the step).

0xECB3 shows its text for every caller (row 10, no pause flag), before its dead / stone / gone test; every caller's
text is mapped (s29, "Status changes"). Code: Core `EffectEvent.Gazes`, `GazeReflected`, `Stoned`,
`CharmGazes`, `Breathes`, `SpitsAcid`; `PorTextCatalog` `CombatMsg.Handler*`; the Client's
`CombatChooser.HandlerMessage`. **D** (s29, DOSBox-X 3000 cycles against the Client with the same seed and scratch
saves, every distinct window frame of both compared as pixels; set-up in `dosbox-trace-method.md` "s29"): the charm
gaze 84 (a vampire, MON4 23: name and text typed from row 10, no pause, the flight gazer → target, then the target's
charmed message 0x10C20 with the COMSPR 9 frames), the acid breath 121 (an ankheg, MON6 65: text from row 10, the
pause, rows cleared, the one-frame flight, then the damage message), the gaze 83 and its stone message (a basilisk,
MON2 26) and the lightning breath 88 (Tyranithraxus: text, pause, flight, the bolt's flights and damage messages):
every message frame equal pixel for pixel, with the typing, rows and pauses as in the table (the few frames that
still differ are flight ends, an AI panel redraw and a skull's 3 pixels: combat-menu.md "s29 DOSBox-X checks", `[?]`). The 84 gaze came from the
vampire four squares from its target (the `[?]` range byte stays unread).

**Status changes** (read s29, P): 0xECB3 (unit 0xEC5B, near calls only; no far caller in GAME.OVR or START.EXE)
copies its text argument to a local (0xAF8:0x0B25, 40 characters) and calls START 0x16EC(creature, text, 10, 0)
**before** the status test. A creature already dead, stone or gone (6, 7, 8): return, the text stays in the panel
until the next text. Else (0xECF8–0xED5D): status, in play 0, HP 0, 0x2BEF4, class 0x0D; still out of play →
CreatureDowned 0x3154B (no fight test here: in a fight the body, the sound DS:261E, the picture and PauseOnly at
0x3174D; outside, the sound and PauseOnly at 0x31569); then PauseOnly (0xED44) whatever happened; START 0x17E3 (= the
docs' START 0x2383: in a fight (23, 10)–(38, 21) cleared, else (1, 18)–(38, 22)); outside a fight START 0x0767
(DS:5D92) redraws the party list. So a victim taken out shows its text through two pauses in a fight. The nine call
sites, their texts (Pascal strings, CS of unit 0xEC5B; GAME.OVR offset = 0xEC5B + CS) and the message before:

| Caller | Call | Status | Text | Before it (START 0x16EC from row 10) |
|---|---|---|---|---|
| 22 (Slow Poison's end on a poisoned creature) | 0xF406 | 6 | CS:0764 (0xF3BF) | — |
| 32 (Animate Dead's 0x20 removed, DS:681F = 0) | 0xF7A4 | 6 | CS:0B11 (0xF76C) | — |
| 57 (mummy rot, last stage: param ÷ 16 ≤ 1) | 0x100A4 | 8 | CS:138A (0xFFE5) | CS:1382 (0xFFDD) at 0x1001F, pause flag 1, each time 57 runs |
| poisoned hit 0x101A0 (64, 65, 66, 70) | 0x10230 | 6 | CS:153B (0x10196) | CS:152F (0x1018A) at 0x101F2, flag 0, then PauseOnly 0x101F7 (no clear); then 0x37 added (0x2BD3C) |
| 83 gaze | 0x10AB5 | 7 | CS:1CA3 (0x108FE) | the gaze (table above) |
| level drain 0x10D7D (85, 86), animated | 0x10E15 | 8 | CS:20FB (0x10D56) | 0x10DEC, flag 1: CS:20EF + (count > 1: Str(count) (START 0x1037) + CS:20DF; else CS:20E7) |
| level drain, hit dice 0x73 < 2 | 0x10F35 | 8 | CS:20FB | (same message, once per call) |
| level drain, HP 0 and status ≠ 6 | 0x10FAB | 6 | CS:2118 (0x10D73) | (same) |
| 95 (in play) | 0x1129D | 6 | CS:2605 (0x11260) | — |

Order around a drop: in 0x12AF4 (attacks) and 0x2C44A (non-weapon damage) the down / dying / killed words come
first, then 0x2BEF4 and class 0x0D (whose handlers may print: handler 32's text replaces the panel from row 10), then
CreatureDowned (still out of play) or PauseOnly, then START 0x17E3 (0x12DE1–0x12E17; 0x2C4F3–0x2C527; outside a fight
0x2C44A only pauses, 0x2C4EC). An animated creature (status 1) is always dead (6) when its HP runs out (START 0x2DC8),
so on these paths handler 32's 0xECB3 stops after its text, which then shows through CreatureDowned's pause instead
of the killed words. 0x3154B's fourth caller 0x13ACE is turn undead's destroyed branch (its own text; combat-menu.md "Turn undead").
Code (s29): Core `EffectHandlers.InflictStatus` raises the caller's event (`PoisonDeath`, `Collapses`, `RotsAway`,
`PoisonKilled`, `Stoned`, `DeadForever`, `DrainKilled`, `FallsDead`) and, when the status is set, `StatusDone` after
the map removal; `Poisoned`, `Rots`, `Drained` (count `EffectHandlers.DrainCount`) before; 0x2C44A ends with
`GoesDownDone`; the attack routine calls `FightState.AttackMessage` after the damage and `FightState.AttackEnd` after
`FightRules.DropDefender`. Texts `PorTextCatalog` `CombatMsg.Status*`, `Poisoned`, `Rots`, `Drained*` (importer
0.7.18); Client `CombatChooser.EffectMessage` / `StatusMessage` / `AttackEnd`. `[?]` Not compared with DOSBox-X.
**Outside a fight** (read s30, P): handlers 22, 32 and 57 run from the clock's effect timeout 0x23DCC (a step, Look,
CLOCK while exploring; the camp's rest), and 95 cannot (above). START 0x16EC's out-of-fight branch (0x22F7) ignores the
row: with DS:6DDA = 1 (set by the rest loop 0x24A66 at 0x24AE2, cleared at 0x24C20 and at the start-up 0xE9E3; no other
writer) r = 18, else r = 17 and the text box frame is drawn again (03F1:0000); (1, r)–(38, 22) cleared, the name on row
r + 1, the text word-wrapped from row r + 2 in green; with the pause flag PauseOnly and START 0x2383 = (1, 18)–(38, 22)
cleared. 0xECB3's text has no pause flag, so it stays; its end outside a fight: CreatureDowned's out-of-fight branch
(0x31554 set auto-stop 0, 0x31560 the body cue DS:261E, 0x31569 PauseOnly, then 0x317D2) when still out of play,
PauseOnly (0xED44), START 0x17E3 (rows 18–22) and START 0x0767 (DS:5D92: the party list). Handler 57's rot words and the
drain words pass the pause flag. Client (s30): `OutsideEffectMessages` on the camp's text window during the rest (DS:6DDA
= 1: rows 18–22, the rest-time line on row 17 kept) and Cast, and on the area's text window for the exploring clock
(`ClientPlayer.ShowOutsideEffects`, hooked on `PorGame.OutsideEffects`); the party list is drawn from memory every frame,
so it shows the new status before 0x0767 would. **D** (s33, camp Rest of 15 minutes with an unconscious Darkstar,
3 HP, carrying 0x4E for 1 minute with the removal call): on the first tick the revive text (handler 78 → 0x2C753)
the party-side revive text: the name on row 19, the text typed from row 20, the pause, rows 18–22 cleared; the rest-time line kept
as drawn (15 minutes, the minutes still lit) and the position line at the old time until the rest ends; the
rest-time menu row stays through the message. Fixed: the rest-time menu keeps the program's spacing ("Rest   daYs
Hours Mins   Inc Dec   Exit"), its field stays lit, the position line holds through the rest, the menu row stays
under the pause. **D** (s38, the same set-up from a test save: Darkstar status 4, 0x10D = 0, 3 HP, `CHRDATA5.SPC` =
one record 0x4E, 1 minute, param 255, removal call 1; Encamp, Rest, Inc × 3, Rest): the party list keeps Darkstar's
light red name through the rest, the camp menu and the area after leaving the camp (nothing calls 0x0767); the camp's
View redraws the screen and the name comes back cyan. Client: the revive outside a fight holds the name
(`TradeContext.HoldRosterName`, `ClientPlayer.RosterNameHeld`) until a full-screen page, a fight, StatusDone's 0x0767
the rest's heal message with anyone healed (0x2463D) or another current member (Home / End at the camp menu, D s38)
draws the list again; with the help lines and the camp picture masked,
the revive then View run 20 of 21 Client frames equal (the miss: the Client's own View page) and the revive then Home /
End run 42 of 42. **D** (s39): steps, turns and the area map keep the light red name; Encamp's entry draws it cyan;
every point that draws the list while exploring is in view3d.md "Where the party list is drawn" (the Client releases
the name there: revive, Exit, Left, Up, Right, Up, A, A, Encamp, Alter, Icon run 75 of 79 Client frames equal with
the help lines and the camp picture masked, 60 of 77 before; the misses are the icon editor's ground). While Rest is lit row 24 also lights the
D of daYs: camp.md "Rest-time menu".
`[?]` A creature dropped twice in one call chain (0xECB3's end inside 0x2BEF4 of an attack): 0x3154B's early exit
(0x31571–0x315A3 compares the body entry DS:70C7 × 7 + DS:66D6 eight times) not settled; the Client shows the downed
picture again.

**Revive and put out of play** (read s30, P). **0x2C753** (creature, HP): 0x317E1 on its square (no → returns 0);
status 0, in play 1, HP; in a fight 0x319D2(creature, 3, 0) (the view, no cursor box); the text by the hostile byte
0x10E (1: CS:1848 = GAME.OVR 0x2C732, else CS:185C = 0x2C746, both in unit 0x2AEEA) through START 0x16EC(creature,
text, row 10, pause 1): the pause and the clear; then the team count START 0x2F7B (00BA:23DB); returns 1. Callers:
handler 78 (0x10649), 99 (0x113E6), 102 (0x114D5), Animate Dead (0x291CE; the hostile byte is the caster's by then, so
a party caster's zombie gets the party-side text). Outside a fight the same text in the text-window layout: a rest's
healing reaches it through handler 0x4E (78) for an unconscious member. Core: `EffectEvent.Revived`; Client: the view,
then `Say` (row 10, pause); `OutsideEffectMessages` outside. **0x2BDF0** (creature, status, text): nothing at all when
out of play (0x2BE0C); slot 0x31399; 0x319D2(creature, 3, 0); START 0x16EC(creature, text, 10, 1); in play 0, status,
HP 0 unless status 3; 0x30C65(slot, 0, 0), START 0x464:0x1B52, DS:5F2A + 4 × slot = 0, 0x30ADA (the icon off the map,
the map redrawn); START 0x31D7 (actions end) → 0x2BEF4. Three callers: the AI's surrender 0xBE31 (status 4; s29,
`AiMessage.Surrenders`), handler 103 0x11551 (status 3, CS:28A5 = 0x11500; it clears its byte 4 and calls 0x319D2
itself first, 0x11534, whatever follows; Core `EffectEvent.EscapeView` then `Escapes`, the map removal now in
`EffectHandlers.PutOutOfPlay`). 103 is carried only by the vampire (MON4SPC block 23, permanent, removal off) and runs
only from trigger 0x0D, which every drop path (0xECB3, 0x2C44A, the attack's drop) calls after in play is 0: so
0x2BDF0 returns at 0x2BE0C, the vampire stays down (dead or dying) and only the view moves; the escape text can show
only for a creature still in play (none on the program's paths, as read) — `HandlerMessageTests.A_vampire_downed_…`;
**D** (s37: the vampire at HP 1 killed by a level-12 Darkstar's Magic Missile, seed 12345): the death text, the downed
picture, then the next member's turn; no escape text, no view change seen; the Client equal frame for frame (121 of
122, the other its help lines) once the panel keeps the lines START 0x1583 drew (below); the flee 0x134EC (status 3, CS:0C4F
= 0x1344A, "away"); the flee's other branch (0x134F3) shows CS:0C58 = 0x13453 through START 0x1693 (row 24), then both
end the actions (0x1350D). Core `FightState.FleeMessage` (raised before the status change), Client
`CombatChooser.FleeMessage`. The revive's fight text **D** (s33, the wild boar): equal frame for frame
(combat-menu.md "s33"). **D** (s36, s37, the not-away branch): Phineas walking east to the combat map's edge (seed 12345,
`fight_save.py BASE OUT 1C0B00040001000224 --hp 200 --monpatch 4:0x72=30`, the orc fast and in sight); Yes at the flee
question: row 24 shows CS:0C58 in green at column 0 0.27 s after the key, for 0.68 s (the game-speed pause), row 24
cleared, the panel kept, then the next member's turn (no creature-side text, no status change). **D** (s36, s37, away;
the orc's movement base 0x72 = 4, or no enemy in sight): 0.28 s after Yes the panel below the weapon line is cleared and
the name (cyan) goes on row 10, the CS:0C4F words (green) typed on row 11 over ~0.09 s; held ~0.93 s; the text cleared,
then the icon leaves the map, the map redrawn and the next member's panel. Row 24 keeps the flee question with **YES
lit** (Y, E, S colour 15; N 15, O 10) until the next menu; s36's "neither lit" was the frame tool's reading. **P + emulation (s37):** the question is 0x2D9A0 (GAME.OVR 0xA431, Y → 0x13465 at 0xA440); its hotkey path 0x2CF48 → 0x2CAA9 redraws the row with YES lit, and nothing after it (0x13465, 0x2BDF0 with every draw trapped under unicorn) writes row 24; text goes to both EGA pages directly (0709:01DF), so no page copy brings back an older row. Client
(s37, seeded, same keys): every frame equal to one of DOSBox-X's but the help lines (away 138 of 139, not away 227 of
228). The two gaps DOSBox-X showed (~0.21 s between the text's clear and the icon leaving the map, away; ~0.23 s
between row 24's clear and the next panel, not away) are **machine time** (P, s38): no Delay, PauseOnly, key wait or
BIOS-tick read lies on either path (static call trees; the program's only wait is CRT Delay 0xA40:0x29C, and nothing
reads INT 1Ah or 0040:006C). Away: START 0x16EC returns right after its clear (START 0x2383 at 0x237A), then 0x2BDF0
runs 0x30C65 (the ground over the footprint, 0x31B5D → START 0x464:0x8D4), START 0x464:0x1B52 (the changed view rows
copied to the screen: here the icon goes), 0x30ADA (the cell grid), START 0x31D7 and 0x2BEF4. Not away: after START
0x1693's clear, START 0x31D7, the menu loop's exit (0x9DC5 → 0x9DFA), the turn's end (0x31321, 0x31349, 0x30A0F),
the next pick 0x9938 and the next turn's start (0x2B1E4, 0x319D2, START 0x1758, START 0x1583); a round end 0xA087
between them waits only with a message or picture of its own. The Client does both
at once, as the program does on a fast machine; not changed.
The panel's creature lines (s37): START 0x1583 draws the name colour and HP once; damage and leaving play do not redraw
them (the vampire above kept its yellow name and HP 1 through the damage and death texts). Client
`CombatView.ShownLines`, taken at each 0x1583 (`ShowPanel`, Manual's panel, the attack-ally question).

Where to see each (for the DOSBox-X check; monsters from the MON*SPC sets, s27's way of loading another disk's
monsters: dosbox-trace-method.md "Monsters of another disk"):
- 83 stone and the poisoned hit 64: the s27 medusa (MON5 49: 0x53 gaze and 0x40 poison); basilisk MON2 / MON5 26
  for the gaze alone. A party member's failed save shows the gaze, the stone text and two pauses.
- 64 / 65 / 66 / 70 poisoned hit: giant snake MON5 / MON6 60 and wyvern MON8 121 (64), poisonous frog MON4 38 (65),
  large scorpion MON4 18 (66), huge scorpion MON4 39 (64), phase spider MON8 116 (70). **D** (s30): four huge
  scorpions on save A, seed 4242: Rhiannon's failed save, the poisoned and killed texts, the downed picture, equal
  frame by frame (combat-menu.md "s30 DOSBox-X checks").
- 85 / 86 level drain: wight MON4 20, wraith MON4 21 (85); spectre MON2 / MON4 17, vampire MON4 23 / 105, Ferran
  Martinez MON4 19 (86). The dead-forever branches need a target with hit dice 0x73 < 2 or an animated one (a party
  member's Animate Dead zombie).
- 32: an Animate Dead (spell 36) creature killed in a fight (its text through the downed picture's pause).
- 57: mummy MON4 114 gives rot (87: 0x32 and 0x39); 57 runs when 0x39 ends (43 200 minutes): a save with a member's
  CHRDAT*.SPC 0x39 record set to 1 minute (param 0x1F → the rot text; 0x10 → also the last-stage text) and a fight
  long enough for a minute to pass.
- 22: Slow Poison (26) on a member killed by a poisoned hit (0x37; the routine sets HP 0 → 1 and runs handler 0x4E):
  when its effect 0x16 ends with the poison still on, the member dies again with this text.
- 95: carried after handler 99's revive (0x5F for 1d4 + 1 minutes). **Effect 0x63's source (read s30, P)**: the wild
  boar, MON6SPC.DAX and MON8SPC.DAX block 120, carries 0x63 permanently (param 255, removal off); no other MON*SPC
  record, no CHRDAT*.SPC, no item (all 681 records: worn-item codes give only 3, 61, 82/240, 89, 38) and no code: every
  writer of the effect list was checked (45 far calls to 0x2BD3C / 0x2C540, their 3 near calls inside unit 0x2AEEA, the
  8 callers of 0xEC7A, the spell resolution's id column DS:3204 + 16 × spell, the item handler's byte 0x3D). So the
  chain is the boar's: downed (trigger 0x0D) → 99 puts it back with 6 HP and its revive message, adds 0x5F and drops
  0x63; when 0x5F ends (a minute passing at the end of a round) 95 kills it (0xECB3, its text). Outside a fight 95 cannot
  run (monsters leave with the fight). Core test: `HandlerMessageTests.A_wild_boar_gets_back_up_once_…`.
  **The original crashes there** (read and seen s39, P + D): the clock's effect walk 0x23DCC removes the expired 0x5F
  record through 0x2AF10 **with its record pointer** (call at 0x23FCA). Its byte 4 is set (99 added it with removal
  on), so 0x2AF10 runs handler 95 **before** unlinking (0x2AF8B). 95 clears byte 4 and kills the boar: 0xECB3 (status
  6) → the dropped-creature removal 0x2BEF4, whose list DS:0C14 holds 95 (seen in a state's DS), so 0x2AF10(nil, 95)
  finds the same record (byte 4 now 0: no handler), unlinks it and frees it. Back in the outer 0x2AF10 the list head
  is now nil, the predecessor walk (0x2AFBD–0x2AFFD) stops at once and 0x2AFFF–0x2B011 writes the record's next pointer
  through nil + 5 (0000:0005, in the interrupt table), then FreeMem(record, 9) (lcall 0AF8:0254, START's System unit;
  carry → AX 0xCC) frees it a second time: **Turbo Pascal runtime error 204 (invalid pointer operation) at 00B0:013A**,
  the return address after that call (stub 0xB0 = GAME.OVR unit 0x2AEEA, offset 0x13A = 0x2B024), and the game exits
  to DOS. Reach: any wild boar revived by 99 that is still in play at the end of the round in which its 0x5F
  (1d4 + 1 minutes, one minute per round) runs out; a boar killed again before that loses 0x5F through 0x2BEF4's
  nil path and the fight ends normally. **D** (s39, seed 12345, all Quick): MON6 120 patched to 1 HP (s38's set-up)
  revived and was killed by its 0x5F → the error over the map; the stock boar (18 HP, `fight_save.py … --mon 6` with no
  patch) was knocked out, revived with 6 HP and killed by Thrender's next blow → the continue-battle question as
  usual; from a state taken just after that revive with every member's 0x10F cleared (the player taking the party
  off Quick; it does not touch the effect list) and every member on Done / Quit, the boar's 0x5F ran out two rounds
  later → the same runtime error. Core unlinks first and then calls the handler (`FightRules.RemoveFirstEffect`, above),
  so the Client kills the boar and goes on to the continue-battle question. **Decided (user, s39): fixed, not an
  option** — an original bug; the Client goes on (PLAN.md decisions).

**Routines**:

| Spell | Routine | Rule (draws before the resolution) |
|---|---|---|
| 1 Bless, 2 Curse | 0x28027, 0x28058 → 0x27F67 | DS:6820 = 1; targets not on the side (Bless: the caster's; Curse: the other, START 0x2F57) leave, and for Bless in a fight those with an enemy in their radius-1 list (START 0x2FD3); plain resolution |
| 3 Cure Light Wounds | 0x28083 | first target healed 1d8 (no bypass), message |
| 4 Cause Light Wounds | 0x280BF | damage 1d8 (count to 681C), element 8 |
| 9 Burning Hands | 0x281A8 | damage = casting level, element 9 |
| 10 Charm Person | 0x281F5 | target type > 1 or size > 1 → unaffected; strength = level + caster hostile × 128; removal on; then the target's charm handler runs |
| 12 Enlarge | 0x282BD | Str 18 / exceptional by level (1: 0, 2: 1, 3: 51, 4: 76, 5: 91, 6: 100; other levels keep the previous value `[?]`); raise (0x2C046); effect 0x0C, removal on, with the returned code |
| 13 Reduce | 0x28386 | first target fails a save vs. 4 and loses an Enlarge → message |
| 14 Friends | 0x283F0 | first target's Charisma + 2d4, at most 25; strength = the old Charisma, removal on |
| 15 Magic Missile | 0x28451 | damage (level ÷ 2)d4 + level ÷ 2 (count to 681C), element 8 — **0 at levels 0–1, no draw** (checked under emulation for levels 0–12, and with an item level 6 = 3d4 + 3; the resolution then sees damage 0, so element 0 and no 0x2C22E: nothing happens. DOSBox `fight_casts` b02 → b04, s13: no damage, no draw) |
| 20 Shocking Grasp | 0x284DF | damage level + 1d8, element 0x0C; range 255 → attack roll |
| 21 Sleep | 0x28533 | DS:6820 = 1; budget 4d4; cost by hit-dice byte 0x73: ≤ 1 → 1, 2 → 2, 3 → 4, 4 → 6, 5 → 10 (race byte 0) or 20, else 20; animated, already asleep or unaffordable targets leave |
| 23, 49 Hold Person | 0x2866F | modifier by target count (1 → −2 for 23 / −3 for 49, 2 → −1, 3–4 → 0); from the last target: save; type > 1 or size > 1 counts as saved; effect for the duration, strength = level, no removal call |
| 26 Slow Poison | 0x28873 | animated target dropped; poisoned target: HP 0 → 1, resolution (strength 255, removal on), handler 0x4E (removed), effect 0x0F 10 minutes |
| 27 Snake Charm | 0x28927 | budget = caster's HP; every snake (type 0x0E) in the creature list whose HP fit is taken |
| 28 Spiritual Hammer | 0x289DB | resolution with removal on; handler 0x17 on the first target |
| 32 Mirror Image | 0x28AA2 | strength 1d4 |
| 5, 6, 7, 8, 11, 16–19, 22, 24, 25, 29–31, 33, 50, 52–54 | shared | plain resolution (the table's effect) |
| 34 Stinking Cloud | 0x28B29 | DS:6820 = 1; a node at the end of DS:6DBB numbered by the current character's nodes **except the list's last** (the walk stops on it: a quirk, so numbers repeat); effect 0x28 on the current character for (level) minutes, param = number × 16 + level, removal on. Cells i = 1..4 at aim + direction DS:290D[i] (8, 2, 3, 4: a 2 × 2 block): slot kept; terrain > 0 with cost < 0xFF → clouded. Kept terrain: its own; under 0x1E the last earlier node's kept terrain ≠ 0x1E for that cell; under 0x1F the last body entry's on it. Clouded cells = 0x1E; message, redraw; terrain check 0x2B9D1 on each creature found |
| 35 Strength | 0x28F59 | gain: MU level 0x9B > 0 → 1d4; cleric 0x96 or thief 0x9C > 0 → 1d6; fighter 0x98 > 0 → 1d8 (each rolled, the last kept; none → uninitialised `[?]`, 0 here); Str + gain > 18: fighter → 18 / min(100, excess × 10 + 0x16), others 18 / 0; raise 0x2C046 (message); effect 0x26 for the duration with the code, removal on |
| 36 Animate Dead | 0x29087 | DS:6820 = 1, list emptied, budget = level; whole creature list while budget: status 6, type 0, 0x317E1 on its square → param = level + old hostile × 16; hostile = current's, 0x10F = 1, 0x76 = 2, 0x6B = 0, 0x72 = 6, memorised cleared, 0x84 → 0xB2 (> 0x7F) or 0xB3, type 4, in a fight +0x0A cleared; budget − 1; 0x2C753 with max HP → effect 0x20 (permanent, param, removal on) and status 1 |
| 37 Cure Blindness | 0x2922F | first target loses 0x21 (0x2BF6B, message) → message |
| 38, 44, 45, 61, 63 | 0x29275, 0x2961C, 0x29655, 0x29FB1, 0x2A042 | plain resolution |
| 39 Cure Disease | 0x29347 → 0x292A2 | under the lock: first target loses (with message) 0x22; 0x2B, then its 0x2C and 0x1F; 0x32, then its 0x39; true when any of the three went |
| 40 Cause Disease | 0x29364 | resolution with removal on (duration 1d6 × 10) |
| 41, 46 Dispel Magic | 0x2939D | DS:6820 = 1; count times, always on the **first** target (reads DS:6C95 each pass, a quirk): each record with param < 255: DS:4845 = param AND 0x0F; chance 50 + 5 × (level − it), 50 − 2 × (it − level), or 50 (byte); 1d100 ≤ chance → removed (0x2AF10); any → message |
| 42 Prayer | 0x294EE | strength = level + current's hostile × 16 (byte); plain |
| 43 Remove Curse | 0x29558 | first target loses Bestow Curse 0x24 (message) → message; else its first item with 0x36 ≠ 0 gets 0 → message |
| 47, 64 Fireball | 0x29683 | DS:6820 = 1; dice = 1d3 × 2 + 1 (64, wand) else level; VM $49E6 (word [DS:49D2]+0x1CC) = 0 (the wilderness overland, `save-format.md`) → the list is rebuilt: 0x3045B around the aim, range 2, no cone, size 1, every slot listed; (draw 0x310D0); resolution with (dice)d6 (count to 681C), element 9 |
| 48 Haste, 55 Slow | 0x298AA / 0x29C21 → 0x2977C | side = caster's (Haste) / other (Slow); DS:6820 = 1, budget = level; a target of the side while budget lasts uses one and leaves when it loses the opposite effect (Slow 0x2A / Haste 0x27, message); others leave; resolution; class 0x12 on each left |
| 51 Lightning Bolt | 0x29BCF | damage (level)d6 (0x2BCCD, 681C untouched); 0x298D4 on the aimed square; the bolt 0x29973 (8, damage, 4, rebound bonus) |
| 60 | 0x29F65 | 0x298D4 with 1d6 + 20 on the aimed square; then 0x29973 (3, 20, 4, no bonus) |
| 56 Restoration | 0x29C5B | the first target only (no search); nothing when its 0x74 is 0. g = 0x75 ÷ 0x74; 0x32, HP, 0xB1 + g, 0x75 − g, 0x74 − 1; class (0..7) with 0 < level ≤ best (13), 0 < XP[level + 1] < lowest (10,000,000), not stopped by START 0x3AF7 → + 1 level; XP 0xAC raised to that threshold when below (also with no class found: the routine then adds 1 at its uninitialised index [bp−0xC], **8** in DOSBox-X, i.e. record 0x9E gender + 1; see below); 0x2AA87 (always, also with no class); no START 0x1758 recalculation; message. At and past the race limit: below |
| 57 | 0x29E1A | first target losing Slow 0x2A (message) → done; else plain |
| 58 | 0x29E65 | first target losing poison 0x37 → under the lock its 0x16 goes; else 0x292A2; nothing cured → heal 1d4 + 8 (no bypass), message |
| 59 | 0x29EFD | raise to Str 21 / 0 (message); effect 0x26, duration 1d4 × 10 + 40, removal on |
| 62 | 0x29FE8 | heal 2d4 + 2 (no bypass), message |
| 65 | 0x2A070 | damage 2d4 + 2 (count to 681C), element 8 |
| 66 | 0x2A0AA | nothing |
| 67 | 0x2A0BA | strength 255, removal on (effect 4 for 1440) |

**Bolt hit** (0x298D4, x, y, damage, save type, out rebound): cell 0x30BF8; terrain with cost 0xFF → rebound. A
creature there: DS:6819 = 0x0C, save (type, 0), 0x2C22E halved when saved, DS:6819 = 0.

**Bolt** (0x29973, length, damage, save type, bonus): nothing when the caster (0x31321 / 0x31349) stands on the aim.
DS:6820 = 1, left = 2 × length, last slot = the aim's, direction 1. While left: line (0x2FCF2) from the aim to aim +
(aim − caster) × direction × left; step (0x2FE1E) until the end, a slot ≠ last, terrain 0, cost > 1, or the line's
distance ≥ left; last slot = that cell's; terrain 0 → left 0; (draw: the moving icon from the cell the walk went on from, set at
0x29A68 on entry and after each hit, to this cell; **D** s27); bolt hit there. Rebound: the aim moves there, a
line is walked back to the caster, and when the bonus is still on and that is ≤ 8 half squares the line's distance
+ 8; direction reversed, bonus off, last slot 0. Left − the line's **total** distance (not the part since the last
hit), at least 0; without a rebound the same line goes on, else a new line. DS:6820 = 0.

**Castable** (`FightSpells.Castable`): with cast allowed (+1), every distinct memorised id (bit 7 clear) whose "when"
byte is not 0. `[?]` The menu's own list (0x239D6) is not read.

## Where the data is (`PorSpellData`)

- Spell table: the immediate of START 0x3262's `mov cl,4 / shl di,cl / mov al,[di+imm] / cmp al,0 / jne`.
- Experience table: the immediate − 2 of GAME.OVR's `mov dx,0x38 / mul dx / mov di,ax / add di,cx / cmp word
  [di+imm],0` (113 dwords read).
- Name lists of handlers 18, 26, 47: the immediates of `mov cl,4 / shl di,cl / add di,imm / push ds / push di /
  lcall` in GAME.OVR, in that order; handler 48's two names: the Pascal strings right before its code.

## Open `[?]`

- The stat recalculation (above); START 0x2197 (where a created item goes, 0xC7); the spell menu (0x239D6; the camp's
  out-of-combat cast and 0x27AA9 are in camp.md); the map target list 0x3045B (implemented by
  the map code through `IFightTargetList` / `IFightAuraRange`; without it auras in a fight are not found and area
  spells fall back to the map distance with a diagnostic).
- Handler 84's sight range (an uninitialised stack byte; DOSBox-X s29 saw gazes at distance 4).
- Everything else here is **P**; the Sleep cast and Restoration's no-class index below are **D**; the routines under "Checked under emulation" are **E**.

## Restoration with no class to raise — D (s18)

The class index of 0x29C5B ([bp−0xC], signed) is written only when a class qualifies. A target with drained levels
(0x74 > 0) and no qualifying class takes the residue. That is the case for every monster: no class levels and drain
bytes 255/255 (so g = 1, 0x74 and 0x75 become 254). In v1.3 the spell comes only from the clerical scrolls in
`ITEM2`–`ITEM4` (no monster memorises it; random scrolls never hold spell 56). A drained party member normally
has a class to raise; one at its race limit or its class's top level has none and takes the residue too (E, s22;
D, s24, below).

DOSBox-X, residue probe (`dosbox-trace-method.md`), fixture `fight_restoration`: a scroll used by a level-1 cleric
in a fight on a drained party member (a class was found), then on a goblin guard, and outside a fight on another
member: the index read **8** all three times (routine BP 3CEE in the fight, 3BD0 outside). On the goblin the record
changed exactly as the routine reads with index 8: 0x74/0x75 255 → 254, max HP 0x32, HP and rolled HP 0xB1 + 1,
**gender 0x9E 0 → 1**, experience 0xAC → 10,000,000, then the level stats (base THAC0 0x2D 41 → 40, saves 0x6D–0x71
→ 20). Core uses index 8 (`FightSpells.RestorationResidueIndex`; `RestorationCaptureTests` replays the goblin record
byte for byte after a stat recalculation: THAC0 0x110 had been recomputed by the time of `after`, consistent with
the turn-start recalculation 0x9A8A, but its trigger was not pinned; the routine itself does not recalculate). What writes the 8 was not traced; a cast from a memorised Restoration (impossible in v1.3) might
see another value.

At the limit, DOSBox-X (s24, fixture `restoration_limit`, residue probe): the same scroll read outside a fight on a
drained human fighter 8 (top level, male) and a drained elf fighter 7 with Strength 18 (the elf's limit row, female).
The probe read **8** both times (BP 3BD0), neither gained a level, and the camp save after it holds exactly the
records the routine gives with index 8 followed by the stat recalculation (`RestorationProgramTests`): HP + g, XP
10,000,000, gender + 1. It matters on screen: the man becomes FEMALE, and the woman's gender 2 has no name. View
reads the gender table (DS:0FEC, 7-byte strings) unchecked, so entry 2 is the next table's first string and the
sheet reads "COPPER ELF". The Client reads the same table the same way (`ClientResources.Gender`), so it shows
the same text.

## Restoration at the race limit — P, E (s22), D (s24)

The race limit is START 0x3AF7, called inside the class loop with the class's **current** level L (before the raise)
and the current Strength byte 0x10; it answers "stopped" only on the exact (race, class, L, Strength) rows below, so
it is an equality, not a ceiling. The experience table stops a class too: XP[L + 1] must be a positive dword below
10,000,000, and PoR's rows hold −1 past each class's top level (cleric 6, fighter 8, magic-user 6; the thief's top 9
reads 0 past its row; classes 1, 3, 4 and 7 hold −1 from level 2 up). For a drained target (0x74 > 0), by case:

- **One level below the limit** (L = limit − 1, the usual drained character): not stopped; the level comes back,
  experience rises to XP[L + 1] when below it (never lowered), then 0x2AA87 (base THAC0 0x2D, saves 0x6D–0x71, cleric
  and magic-user slots and newly known cleric spells, the fighter's 0xA1, thief skills).
- **At the limit** (single class, or every class stopped or at its top): no class qualifies. The hit points still come
  back (0x32, 0x11B, 0xB1 + g; 0x75 − g; 0x74 − 1), the level does not, the uninitialised index takes the + 1
  (DOSBox-X: 8 → gender 0x9E + 1, so a male becomes female and a female shows "COPPER"; seen on two party members, s24), and experience below 10,000,000 becomes 10,000,000.
  0x2AA87 still runs (nothing changes for the unchanged levels unless those bytes were stale). The same happens at a
  class's top level for any race (a human cleric 6, fighter 8, magic-user 6, thief 9).
- **Multi-class with one class at its limit**: that class is skipped and another gains the level (the scan in class
  order takes a class whose level is not above the pick's and whose next threshold is strictly lower), e.g. a half-elf cleric 5 / fighter 6 with Strength 17 or 18 → fighter 7 (with
  Strength 16 the fighter's 6 is a limit row too and the residue path runs).
- **Past the limit** (L above the row, e.g. a gnome or halfling fighter 7 with any Strength, an elf fighter 6 with
  Strength 16): not stopped; the level rises like any other, up to the class's top level. The limit also moves with Strength: a
  Strength 18 dwarf fighter is never stopped by the race.
- **Levels 9..13 past the decoded rows**: XP[L + 1] reads the neighbouring tables (the cleric's and magic-user's
  slot rows); for a cleric or magic-user of level 12 that is a positive value below 10,000,000 (131,843 and
  131,588), so those rise to 13; the other levels and classes read 0, −1 or 10,000,000 and more. L ≥ 14 or negative (255): no class.
- No maximum-HP roll, Constitution bonus or stat recalculation (0x110 THAC0, AC) in the routine.

`[?]` Whether a drained character at its limit is reachable in normal play. Level drain (− 1 level, 0x74 + 1),
training (`training.md`: + 1 level, 0x74 − 1, and a class the race stops is refused) and a Restoration that finds a
class all keep level + 0x74 constant, so a single-class character meets its limit row with drain left only when the
row moves (0x10 is the Strength byte as it is now) or, for a multi-class, when drain and raise land on different
classes. `[?]` What writes the 8 (residue, sampled on three paths).

**E**: `tools/scripts/por_restore_oracle.py` runs 0x29C5B with the real START 0x3AF7 and 0x2AA87 (only the message
trapped; the residue slot seeded with DOSBox-X's 8 after the prologue) on a grid of 691 records: every limit row at,
below and above its level with Strength 16/17/18, eighteen multi-classes with one class at its limit, levels 6..14 and
255 for clerics, fighters, magic-users and thieves of five races, drain 1/2/3/255, experience below, at and above the
threshold (fixture `program-v1.3/restoration.json`). `RestorationProgramTests` matches every byte with the seams wired
as `FightKit.WireLevels` wires them; Core needed no change.

## Level stats (0x2AA87) and the race level limit (START 0x3AF7) — P, E (s15)

**0x2AA87** (creature; after a level changes: Restoration, level drain, ADDNPC): 0x2D (base THAC0) := 0; for each
class 0..7 with level L (record 0x96 + class, signed): 0x2D := DS:3C7C[class × 11 + L] when larger (unsigned); 0x73 := L
when larger (signed). Fighter (2): 0xA1 := 3 when L > 6, else 2. Cleric (0), L > 1: slots 0xB2..0xB4 := DS:40BC[L × 3
+ 1..3]; the Wisdom bonus (0x2AC36: Wisdom above 12 and 13 → 0xB2 + 1 each, above 14 and 15 → 0xB3, above 16 → 0xB4,
each only when that slot is already non-zero); every cleric spell (table DS:31FA byte 0 = 0) of spells 1..56 whose
level (byte 1) has slots becomes known (0x32 + spell := 1). Magic-user (5), L > 1: 0xB5..0xB7 := DS:41D4[L × 3 + 1..3].
Then the saves (0x2ACDC): each of 0x6D..0x71 := 20, then the lowest (unsigned) DS:426E[class × 0x2D + L × 5 + save]
over the classes with L > 0. With a thief level 0x9C > 0 (signed), the thief skills (0x2ADB0): 0x77..0x7E (i = 1..8):
base b = DS:3CF3[level × 8 + i], race adjustment r = DS:3D3B[race × 8 + i] (signed); r < 0 and |r| > b → 0; else b + r,
and for i < 6 + DS:3D4E[Dexterity × 5 + i] (signed), all byte sums. The tables are read where the program reads them
(Pascal arrays with bases, so rows overlap). Core: `LevelStats.Apply` (tables `PorSpellData.LevelTables`), wired by
`FightKit.WireLevels` into the fight's and `PorGame`'s effect engines and the VM's (`EclVm.Effects`, for ADDNPC).

**Layout in v1.3's data segment** (s18, read from the bytes; decoded in a module's `rules/levelStats.json` and
`rules/experience.json`, module-format.md §12): the experience table DS:409B is 0x38 bytes per class, of which levels
2..9 hold experience (−1 past the class's top level in PoR); the cleric slot rows (levels 2..6) are class 0's cells for
levels 10..13 and the magic-user's (levels 2..6) class 5's. THAC0 rows hold levels 0..10, saves rows levels 1..9 (a
class's level 9 + 1 is the next class's level 0). The thief level rows run 1..9; race 0's adjustment row is thief level
9's cells, races 1..7 follow, then Dexterity 9..18. The row ranges are inferred from where the next table starts
`[?]`; reads outside them get the neighbouring bytes, as in the program.

**START 0x3AF7** (00BA:2F57; creature, class) → stopped: level L ≤ 0 → no. Dwarf (race 1), fighter: L = 8 with
Strength (0x10) 17, or L = 7 with Strength below 17. Elf (2), fighter: L = 7; 6 with 17; 5 below 17. Gnome (3),
fighter: L = 6; 5 below 18. Half-elf (4): cleric L = 5; fighter L = 8; 7 with 17; 6 below 17. Halfling (5), fighter:
L = 6; 5 with 17; 4 below 17. Anything else: no. Core: `LevelStats.RaceLimitStops`. Every test is an equality on the level (and Strength), so a
level above the row, or a Strength the row does not name (18 for a dwarf), is not stopped (s22, Restoration above).

**E**: `tools/scripts/por_level_oracle.py` runs both routines on 400 made-up creatures (fixture
`program-v1.3/level_stats.json`); `LevelStatsProgramTests` matches every byte and every answer. Since s18 the fixture
also holds the 17 monster records v1.3's ADDNPC can load (ids 24, 25, 27, 36, 88, 103, 104, 107–112, 122 in every set
that has them) after 0x2AA87; the VM's ADDNPC gives the same bytes.

**0x317E1 in 0x2C753 and Animate Dead** (s15): (creature, its square 0x31321 / 0x31349, 1): outside a fight (DS:49F3
≠ 5) yes; in a fight the creature is put on its square (`CombatMap.TryPlace`, moving) and the answer is whether the
square takes it. Core: `EffectHandlers.PutBackOnSquare` (`CanRevive` only overrides it in tests).

## Checked under emulation — E

`tools/scripts/por_spell_oracle.py` runs the v1.3 routines under unicorn on generated creatures (fixture
`tests/fixtures-private/por/program-v1.3/spells.json`, 487 cases; `FightSpellsProgramTests`): record bytes, effect
lists, items, RandSeed (so the draws), and on a combat map the arena cells and the aim. Covered: spells 34 (twice,
then handler 40 through the removal of 0x28), 35, 36, 37, 39, 41, 42, 43, 44, 46, 47, 48, 51, 55, 56, 57, 58, 59, 60,
62, 64, 65, 67; handlers 81, 82, 83, 85, 86. Outside a fight (DS:49F3 = 0); text and drawing trapped; 0x2AA87 and
START 0x1758 skipped, START 0x3AF7 answers "no limit" (the seams do the same). Restoration with the real limit and level stats:
`por_restore_oracle.py` ("Restoration at the race limit"). Handlers 73–77, 84, 121 need the
fight's slots and attack: hand tests (`EffectHandlersHoldsTests`). Magic Missile: levels 0–12 and the item path.

## DOSBox — D

`fight_moves` s02 → s04 (the goblin fight of save A): the elf magic-user of level 1 (list index 2) casts Sleep at the goblin on
26,12. The captured positions put 9 creatures in the 3 × 3 squares around it (the fighter at list index 0, the caster
herself, six guards, the leader). Replayed from s02's RandSeed (`FightSpellsTests.Sleep_cast_matches_DOSBox`, the map's target list given
as those 9): exactly 5 draws to the end of the cast (s03's RandSeed) — 4d4 = 11, then the caster's elf resistance
(handler 107, 1d100 = 17 ≤ 90: unaffected); the other 8 asleep with records 0x35 / 5 minutes / param 1 / no removal
call; the caster's memorised Sleep (slot 0x2B) cleared; the caster's initiative 0. That also confirms the cast-time-0 path, the target
pick of mode 9 (radius 1), the class-9 immunity in 0x2C540, the duration (per level 5 × level 1) and that Sleep has
no saving throw. The 30 draws to s04 are the two picks (15 creatures each) of the next turns.
