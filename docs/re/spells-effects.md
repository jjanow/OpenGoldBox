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
| 6828 + 4·id | effect handler table (ids 1..127 effects, 128..139 item codes; built at 0x11DFD) |
| 6B84 + 4·id | spell routine table (id 0 = target pick: 0x27AA9 outside a fight, 0x148BE in one; built at 0x2A42B) |

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
dropped-creature removal, class 0x0D, and still out of play it leaves the map (0x3154B).

**Status** (0xECB3, creature, status, message): message; a creature already dead, stone or gone (6, 7, 8) is left;
else status, out of play, HP 0, the dropped-creature removal, class 0x0D, and still out of play it leaves the map.
Team counts are not touched (the next count does it).

**Healing** (0x2C647, creature, amount, bypass): only for status 0, 1, 4, 5 (set at CS:0x173D). Below full HP with
bypass: healed. Otherwise rot (0x32) blocks it, and so does bypass at full HP. HP + amount (byte), at most 0x32; an
out-of-play creature that is dying becomes unconscious, and an unconscious one outside a fight runs handler 0x4E
(removed, no record). START 0x2EEA after a heal is only the "healed" message.

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
| 28 Mirror Image | 0xF61B | **1d(param + 1)** always; > 1 with a spell being cast and one target → damage and effect nullified, an image lost (param − 1; at 0 the effect goes). DS:681B is written only by the cast (0x27EC7 / 0x27ED9) and handler 84 (0x10BCB), so as read a weapon hit is never absorbed (not yet checked in DOSBox) |
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
| 78 | 0x10634 | revive with current HP (0x2C753); failing, re-add 0x4E for 1 minute (lock) |
| 79 | 0x10674 | element 9; target takes 2d10 non-weapon damage (count to 681C) |
| 80 | 0x106A7 | element 0x10; target takes 1d4 |
| 87 | 0x11047 | target without rot gets 0x32 (14400 min) and 0x39 (43200 min, param 1d6 × 16 + 15), removal on (1d6) |
| 89 | 0x110EB | round 0 and attack roll 0 → param AND 0x0F; else first attack (bit 4 clear) → attack roll − 1, bit 4 set |
| 90 / 97 | 0x11134 / 0x112E5 | save type 0 resp. 4 or 2 → save + Constitution bonus (4–6: 1, 7–10: 2, 11–13: 3, 14–17: 4, 18–20: 5; else an uninitialised byte `[?]`) |
| 91 | 0x111B1 | spell 15 → nullified (message); else element bit 2 → nullified |
| 93, 114, 118 | 0x111FD, 0x116C8, 0x117BE | element bit 0 / 2 / 1 → damage ÷ 2 |
| 94, 115 | 0x11217, 0x116E2 | attacker's missile with base byte 7 AND 0x81 (resp. byte 7 = 0 or bit 0) → damage ÷ 2 |
| 95 | 0x1126B | byte 4 cleared; in play → dies |
| 96, 119, 123 | 0x112A6, 0x117D8, 0x119E3 | need magic / silver weapons: damage 0 (or ÷ 2 for silver in 123) by the missile's plus and word 0x31 |
| 98 | 0x1135B | HP + 3, at most max |
| 99 | 0x1138B | dying with +0x0E < 6 → n = 6 − it; unconscious → 6; revive with n HP → 0x5F for 1d4 + 1 min, then this record removed (1d4 after the revive) |
| 100 | 0x11434 | element bits 0 and 4 clear → 0x66 for 3d6 minutes (removal on) |
| 101 | 0x1146F | no 0x62 and no 0x3B → 0x3B for 3 minutes (removal on) |
| 102 | 0x114C1 | revive with max HP; failing, re-add 0x66 for 1 minute (lock) |
| 103 | 0x1151A | byte 4 cleared; out of play, status 3 (0x2BDF0) |
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
| 135 | 0x11D8F | Str < 19 → unreadied |
| 137 | 0x11DD5 | removed → effect 0x17 removed |
| 40, 73–77, 81–86, 88, 121 | | `[?]` not modelled: arena cells, holds and blood drain (combat slots), fear, gazes, level drain, breath (no table entry), acid |

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
begins casting: combat record +0 = spell, initiative − delay when above it, else 1. On its later turn the menu
(0x9B21) and the AI cast +0 (0x27C38) and the actions end. Damage before that clears +0 (0x12AF4 / 0x2C22E).

**Cast** (0x27C38, in a fight): the target pick (DS:6B84 = 0x148BE). Not picked: the AI loses the spell at once
(the menu asks first): message, memorised copy removed. Picked: facing and animation, the caster's invisibility goes
(0x2BEBC), the current character's first memorised copy is removed (START 0x202B; slots 0x17..0x2B, exact byte;
not while an item is used), DS:681B = spell, the routine runs (DS:6B84 + 4·spell), DS:681B = DS:6820 = 0.

**Target pick** (0x148BE): DS:6820 = 0, list cleared, caster's square to DS:6DB9/6DBA. Mode = area AND 0x0F:
- 0: the caster.
- 0x0F: aim at a creature (0x14611, no bare square); the caster's +0x0A creature, else the list around the aimed
  square with radius `(area AND 0x0F) shr 4` = 0; DS:6820 = 1.
- 8..0x0E: aim (bare square allowed); the target list around it with radius `area AND 7`; DS:6820 = 1.
- 1..7: (area AND 3) + 1 aims, each a creature not already taken (a repeat is refused with a message, or skipped
  for the AI); an aborted aim uses up a pick. Empty list → not picked.
The aim itself and the range check are the menu's / AI's (0x14611 `[?]`).

**Resolution** (0x278DB, spell, strength, removal flag, damage, element, message): element mask = element (0
without damage); nothing without targets; strength 0 → the casting level. Per target in list order: a save (type
from the table, modifier 0) when the save byte is set; a range-255 spell recalculates the target, runs its class
0x0B and needs the caster's attack roll (0x2BBA1) against AC byte 0x111 — a miss zeroes the damage **for this and
every later target** and counts as saved; damage > 0 → non-weapon damage with reduction = the save byte; an effect
id → 0x2C540 for the duration (dice rolled per target for the dice-duration spells). Element mask 0 at the end.
Draw order per target: save 1d20, attack 1d20, duration dice; and inside: handler draws (mirror image).

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
| 15 Magic Missile | 0x28451 | damage (level ÷ 2)d4 + level ÷ 2 (count to 681C), element 8 — **0 at level 1** (as read; not yet checked in DOSBox) |
| 20 Shocking Grasp | 0x284DF | damage level + 1d8, element 0x0C; range 255 → attack roll |
| 21 Sleep | 0x28533 | DS:6820 = 1; budget 4d4; cost by hit-dice byte 0x73: ≤ 1 → 1, 2 → 2, 3 → 4, 4 → 6, 5 → 10 (race byte 0) or 20, else 20; animated, already asleep or unaffordable targets leave |
| 23, 49 Hold Person | 0x2866F | modifier by target count (1 → −2 for 23 / −3 for 49, 2 → −1, 3–4 → 0); from the last target: save; type > 1 or size > 1 counts as saved; effect for the duration, strength = level, no removal call |
| 26 Slow Poison | 0x28873 | animated target dropped; poisoned target: HP 0 → 1, resolution (strength 255, removal on), handler 0x4E (removed), effect 0x0F 10 minutes |
| 27 Snake Charm | 0x28927 | budget = caster's HP; every snake (type 0x0E) in the creature list whose HP fit is taken |
| 28 Spiritual Hammer | 0x289DB | resolution with removal on; handler 0x17 on the first target |
| 32 Mirror Image | 0x28AA2 | strength 1d4 |
| 5, 6, 7, 8, 11, 16–19, 22, 24, 25, 29–31, 33, 50, 52–54 | shared | plain resolution (the table's effect) |
| 34–48, 51, 55–67 | | `[?]` not read yet (stinking cloud, fireball and lightning need the map) |

**Castable** (`FightSpells.Castable`): with cast allowed (+1), every distinct memorised id (bit 7 clear) whose "when"
byte is not 0. `[?]` The menu's own list (0x239D6) is not read.

## Where the data is (`PorSpellData`)

- Spell table: the immediate of START 0x3262's `mov cl,4 / shl di,cl / mov al,[di+imm] / cmp al,0 / jne`.
- Name lists of handlers 18, 26, 47: the immediates of `mov cl,4 / shl di,cl / add di,imm / push ds / push di /
  lcall` in GAME.OVR, in that order; handler 48's two names: the Pascal strings right before its code.

## Open `[?]`

- The stat recalculation (above); START 0x2197 (where a created item goes, 0xC7); the camp/out-of-combat cast
  (0x27AA9 target pick); the spell menu (0x239D6) and the aim (0x14611); the map target list 0x3045B (implemented by
  the map code through `IFightTargetList` / `IFightAuraRange`; without it auras in a fight are not found and area
  spells fall back to the map distance with a diagnostic).
- Effect expiry: the clock (0x241A1) counting effect minutes down; in a fight one round is one minute.
- Handlers 40, 73–77, 81–86, 88, 121; spell routines 34–48, 51, 55–67.
- Everything else here is **P**; only the Sleep cast below is **D**.

## DOSBox — D

`fight_moves` s02 → s04 (the goblin fight of save A): the elf magic-user of level 1 (list index 2) casts Sleep at the goblin on
26,12. The captured positions put 9 creatures in the 3 × 3 squares around it (the fighter at list index 0, the caster
herself, six guards, the leader). Replayed from s02's RandSeed (`FightSpellsTests.Sleep_cast_matches_DOSBox`, the map's target list given
as those 9): exactly 5 draws to the end of the cast (s03's RandSeed) — 4d4 = 11, then the caster's elf resistance
(handler 107, 1d100 = 17 ≤ 90: unaffected); the other 8 asleep with records 0x35 / 5 minutes / param 1 / no removal
call; the caster's memorised Sleep (slot 0x2B) cleared; the caster's initiative 0. That also confirms the cast-time-0 path, the target
pick of mode 9 (radius 1), the class-9 immunity in 0x2C540, the duration (per level 5 × level 1) and that Sleep has
no saving throw. The 30 draws to s04 are the two picks (15 creatures each) of the next turns.
