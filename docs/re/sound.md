# Sound and music (Pool of Radiance)

Status: DOS v1.3 read in the program (s19) and checked under emulation; Amiga v1.0 music and effect players decoded (s25). Marks:
**P** read in the program, **E** checked by running the program's own routines under emulation
(`tools/scripts/por_sound_oracle.py`, unicorn), **D** checked against DOSBox-X, `[?]` not settled. Addresses:
START = offset in the EXEPACK-expanded `START.EXE` load image (segment 0 = offset 0); `CS:xxxx` = offset in the
sound driver's code segment (v1.3: segment 0x08AE, image 0x8AE0); OVR = `GAME.OVR` file offset; DS = data segment.
The talonius source reconstruction (prior-art.md §3d) was used as a map only; every fact below was read in v1.3.

## 1. What PoR DOS plays

- **Devices** (P): the second line of `POOL.CFG` (written by the installer) picks the sound: `T` Tandy SN76489
  (sound mode 0), `P` PC speaker (mode 1), anything else off (mode 2). The mode byte is DS:5372; DS:5373 keeps the
  configured mode while sound is toggled off. No AdLib, no Sound Blaster, no music: PoR DOS plays **short sound
  effects only**, synthesised by its own timer-driven sequencer in START.EXE. There are no sound files; the data is
  in the program.
- **Ctrl-S** toggles sound (the key reader: mode → 2 with the stop command; back to the saved mode). Leaving the
  game sends the stop command (START 0x8990). The toggle happens on GetKey's first read, before any extra bytes drain
  from the buffer. The Client uses the same ordering when it reads game keys.

## 2. The driver (START.EXE, one unit; P, E)

### 2.1 Entry points

| CS | What |
|---|---|
| 0x0000 | Sound time: DOS Get Time (INT 21h 2Ch) as centiseconds since midnight |
| 0x006C | Sound delay(cs): busy-waits until the sound time reaches start + cs (a clock that goes back ends it) |
| 0x00DC | **Sound command(cmd)**: nothing when the mode is 2. With auto-stop on (DS:5374 ≠ 0) it installs the timer first. cmd 0: silence both devices and close the output gate; 1: open the gate; 255: silence both devices; else tune **cmd − 1** on the configured device (mode 1 → speaker programmer, mode 0 → Tandy programmer). With auto-stop on it then waits **sound delay(10)**, silences both devices and uninstalls the timer |
| 0x017B | Sound suspend: keep the mode in DS:5373; unless off, install, silence, uninstall |
| 0x01A5 | **Set auto-stop(n)**: unless off: n > 0 and auto-stop was 0 → uninstall the timer; n = 0 → install it (it stays resident); DS:5374 = n |
| 0x01D7 | Driver data starts (below) |
| 0x137C | Timer install: INT 8 vector to CS:13EA, PIT channel 0 divisor **5041** (0x13B1). The control byte written first (0xB6) selects channel 2; the divisor goes to port 40h, so channel 0 keeps the BIOS's lo/hi mode |
| 0x1391 | Timer uninstall: old INT 8 back, channel 0 divisor 65535, port 61h bits 0–1 cleared, all four SN76489 attenuators to 15 |
| 0x13EA | **Timer interrupt**: (an exit countdown, 0 in PoR); unless busy, tick the speaker voices then the Tandy voices; then the BIOS-chain count (CS:01D7, starts at 13): when it reaches 0 it is set to **13** and the BIOS handler runs (which sends its own EOI), else EOI |
| 0x1477 | Speaker tick |
| 0x14EC | Tandy tick |
| 0x157A | Voice step |
| 0x160B | Stream interpreter |
| 0x1742 / 0x17AE | Start a tune on the speaker / Tandy (argument: tune id) |
| 0x181A | Output gate (byte CS:020D, 1 at start) |

- **Tick rate**: 1,193,182 / 5041 = **236.69 Hz** (4.22 ms). 13 × 5041 = 65,533 ≈ 65,536, so chaining the BIOS
  every 13th tick keeps the DOS clock at its 18.2 Hz. The voices advance only inside this interrupt.
- Start-up (OVR 0xEA29): auto-stop = 0, then **set auto-stop(100)**, so auto-stop is **on** by default; the
  configured mode is kept by sound suspend (START 0x45B2).

### 2.2 Data (CS:01D7 – CS:137B, read by the importer; §4)

| CS | What |
|---|---|
| 0x01D7 | BIOS-chain count (byte, 13) |
| 0x01FF | Current tune (word; 0 = silent: both ticks then only clear the speaker gate / mute the Tandy) |
| 0x0201–0x0207 | Stream interpreter state: running voice, device voice base, device note table, the one-level call return |
| 0x020D | Output gate |
| 0x022D | Speaker voice blocks, 4 × 0x30 (four spare blocks follow, unused) |
| 0x03AD | Speaker note table: 12 PIT divisors, the lowest octave from C (≈ 32.7 Hz) |
| 0x03C5 | The speaker voice that drives the speaker this tick |
| 0x03C7 | Tandy voice blocks, 4 × 0x30 (four spare blocks follow) |
| 0x0547 | Tandy note table: 12 SN76489 periods shifted left 6, the lowest octave from A |
| 0x055F | Song table A (speaker): 21 rows (tunes 0–20) × 4 stream offsets, voice 0–3; 0 = leave that voice alone |
| 0x05A7 | Song table B (Tandy), same shape |
| after | LFO waveforms (a 256-sample sine, a zero run, a constant run, a 512-byte noise table), then the tune streams and envelopes |

**Voice block** (words): +0 duration (ticks to the next stream step; 0 = idle), +2 stream offset (0 = ended),
+4 period, +6 period slide (added every tick), +8 output period (period + LFO; what the device gets), +0A level,
+0C level slide, +0E tempo (low byte > 1 multiplies note durations), +10 (unused), +12 transpose (added to a note's
pitch), +14 gate length (when it runs out the envelope jumps to its 5th segment), +16 envelope base, +18 envelope
position, +1A envelope wait, +1C LFO table, +1E LFO phase, +20 LFO rate, +22 LFO depth (signed), +24 LFO wrap,
+26/+28 loop counters, +2A..+2F unused.

### 2.3 How a tick works

- **Start a tune** (CS:1742/17AE): current tune = id; for each voice whose row entry is non-zero: clear the block,
  stream = the entry, duration = 1 (it steps on the next tick).
- **Voice step** (each voice with duration ≠ 0): level += level slide; period += period slide; phase = phase + rate
  (when the sum is 0 the LFO is skipped: no offset, phase not stored), minus wrap once when ≥ wrap (unsigned); the
  sample at table + (phase sar 4) times depth (signed 8 × 16 bits) gives an offset = the product's high word; output
  period = period + offset. Gate length − 1 when set; at 0 the envelope restarts at its 5th segment (position 0x10,
  wait 1). Duration − 1; at 0 the stream runs. Then envelope wait − 1 when set; at 0 the envelope advances: each
  segment is (value, ticks); ticks = 0FFFFh means "level = value" (value 0 also stops the level slide) and the next
  segment follows at once; otherwise level slide = value and wait = ticks.
- **Stream bytes**: 0FAh reset the voice (all fields but duration, stream, tempo, gate length, loop counters);
  0FBh return from the one-level call; 0FCh w call; 0FDh w continue on voice block w of the same device, reset
  (the rest of the stream writes that block; the stream pointer still goes back to the first voice); 0FEh f w loop:
  jump to w when the word at +f is 0 or decrements to non-zero (a counter of 0 loops for ever); 0FFh f w store word w
  at +f, and f = 0 (duration) yields; a duration of 0 ends the stream. Bytes < 0FAh are **note records** of 3 bytes:
  voice index in bits 5–7 of the first, last-record flag (bit 7) and pitch (0–126, 127 = rest) in the second, the
  duration in the third. Records run until one has the last flag. The duration (× tempo when tempo > 1) goes to the
  running voice; only a record for voice index 0 sets a pitch: that voice gets duration and gate length, envelope
  restarted, and period = note table[(pitch + transpose) mod 12] >> octave (the shift count masked to 5 bits).
- **Speaker output**: the first voice that, after its step, has a level and a duration drives PIT channel 2: its
  output period is written to port 42h (low, high) and port 61h bits 0–1 = level AND 3 (both set = the timer's
  square wave reaches the speaker). No such voice, or gate closed, or tune 0: bits 0–1 cleared.
- **Tandy output**: every tick, all four voices: tone 0–2 get the period (output period >> 6, 10 bits: latch byte with
  the low 4 bits, then the high 6) and the attenuation (top 4 bits of 0FFFFh − level); voice 3 is the noise channel
  (latch 0E0h with the low 4 bits of its period byte as noise control, no second byte). Tune 0 or gate closed: all
  four attenuators to 15.
- Both ticks run on every interrupt whatever the device: in speaker mode the Tandy voices are idle and only muted
  values reach port C0h (nothing listens there on a PC).

### 2.4 Commands, cues and what triggers them (P)

The program never passes tune ids itself: it pushes a word of the **cue table DS:2612** (16 words: 255, 0, 1, 2, 3,
4, …, 14) and calls sound command, which plays tune = word − 1. All callers (exhaustive search of START and GAME.OVR
for far calls to CS:00DC, CS:01A5, CS:017B):

| Cue (DS) | Word | Tune | Played by | Auto-stop |
|---|---|---|---|---|
| 2612 | 255 | stop | leave the game (START 0x8999), Ctrl-S off (0x8A0B) | — |
| 2618 | 2 | 1 | a spell cast in a fight, spell id other than 47 / 51 (OVR 0x27E2E, the cast's flight to the target; a wand's Fireball is id 64, so it plays this); Hold Person, once per further target (0x286FA) | off (resident) |
| 261A | 3 | 2 | a creature's name shown with damage, in a fight (START 0x2C14, the name animation; only when DS:49F3 = 5) | off |
| 261C | 4 | 3 | a creature's name shown with an effect (START 0x2C09) | off |
| 261E | 5 | 4 | the body routine OVR 0x3154B (combat-map.md "Bodies"): in a fight (DS:49F3 = 5) as the body is placed (0x31744), else at once (0x31560) | off |
| 2620 | 6 | 5 | a missile leaves (OVR 0x15016, before the flight animation) | off |
| 2622 | 7 | 6 | **impact**: a melee/missile hit (OVR 0x13EF1, per hit), an attack on a helpless defender (0x13C54); the **bump** of a refused step or failed door (0x176FE) | on |
| 2624 | 8 | 7 | Lightning Bolt (spell 51) cast in a fight (0x27E23) | off |
| 2626 | 9 | 8 | **miss**: an attack with no hit (0x13F6F) | on |
| 2628 | 10 | 9 | **step**: the move one square ahead (0x16E09, start of the move routine 0x16E06); turning left / right (0x171DB / 0x17220; turning around plays nothing); after a command, when the redraw finds x or y changed (area loop 0x4215); a creature's step on the combat map (0x1306D); ECL CALL $BA03 with $03DE = 8 (0x3ACE) | on |
| 262A | 11 | 10 | Fireball (spell 47) cast in a fight (0x27E14); ECL CALL $BA03 with $03DE = 10 (0x3ADE) | off / on |
| 262C | 12 | 11 | a missile arrives (0x1505C, after the flight) | off |
| 2614, 2616, 262E, 2630 | 0, 1, 13, 14 | gate off, gate on, 12, 13 | nothing (no caller) | |

- Tunes 12–20 are never played by PoR (12 and 13 are in the cue table but unused; 14–20 have no cue). The driver is
  shared with SSI's other games of the time `[?]`.
- **Resident (auto-stop off) brackets** (P): set auto-stop(0) … set auto-stop(10) around the fight animations: the
  name animation (START 0x2BB1 / 0x2D07, only in a fight), a missile's flight (OVR 0x15011 / 0x15069: cue 2620, the
  flight, cue 262C), a spell's flight (0x27E08 / 0x27E56), Hold Person's further targets (0x286F5 / 0x2874E) and the
  body routine (0x31554 / 0x317D6). Inside, a tune runs to its end or until set auto-stop(10)
  uninstalls the timer, which silences it. Some tunes never end by themselves (speaker tune 7 loops), so the
  animation's length is the sound's length.
- **Auto-stop on** (everything else, e.g. step, impact, miss, bump): install, start the tune, wait sound delay(10),
  silence, uninstall. The DOS clock moves only when the BIOS handler runs (every 13th driver tick), in steps of 5 or
  6 cs, so the wait ends at the **second** BIOS tick after the start: the tune sounds for (BIOS-chain count at the
  start + 13) driver ticks, 14–26 ticks = 59–110 ms. The count is not reset by install or uninstall: after an
  auto-stop sound it is 13 (so the next one gets 26 ticks), after a resident bracket it can be anything. The program
  also stops while it waits (the busy-wait), so every step, hit or miss takes that long.
- **D (s19, DOSBox-X, PC speaker on, captured from its own null sink; save A in the Slums):** the intro and title
  are silent. A turn left or right: one 27.4 ms pulse (the step tune's first tick writes divisor 0, i.e. half a
  period of 18.2 Hz; the PIT takes the next divisors only at that half period's end, by when the tune is nearly
  over). A step forward: two such pulses, 275 ms apart (the move's cue, the redraw, the area loop's cue). Turning
  around: nothing. A bump: **106 ms** from the first rise to the final drop = 25 tick intervals, i.e. the 26 ticks
  of the model with the count at 13. `[?]` Not measured: the onset delay after the key (the PIT applies the new
  channel-0 divisor when the running BIOS period ends, up to 55 ms).
- **D (s39, DOSBox-X fights, PC speaker and Tandy, each recorded from DOSBox-X's own null sink with `parec`, the
  Client's from its own sink with `client_xvfb.sh audio`, same save and seed 12345; `fight_save.py` saves: a vampire
  MON4 23 with every member on Quick, and ADDNPC 94 + one goblin guard; combat-format.md / combat-menu.md s39):**
  - Heard: steps (a creature's step on the map), impact (hits), miss, the missile's tunes (darts), the body
    (CreatureDowned). Not heard: a spell's cast tune (no member cast under Quick) `[?]`.
  - **Pitch and shape equal** on both devices: speaker step = one 27 ms pulse; miss ~1.19 kHz (half period 18–19
    samples at 44.1 kHz on both sides); impact ~80 ms rising 275 → 595 Hz; body 500 ms at 100–200 Hz plus its tail
    (~0.73 s in all, ending by itself inside the 0.9 s pause at speed 4); missile ~230 Hz (speaker) / ~3 kHz (Tandy).
    Tandy: step ~59 ms 150 → 230 Hz; miss ~105 ms noise; impact ~84 ms; body cut at the pause end (0.90–0.92 s).
  - **Fixed**: the body tune started after the downed picture's pause (Core cued it at the map removal, after the
    display), 0.9 s late; it now starts with the picture (0.17 s after it in DOSBox-X: the picture's drawing time),
    §3 "Hooks". Tandy noise: the Client restarted the noise register on every write of the noise control, which the
    driver makes every tick, so the misses were a 237 Hz buzz (FFT peak 475 Hz) where DOSBox-X plays noise; §3. Tandy
    body: the Client let the tune run 1.37 s past the bracket's end; the bracket now closes at once (§3 "Client").
  - **Machine time, not changed**: DOSBox-X at 3000 cycles spends drawing time the Client does not: a creature's steps
    on the combat map come every ~220 ms (Client ~117 ms: the auto-stop wait alone), a dart's flight and so its tune
    last 0.36–0.50 s (Client 0.18–0.34 s; the moving icon's positions are drawn ~15–30 ms apart plus a ~0.15 s end),
    and a step → impact gap was ~0.05–0.1 s longer. Auto-stop lengths vary by a few ticks with the BIOS-chain count,
    which follows the real time spent in the brackets.
- Tune lengths (speaker / Tandy, driver ticks until silent, from the emulated driver): step 9: 8 / 16; impact 6:
  65 / 20; miss 8: 9 / 60; bump = impact. With auto-stop the impact and miss sounds are cut to 14–26 ticks.

## 3. Engine model (GoldBox.Core `Sound/`)

- `PcSoundDriver` is our implementation of §2.2–2.3 on a 64 KB copy of the driver segment holding the imported data;
  it writes the same bytes to the same ports as v1.3 on every tick for tunes 0–20 on both devices (E,
  `SoundDriverProgramTests`: 600 ticks each, hashed per 25 ticks against the oracle; also a tune started over a
  running one). Tandy tune 18 is not checked: its stream makes the driver read its own code bytes, which the module
  does not hold; PoR never plays it.
- Output: PC speaker = PIT channel 2 in mode 3 (square wave at 1,193,182 / divisor Hz; divisor 0 = 65,536) gated by
  port 61h: bits 0 and 1 set = the wave, bit 1 alone = speaker held high, else low; a DC-blocking filter follows
  `[?]` (the speaker's analogue response is not modelled). Tandy = SN76489 at 3,579,545 Hz (tone = clock / 32 / period),
  2 dB per attenuation step. Noise (s39): the Tandy 1000's NCR 8496 as DOSBox-X has it (MAME's NCR8496 model, which
  DOSBox-X uses on every machine but the PCjr): a 16-bit register (feedback into bit 15, start 0x8000), white noise
  from bits 1 and 5 with the second inverted, output bit 0; a write of the noise control restarts it only when the
  white/periodic bit changes (the TI part restarts on every write, which with the driver's write every tick would
  repeat the noise each 4.22 ms); data bytes for the noise register are ignored (`TandyNoiseTests`). `[?]` The early
  Tandy 1000's SN76496 not checked.
- The auto-stop model: cue → start the tune, run (BIOS-chain count + 13) ticks, silence; in a resident bracket the
  tune runs until the bracket closes (`PcSoundPlayer`). Rendered, a step is one 27.4 ms pulse and a bump 104–108 ms,
  as in DOSBox-X (`SoundRenderTests`).
- **Hooks** (Core `ISoundSink`, called where the program calls the sound unit): the explore loop (`AreaLoop.Sound`:
  the move, turns left/right, the area loop's check after the redraw), the bump (`IAreaUi.Bump`), ECL CALL $BA03 (the
  Client's host), the fight (`FightState.Sound`: impact per hit and on a helpless defender, miss, a creature's step,
  the body routine, a missile's flight, a spell's flight with its own cue for spells 47 and 51, Hold Person's further
  targets, the second missile flight 0x1417E of a weapon of type 0x2F with its own bracket and cues, s26). Not hooked
  `[?]`: the name animation (START 0x2B8B, cues 261A/261C; 10 callers in GAME.OVR: damage and effect messages), sounds
  outside a fight in the body routine.
- **Client**: `--sound speaker|tandy|amiga|off` (settings `Sound`, default speaker), also selectable on the SOUND
  row in Controls. Ctrl-S mutes or restores the configured device for the session. With Amiga selected, the
  Client uses the module's sampled effects if available and falls back to the PC speaker otherwise. Output is
  44.1 kHz mono. The logic thread waits out a DOS auto-stop sound while the UI thread services the stream.
  Under Xvfb/PulseAudio (s32), a step called both cues 114 ms apart and the dedicated null-sink recording
  contained both pulses. `[?]` A resident
  bracket's length is the program's animation. The missile, spell and Hold Person flights are drawn inside it (s25,
  `FightState.Flight`, combat-menu.md "Moving icon"), so their tune sounds for the flight; (s39) the downed picture too: the Client's CreatureDowned display opens the
  bracket, cues the body once the picture is drawn, waits the pause and closes it (OVR 0x31554 / 0x31744 / 0x317D6;
  `FightState.FrontEndPlaysDowned`, the map removal then cues nothing; a drop the Client does not draw, e.g. turn
  undead's destroyed, still cues at the removal). A bracket that stayed open while the Client drew (15 ms or more)
  closes at once, which silences the tune as the timer uninstall does (DOSBox-X s39, Tandy body: cut at 0.9 s); one
  opened and closed with nothing drawn between stays open until its tune ends by itself or the next sound call.
- **D (s39): ECL sound in a load's vector 4.** A scratch save (save A with vector 4 = SAVE 8 → $03DE, CALL $BA03,
  SAVE 10 → $03DE, CALL $BA03, EXIT; the other vectors EXIT) loaded at the party menu (L, B) on both sides.
  DOSBox-X (3000 cycles, PC speaker on, SDL's dummy audio driver, so nothing reaches any sink; the mixer's own WAV
  capture, F12+W): the step tune 9 as one 27.46 ms pulse, then tune 10 (0x3ADE) 109.85 ms after the first rise
  (the step's auto-stop: 26 ticks, the count at 13): a 27.46 ms pulse, then 10.7 / 12.2 / 10.6 ms pulses with 11.9 /
  12.3 / 11.1 ms gaps, 96.3 ms first rise to last fall. The Client (`client_xvfb.sh audio` on its null sink, PC
  speaker): the same pulses to 0.05 ms, the second tune 116.67 ms after the first rise, 6.8 ms later than DOSBox-X
  `[?]` (about 1.6 driver ticks; the Client's start of the second cue after the first one's wait, not measured further).

## 4. Module (`audio/`)

The importer reads the driver from the player's START.EXE (`PorSoundDriver`, found by its code: the timer install,
the BIOS-chain count in the interrupt, the two tick routines, the two tune programmers) and writes
`audio/dos/pcsound.json`: the data bytes CS:01D7 up to the timer install (base64), their origin and the table offsets.
The cue table stays in `rules/program.json`'s data segment (DS:2612; found by its shape). See module-format.md §10a.

## 5. Amiga v1.0 (decoded s25; P, E)

Read from disk A's `program` (41 hunks) with every hunk loaded at its own base and its RELOC32 entries applied;
addresses are hunk + offset. **E** here = the program's own player routines run under unicorn (m68k, 68040 model)
by `tools/scripts/por_amiga_sound_oracle.py`, which records every custom-chip write per vertical blank (private
fixture `tests/fixtures-private/por/amiga-v1.0/sound_player.json`). Two players share the four Paula channels: a
**music player** in hunk 27 with its data in hunk 28, and a **sound-effect player** in hunk 33 with its samples in
hunk 29. Neither is ever run together with the other (below).

### 5.1 Set-up and the interrupt (hunk 33, the resident hunk)

- **Install** (hunk 27 +0x6E78 → hunk 33 +0xD8, called once at start-up, hunk 10 +0x562): silence the effect
  channels (+0x546: their four volume bytes and AUD0–3VOL = 0), then +0x0: AllocMem an Interrupt node, copy the
  75-byte handler +0x82 into memory and **AddIntServer(INTB_VERTB = 5)**: a vertical-blank server, so both players
  tick at the display rate: 50 Hz on a PAL machine, 60 Hz on NTSC `[?]` (which the program expects is not stated;
  the Client uses 50 Hz).
- **Handler** (+0x82): when the long "music on" at +0xCC is non-zero it calls the music tick (hunk 27 +0x78B2); else,
  when INTREQR AND INTENAR has the vertical-blank bit, the effect tick (+0x172).
- **Remove** (hunk 27 +0x6E88 → hunk 33 +0xEE, at exit, hunk 1 +0xA): with music on, PlaySong(0) first, else silence
  the effect channels; then RemIntServer.
- **PlaySong(n)** (hunk 27 +0x6E54, C convention, n on the stack): "music on" := 1, then hunk 33 +0xE6 → hunk 27
  +0x78A0. **PlayEffect(n)** (hunk 33 +0x14E, n on the stack): "music on" := 0 and request byte 0 (+0x176) := n.
  So **an effect ends the music**: the music tick no longer runs (its channels are taken over, the voices still
  playing a looped instrument are switched off by the effect's DMA write) and nothing resumes it but the next
  PlaySong. The music never runs while an effect is wanted.

### 5.2 Music player (hunk 27 +0x78A0 … +0x831E; data hunk 28 +0x16C8 … +0x2C60, samples to the end)

A credit string sits in the code at +0x7C92. **Commands**: PlaySong stores n in the command byte (hunk 27 +0x78C0)
and resets (+0x807E: AUD0–3VOL and the four volume bytes 0, speed counter 0, "playing" (hunk 28 +0x17D5) 0). The
start happens on the next tick (+0x78C2): n = 0 stops (silence, "playing" 0); **1 ≤ n ≤ 7** starts song n; anything
else is **the running state**: every start or stop ends by writing 0xFF into the command byte (+0x8048), so the
`n > 7` branch (+0x79AC) is simply the per-tick sequencer, which does nothing but silence while "playing" is 0.
No caller passes n > 7.

**Data (hunk 28)**, all tables found through the player's own `lea` operands:

| hunk 28 | What |
|---|---|
| +0x16C8 | 32 bytes loaded into d0–d7 at each tick (`movem`), not used `[?]` |
| +0x16E8 | Period table, word per note: note 0 = 0 (rest), notes 1–45 = 1616 … 113 (Amiga periods, 12 per octave) |
| +0x1750 | Voice state: **interleaved**, field f of voice v at +0x1750 + f + v (each field a group of 4 bytes, one per voice). Constants in it: +0 the channel's register offset (0, 0x10, 0x20, 0x30), +4 the voice's arpeggio slot (1, 10, 20, 30), +0x64 its DMA bit (1, 2, 4, 8) |
| +0x17D4 … +0x17F3 | Globals: voice loop counter, playing, **speed** (+0x17D6), speed counter (+0x17D7), vibrato work bytes, arpeggio mask, row position, register offset, slide direction, the **DMA-off mask** (+0x17E0, a bit per voice) and per-voice words: base period (+0x17E2), slide target (+0x17EA), slide step (+0x17F2) |
| +0x17FC … +0x1853 | Per voice: sample start (long), repeat length and repeat offset (words), pattern pointer (long, +0x181C), the current song's four track-list pointers (+0x182C), work longs |
| +0x1854 | Arpeggio buffers (4 × 10 bytes) |
| +0x187C | 16 sample pointers (relocated; samples 0–11 used, 12–15 repeat sample 11's) |
| +0x18B8 | One pointer to 4 zero bytes (+0x18BC): the "silence" a one-shot instrument repeats |
| +0x18C4 | **Instruments**, 8 bytes each (12): sample number, loop flag, one-shot length (word, bytes), repeat offset (word), repeat length (word) |
| +0x1924 | Arpeggio intervals for mask bits 0–7: 3 4 5 7 8 9 10 12 |
| +0x192C | **Envelopes**, 8 bytes each (10): start volume, attack target, attack step, release step, sustain divisor, vibrato (depth in the high nibble, speed in the low), arpeggio mask, flags |
| +0x197C | **Song table**: 16 bytes per song, the four voices' track-list pointers. Seven slots; slot 7 is the start of the pattern table (songs 1–6 are real) |
| +0x19DC | **Pattern table**: 127 pointers |
| +0x1BD8 … +0x2C5F | Track lists and patterns |
| +0x2C60 … end | **Samples**: 8-bit signed PCM (sample 0 at +0x2C60 is 2 zero bytes) |

**Track list** (one per voice; +0x793C): each byte ≤ 0xC0 is a pattern number (the pattern table); 0xFF goes back
to the list's start; any other byte sets the voice's **transpose** := byte + 0x20 (mod 256, so 0xE0 = 0, 0xDF = −1)
and the next byte is read.

**Pattern** (one voice's events; read on a row, +0x7A92). Optional prefixes in this order, each followed by the next
byte: 0xF0–0xFB and 0xFD speed := low nibble; 0xE0–0xEF instrument
:= low nibble; 0xC0–0xDF envelope := low 5 bits; 0x80–0xBF duration := low 6 bits. Then an optional effect:
0x7F slide down a semitone per row, 0x7E slide up; 0x7D m / 0x7C m arpeggio with mask m (the buffer gets the interval
of each set bit after a leading 0, ends 0xFF); 0x7B t p **portamento** to note t + transpose, p = delay (high nibble)
and length (low nibble) in rows; 0x7A d vibrato delay d ticks. Then the **note** (0x00–0x5F; 0 = rest; + transpose).
0xFF anywhere a prefix may stand ends the pattern (the next pattern of the track list is loaded and read at once);
0xFE stops the song (it re-enters the start code with 0: silence); 0xFC s chains to song s. Notes 0x60–0x79 are read
as notes past the 46-entry table `[?]` (none in the data). In the data only speed, instrument, envelope, duration,
0x7A and notes occur, and 0xFE ends patterns 62, 69, 77 and 81 (songs 2–5 stop by themselves; songs 1 and 6 loop);
0xFC never occurs.

**A tick** (+0x79AC; voices 3, 2, 1, 0):
- **Row ticks** are the ticks where the speed counter is 0: a row lasts speed + 1 ticks (the counter is reloaded
  with the speed when it runs below 0, +0x8034). On a row the voice's duration counter drops by 1; with sustain
  divisor 0 a counter reaching 0 sets the voice's DMA-off bit; a counter below 0 reads the next event. So a note
  lasts duration + 1 rows.
- **New note** (+0x7BF8): the voice's DMA-off bit cleared; DMACON := the voice's bit (channel off); the instrument
  (+0x8204) writes AUDxLC = the sample and AUDxLEN = (loop ? repeat offset + repeat length : one-shot length) / 2
  and keeps the repeat part; the envelope (+0x8198) sets volume, attack target and step, release step, sustain,
  vibrato, arpeggio; note + transpose → period (+0x8290); duration counter := duration; vibrato delay counter := the
  voice's 0x7A value (P, E: written only by 0x7A, never cleared by a song start or reset, so a voice keeps its last
  value across notes and songs; 0 until the first 0x7A).
- **Effects** (non-row ticks, and row ticks where the note goes on): with a vibrato and no arpeggio/portamento:
  vibrato (+0x7CD8): step = (period[note] − period[note + 1]) shifted right `speed` times with bit 0 set each time;
  a phase counter walks 1 … depth + 1 and back to 0; output period = base + step × (depth / 2) − step × phase, after
  the delay. Arpeggio (+0x7E02): period of note + the buffer's next interval each tick. Portamento (+0x7E5A, patches
  the immediates at +0x7E93, +0x80F5, +0x817F): between the delay and delay + length rows the base period moves by
  (period[target] − period[note]) / (length × (speed + 1)) per tick, then it snaps to the target. Slides move the note
  by one per row.
- **Envelope** (+0x7F0E; not on a voice whose duration counter is 0): attack adds the step until the volume equals
  the target (exact compare); release starts when the counter equals duration / sustain divisor (divisor 0: at once;
  0xFF: never) and subtracts the release step each tick; at ≤ 0 the volume is 0 and the voice's DMA-off bit is set.
- **Output** (+0x82BE, once per tick after the four voices): AUD0–3PER := the four output periods; DMACON := 0x8200
  | the voices whose DMA-off bit is clear, then the others off; AUD0–3VOL := the four volumes. Then (+0x7FB6) for
  each channel the **repeat part**: AUDxLEN := repeat length / 2, AUDxLC := sample + repeat offset for a looping
  instrument; for a one-shot one the program means the silence pointer (+0x18B8) but indexes it by voice × 4, so only
  voice 0 gets it: voices 1, 2 and 3 repeat from **chip addresses 0, 0 and 2** (the next longs, +0x18BC … +0x18C4),
  a 2-byte loop of whatever low memory holds (a constant: no sound, only a DC level `[?]`).
- **Paula** `[?]` (hardware, not program): a channel switched off and on within one tick restarts from AUDxLC/LEN;
  the LC/LEN written just after the DMA write are latched for the next pass, so a sample plays once from its start
  and then loops its repeat part. The Client assumes this and a PAL clock of 3,546,895 Hz.
- Under emulation (E) the songs use only speed, instruments, envelopes (attack, sustain, release), vibrato and its
  delay; no arpeggio, portamento or slide is ever reached, so those paths (and two cross-voice slips in them: the
  envelope's flag bit 3 writes the portamento target of voice v + 3, and a byte read at +0x4E belongs to another voice) are transliterated but unchecked by data.

### 5.3 Which song plays where (P)

| Song | Played by | Length |
|---|---|---|
| 1 | start-up (hunk 10 +0x580) while disk A's `intro` runs, stopped after it (+0x59A); the **camp** menu (hunk 1 +0x4FBC, stopped on leaving, +0x513C) | loops |
| 2 | **"A battle begins..."** (hunk 13 +0x24E2): the song number is the word at hunk 31 +0x18C0 (2), skipped when the byte at hunk 32 +0x2D1 is non-zero `[?]` (initially zero, and the battle check is its only direct relocated reference) | stops |
| 3 | a new character's name accepted (hunk 10 +0x235C, after the name checks) and an icon accepted ("Is this icon ok?" Y, +0x433E) | stops |
| 5 | the party is killed (hunk 16 +0x46C2; in a fight, hunk 19 +0x102E) | stops |
| 4, 6 | no caller (4 stops, 6 loops) | |

All calls go through each overlay's stub to hunk 27 +0x6E54. Since any effect turns the music off, the battle song
is cut by the fight's first sound.

### 5.4 Sound-effect player (hunk 33 +0x172 … +0x573; tables hunk 33 +0x574 … end, samples hunk 29)

- **Cues**: the callers push a word of the table at **hunk 31 +0x18A8** (13 words: 5, 9, 8, 10, 12, 11, 6, 11, 4,
  7, 0, 2, 2), the Amiga's counterpart of DS:2612 (§2.4), in the same order: +0x18A8 a spell cast (other than 47/51)
  and Hold Person's further targets (hunk 22 +0x2B70, +0x3496), +0x18AA / +0x18AC a creature's name with damage /
  with an effect (hunk 13 +0x5484 / +0x5472), +0x18AE the body routine (hunk 22 +0x1128, +0x1322), +0x18B0 missile
  leaves (no caller found), +0x18B2 impact and bump (hunk 4 +0x351C, +0x3756; hunk 19 +0x2FFC), +0x18B4 Lightning
  Bolt (hunk 22 +0x2B5E), +0x18B6 miss (hunk 4 +0x37D0), +0x18B8 step (hunk 4 +0x2B28; hunk 16 +0x4AB4, +0x541A;
  hunk 19 +0x1F54, +0x29CE, +0x2A52, +0x2FE6), +0x18BA Fireball (hunk 22 +0x2B46; hunk 16 +0x4AD0), +0x18BC missile
  arrives (hunk 4 +0x46D8), +0x18BE unused, +0x18C0 the battle song (§5.3). Which screens hunks 4, 16 and 19 are
  beyond these matches is `[?]`. Impact, bump and miss share effect 11.
- **Effects** (+0x662, 8 bytes each, 0–14): sample, note (index into the 60-word period table at +0x574,
  3424 … 113), parameter, volume, flags (bit 2 switch to the parameter's note once, bit 1 slide the period up by the
  parameter per tick until 3424, bit 0 down until 113: exact compares), duration (ticks), volume decay, repeats
  (0xFF: for ever). **Samples** (+0x6DA: 15 pointers into hunk 29; +0x716: 8 bytes each: length, repeat offset,
  repeat length, loop flag).
- **Tick** (+0x17A): channel 3 … 0: a request byte other than 0xFF starts that effect (+0x1E2: AUDxLC/LEN, period,
  volume; all four channels marked off for this tick, so the sound starts one tick later); else a running effect
  steps (+0x2F4): duration − 1 and the flag effect; at 0 a repeat reloads the duration, after the last the volume
  drops by the decay each tick and the channel stops at < 0 (or at once with no decay). Output (+0x442): periods,
  DMACON, volumes. Every 20th tick (+0x4BA) looping samples get their repeat LC/LEN. Only request byte 0 is ever
  written, so effects play on **channel 0** only.
- The engine's effect player reproduces the same register writes in the same order for all 15 effects over
  600 ticks each (E, `AmigaSoundProgramTests`). The Amiga effect player takes over the music player's vertical
  blank and shares its Paula channels. The DOS-only missile departure cue is skipped in Amiga mode.

### 5.5 Engine and module

- The Amiga overlay import writes `audio/amiga/sound.json` (module-format.md §10a): hunk 28 whole with its
  self-relocations, the effect tables, hunk 29 and the cue words, after checking the players' table operands
  (`PorAmigaSound`).
- Core `AmigaMusicPlayer` is the music player transliterated routine by routine on a copy of hunk 28 (voice fields
  interleaved as in the program, the patched immediates as fields). **E**: for songs 0–6 over 13,000 ticks (past song
  1's loop at tick 12,289) and a song started over another then stopped, it writes the same custom-chip registers
  with the same values in the same order as the program (`AmigaSoundProgramTests`). Songs 2, 3 and 5 last 385 ticks
  (7.7 s at 50 Hz), song 4 769; songs 1 and 6 loop after 12,288 and 7,680 ticks.
- `PaulaSynth` turns the writes into sound (the `[?]` hardware model above, mono); `AmigaMusic` ticks the player at
  50 Hz. Client: with the module's Amiga data and sound not off, the Amiga intro (Best / Amiga) starts song 1 and
  stops it when it ends (`ClientAudio.PlaySong`), after the intro's closing 3 s (`intro` hunk 1 Delays 150 ticks
  before returning; the program reaches PlaySong(0) at hunk 10 +0x59A after Execute returns, s27). Departure: on the
  Amiga the song also plays through hunk 25 +0x12A8 (called between Execute and +0x59A; read s28: the Amiga's copy of
  DOS's start-up 0xE600, allocations, start values and the start-up loads with the loading message on row 24,
  start-menu.md §1a "After the intro"; no sound call but the out-of-memory quit, hunk 1 +0x6E), so it stops after the disk time `[?]`; the Client, whose
  loads take no time, stops it at the end of the 3 s. The camp (1), battle (2), name and icon acceptance (3), and
  party defeat (5) use these same hooks under Best / Amiga presentation or when the selected sound device is
  Amiga. Amiga effects interrupt a song until another song starts. The camp exit stops the camp song.

### 5.6 FS-UAE check (s39)

The camp song (1) recorded from FS-UAE (A500, PAL, Kickstart 1.3; por-art-formats.md §4 "Amiga in FS-UAE") through its
own null sink with `parec`, against the Client's camp under `--art amiga --sound amiga` (25 s each, from the camp's
start in the Client, some seconds in on the Amiga). A semitone-band spectrogram cross-correlates best at tempo ratio
1.00 (0.133 against 0.06–0.07 at 0.83, 0.9, 1.1, 1.2), lag 8.7 s: the Client's 50 Hz tick is the PAL machine's rate
(**E**; which rate the program expects is still not stated, but a PAL A500 plays it as the Client does). Differences
**[?] not built**: FS-UAE's output is stereo (Paula channels 0 / 3 left, 1 / 2 right: L/R rms 3241 / 4392), the Client's
mono; above 3 kHz the Amiga is 10 dB lower, above 6 kHz 25–40 dB lower (the A500's output filter, which the Client's
`PaulaSynth` lacks); FS-UAE has strong energy at 20–60 Hz (+17 dB, a 24–25 Hz peak) the Client does not, not
explained. Not checked in the emulator: songs 2, 3, 5, an effect cutting a song, Paula's retrigger, the hunk 32 +0x2D1
flag.

## 6. Open `[?]`

- Auto-stop onset in DOSBox-X (§2.4); the speaker's analogue filter; the early Tandy 1000's SN76496; a spell's cast
  tune and Lightning Bolt / Fireball in DOSBox-X (not captured, s39); machine-time lengths (§2.4 s39) `[U]` whether
  to model them.
- Amiga (§5): PAL or NTSC rate; Paula's restart and latch timing; how the flag at hunk 32 +0x2D1 can change;
  the screens of hunks 4, 16 and 19.
- Which other SSI games share this driver and its tunes 12–20.
