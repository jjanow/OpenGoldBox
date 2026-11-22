# ---- hand-ranked gap lists (exec'd at the end of render.py) ----
GAP_PLAY = [
    ('O:21aed', 'read whole s49: the entries follow the record and DS:49F3 = 5 only, the same for every caller (combat-menu.md "View / Use"); Spells shows the memorised list from seven callers, the party menu\'s View (0x19D23) still gives the Client\'s notice'),
    ('O:18668', 'Memorize / Scribe page buffer allocation (memory only; likely nothing to port)'),
    ('O:1638e', 'Alt-X debugging switch (needs the start word; no effect in normal play)'),
    ('S:6', 'main program: the order is in `start-menu.md` §1 and Program.cs, cited only inside'),
]
# rows closed in s49 (W or N now): their hand notes, shown under "Closed in s49"; prune at the next session's rebuild
GAP_DONE = [
    ('O:27c38', 'done s49: read whole, oracle `por_spellcast_oracle.py` (memorised, item and scroll casts, aim abort, combat-only questions; Core driven out of a fight, the fight branch checked against the program\'s rules only); bit 7 of a scroll pick is not kept; out-of-fight branch `CampCast.Run`, fight branch `FightSpells.CastNow` + `FightMenu.CastNow` (the loot menu\'s D caller 0x6605 not wired in the Client)'),
    ('O:127fb', 's49: read whole and run under emulation with its callees and the trigger-0x12 effects (`por_round_oracle.py`, `RoundProgramTests`); the C# already matched'),
    ('O:9938', 's49: read whole, run under emulation on every initiative byte and after real round starts (`RoundProgramTests`)'),
    ('O:12f0d', 's49: read whole with the guards 0x12E22, the callers\' hand-off and 0x2B9D1, run under emulation (`RoundProgramTests`); the step is now drawn inside the routine (`Fight.StepDrawn`)'),
    ('O:14c0f', 'done s49 (P3): ported whole, oracle `por_cast_oracle.py` (ids 0..67, ai 0 / 1, the list 0x275B4 kind 0); `[?]` left: ids above 67 read past the table (`spells-effects.md` "Combat cast")'),
    ('O:f91e', 's49: ported whole, by execution (`por_handler_oracle.py`, bodies under the clouds)'),
    ('O:10ad2', 's49: by execution; the range word is stack residue, 0x3E58 at 18 of 18 DOSBox-X gazes (no range in practice; sampled, not proven)'),
    ('O:2a0f1', 's49: whole routine rechecked, by execution (the pick at DS:6B84 entry 0 scripted)'),
    ('O:11134', 's49: by execution inside the saving throw 0x2BC4D; Constitution outside 4..20 adds stack residue that varies (DOSBox-X 0x1F, 0x00), 0 taken `[?]`, reached after enough temple raises (Con − 1 each, 0x4B85) or handler 7 at Con 20'),
    ('O:112e5', 's49: as 90'),
    ('O:282bd', 'done s49: other levels keep DS:4845 as Enlarge, Sleep or Dispel Magic last left it, shared for the run (`spells-effects.md` "Spell globals"); start value `[?]` (uninitialised data)'),
    ('O:27466', 'done s49: read whole, oracle `por_spellcast_oracle.py`; signed cleric level; DS:4843 / DS:476A overflows `[U]` (`item-format.md`)'),
    ('O:27534', 'done s49: read whole, oracle `por_spellcast_oracle.py` (`item-format.md`)'),
    ('O:4864', 'W by execution s49 (`por_venue_oracle.py`, VenueProgramTests): DS:681F stops the removal handlers\' re-add (shop-format.md "Temple")'),
    ('O:22f8c', 'W by execution s49 (`por_venue_oracle.py`, VenueProgramTests; DOSBox-X s42)'),
    ('O:23819', 'W by execution s49 (`por_venue_oracle.py`, VenueProgramTests; Core PartySelection); other extended keys → first member not wired in the Client (shop-format.md "Party selection")'),
    ('O:202bb', 'read s49 (ecl-format.md NEWECL): disk prompt only; a module holds every script, so it never runs. Not ported by design'),
    ('O:24331', 'done s49 (P3): a text routine, not the clock (kind pres); ported (`CampTime.TwoDigits`), spec in `camp.md`, oracle for 0..255'),
    ('O:1e294', 'read s49 (start-menu.md §5): disks and paths; the Client makes its own save folders. Not ported by design'),
    ('O:b4', 'read s49 (start-menu.md §1): memory handler set-up. Not ported by design'),
    ('O:4a', 'read s49 (start-menu.md §1): reached through DS:4414 from GetMem (START 0xB343), not dead; .NET has no fixed heap. Not ported by design'),
    ('S:228c', 'W by citation after s49: the C# now names the entry (FightSpells.CombatCastStep)'),
]
GAP_PRES = [
    ('S:77f8', 'text window: typing and paging; open `[?]` on the 255-character case (PLAN "Next" (1))'),
    ('O:2d669', 'list window and its never-cited helpers 0x2C936, 0x2CFE0, 0x2D073, 0x2D1A6, 0x2D1F2, 0x2D238, 0x2D2D7: the Client rebuilt the pages from DOSBox-X (s45–s46)'),
    ('S:3379', 'screen redraw by case (hall, camp, loot, shop): two `[?]` and three observed-only lines'),
    ('O:2dcf4', '3D / overland redraw: marker and backdrop seen in DOSBox-X; the Amiga marker after a same-area Load is open (PLAN "Next" (2))'),
    ('O:239d6', 'spell list window: four `[?]` lines; the Client draws its own page'),
    ('O:2cc8c', 'menu routine (every horizontal menu): one `[?]` line; its key handling is run by the oracles but its drawing is not compared'),
    ('S:1307', 'party list redraw and its colour helpers 0x148F, 0x1513, the creature panel 0x1583, the icon flash 0x257E, the View text helper 0x1C4D: run under the oracles, drawing compared only in DOSBox-X'),
    ('S:2b8b', 'effect message and pause'),
    ('O:2eef4', '3D view internals 0x2E8F7, 0x2EA6B, 0x2EB08, 0x2EEEA never cited, the piece draw 0x32392 cited only inside: the view is pixel-equal to DOSBox-X from the formats (`view3d.md`), so only a check'),
    ('O:20d50', 'after a load or script switch, never cited: overland / picture set-up (calls 0x2DE38 and the picture draw)'),
    ('S:4751', 'pictures, START segment 0464 (0x4640–0x6C72): read, decode, draw, recolour, free; the importer decodes the formats and the Client draws them (`por-art-formats.md`); only some draw entries are cited'),
    ('O:1e468', 'combat icon slots and their builders 0x1E315, 0x1CE5E, 0x1CF3D (icon editor preview, recolour)'),
    ('O:32123', 'text page with a key prompt on the level-drain path, never cited (trapped by the spell oracle)'),
    ('O:2e0ce', 'picture frame draw / free (0x2E0CE, 0x2E55D) and the sprite renderer 0x2E7D6: spec lines only'),
    ('S:8c85', 'sound API (cue 0x8C85, device set-up 0x8C5B, driver start 0x8BBC and helpers): the driver itself is oracle-checked, the game-side calls are spec only'),
    ('S:4354', 'start-up configuration (video, sound, installer lines), read in part'),
    ('S:7d3b', 'text helpers 0x7090, 0x7101, 0x7130, 0x7147, 0x720C, 0x7D3B never cited'),
]
G = []
G.append('## Gaps on the title-to-ending path')
G.append('')
G.append('Ranked by how much of play they can change (rules, state, branching), then by size and callers. Each line '
         'is a candidate [play] item: read the routine whole, port every branch, add an oracle check where the '
         '`por_*_oracle.py` pattern fits, then confirm in DOSBox-X.')
G.append('')
G.append('### Play (rules, state, branching)')
G.append('')
G.append('| # | Routine | Role | St | Why it is open |')
G.append('|---|---|---|---|---|')
for i, (k, note) in enumerate(GAP_PLAY, 1):
    G.append('| %d | %s | %s | %s | %s |' % (i, addr(k), role(k), ST[k], note))
G.append('')
G.append('### Presentation only (drawing, text, sound, input timing)')
G.append('')
G.append('| # | Routine | Role | St | Why it is open |')
G.append('|---|---|---|---|---|')
for i, (k, note) in enumerate(GAP_PRES, 1):
    G.append('| %d | %s | %s | %s | %s |' % (i, addr(k), role(k), ST[k], note))
G.append('')
G.append('### Closed in s49')
G.append('')
G.append('Rows that left the ranked lists this session, with the lane notes (their status stays in the unit tables).')
G.append('')
G.append('| Routine | Role | St | Note |')
G.append('|---|---|---|---|')
for k, note in GAP_DONE:
    G.append('| %s | %s | %s | %s |' % (addr(k), role(k), ST[k], note))
G.append('')
G.append('Not gaps: the N rows (DAX reader START segment 0802, disk numbers, memory release, the code wheel) and the '
         'library segments, which the module format and .NET replace.')
G.append('')
G.append('### Statuses set by hand')
G.append('')
G.append('| Routine | St | Why |')
G.append('|---|---|---|')
for k in sorted((k for k in OV if OV[k].get('status') and k in rows), key=lambda k: (k[0], rows[k]['addr'])):
    G.append('| %s | %s | %s |' % (addr(k), OV[k]['status'], OV[k].get('why', '')))
G.append('')
missing = [k for k in rows if ST[k] in 'PU' and KIND[k] == 'play' and k not in dict(GAP_PLAY)]
print('play gaps not ranked:', [addr(k) for k in missing])
print('pres gaps not ranked:', [addr(k) for k in rows if ST[k] in 'PU' and KIND[k] == 'pres' and k not in dict(GAP_PRES)])
print('ranked but not P / U:', [addr(k) for k, _ in GAP_PLAY + GAP_PRES if ST[k] not in 'PU'])
print('closed but still P / U:', [addr(k) for k, _ in GAP_DONE if ST[k] in 'PU'])
body = open(W + 'ledger_body.md').read()
i = body.index('## ECL opcode handlers')
doc = body[:i] + '\n'.join(G) + '\n' + body[i:]
open(ROOT + 'docs/re/routine-ledger.md', 'w').write(doc)
print('written', len(doc))
