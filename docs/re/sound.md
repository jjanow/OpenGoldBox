# Sound and music (Pool of Radiance)

Status: DOS v1.3 read in the program (s19) and checked under emulation; Amiga v1.0 located, not decoded. Marks:
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
  game sends the stop command (START 0x8990).

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
  channel-0 divisor when the running BIOS period ends, up to 55 ms), the Tandy, the fight's sounds.
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
  2 dB per attenuation step, 15-bit noise register `[?]` (taps as the TI part; the Tandy 1000's exact chip variant
  not checked).
- The auto-stop model: cue → start the tune, run (BIOS-chain count + 13) ticks, silence; in a resident bracket the
  tune runs until the bracket closes (`PcSoundPlayer`). Rendered, a step is one 27.4 ms pulse and a bump 104–108 ms,
  as in DOSBox-X (`SoundRenderTests`).
- **Hooks** (Core `ISoundSink`, called where the program calls the sound unit): the explore loop (`AreaLoop.Sound`:
  the move, turns left/right, the area loop's check after the redraw), the bump (`IAreaUi.Bump`), ECL CALL $BA03 (the
  Client's host), the fight (`FightState.Sound`: impact per hit and on a helpless defender, miss, a creature's step,
  the body routine, a missile's flight, a spell's flight with its own cue for spells 47 and 51, Hold Person's further
  targets). Not hooked `[?]`: the name animation (START 0x2B8B, cues 261A/261C; 10 callers in GAME.OVR: damage and
  effect messages), the second missile flight for an item of type 0x2F (OVR 0x1417E), sounds outside a fight in the
  body routine.
- **Client**: `--sound speaker|tandy|off` (settings `Sound`, default speaker); 44.1 kHz mono through MonoGame's
  dynamic sound instance; the logic thread waits out an auto-stop sound as the program does. `[?]` A resident
  bracket's length is the program's animation (missile / spell flight, body drawing, message pause), which the Client
  does not draw yet: it stays open until its tune ends by itself or the next sound call outside a bracket.

## 4. Module (`audio/`)

The importer reads the driver from the player's START.EXE (`PorSoundDriver`, found by its code: the timer install,
the BIOS-chain count in the interrupt, the two tick routines, the two tune programmers) and writes
`audio/dos/pcsound.json`: the data bytes CS:01D7 up to the timer install (base64), their origin and the table offsets.
The cue table stays in `rules/program.json`'s data segment (DS:2612; found by its shape). See module-format.md §10a.

## 5. Amiga v1.0 (located; not decoded)

- `program` (disk A, 41 hunks): hunk 27 (CODE, 33,568 bytes, file offset 267,572) is shared by most overlay hunks
  (relocations from hunks 1, 4, 7, 10, 13, 16, 19, 22, 25, 33). Near +0x7C95 it holds a **music and sound player**
  (a programmer credit string sits in the code), with self-modifying code (it writes into its own hunk) and direct
  custom-chip access (DMACON). Its tables are in hunk 28 (DATA, 78,504 bytes; its 167 relocations all point inside
  itself), among them a 12-per-octave Amiga period table at +0x16E8. Hunk 29 (DATA, 58,284 bytes) is **8-bit signed
  PCM** (sample data); no relocation in the file points into it, so how the player addresses it is `[?]`.
- **Entry points** (P, read s19): hunk 27 +0x6E54 `PlaySong(n)` (C calling convention, n on the stack; sets the
  "playing" long at hunk 33 +0xCC and calls hunk 33 +0xE6 → hunk 27 +0x78A0 with n in d0), +0x6E88 stop (n = 0
  through the same path), +0x6E78 (calls hunk 33 +0xD8, `[?]` set-up). Hunk 33 (the program's resident hunk) +0x82 is
  a vertical-blank interrupt handler: while +0xCC ≠ 0 it calls the player's tick (hunk 27 +0x78B2), so the player runs
  at the display's 50 Hz (PAL) `[?]`. The player (hunk 27 +0x78C2): n = 0 stops; 1 ≤ n ≤ 7 copies 16-byte entry n − 1
  of a table at hunk 28 +0x197C (seven **songs**) into the current-song slot +0x182C and starts four voices; n > 7
  goes to another path (+0x79AC) `[?]`.
- **Callers** (through each overlay's jump stub to +0x6E54): hunk 1 +0x4FBC (song 1) and +0x513C (0, stop); hunk 10
  +0x0580 (1), +0x059A (0), +0x235C (3), +0x433E (3); hunk 13 +0x24E2 (a variable, the word at hunk 28 +0x18C0 `[?]`);
  hunk 16 +0x46C2 (5); hunk 19 +0x102E (5). So the Amiga version plays **music** (sampled instruments, hunk 29) on some
  screens; which screens the overlays are (main menu, character creation, …) and whether it has any sound effect at
  all are `[?]`. No caller passes n > 7.
- The `intro` (PowerPacker, por-art-formats.md §5) decrunches to 9 hunks; whether it plays music is `[?]`.
- Until the player and its song format are decoded the engine plays the DOS sounds for every setting.

## 6. Open `[?]`

- Auto-stop length and onset in DOSBox-X (§2.4); the speaker's analogue filter; the Tandy noise register.
- The Amiga player (§5): which screens play which song, the song and instrument format, the tick rate, the n > 7 path.
- Which other SSI games share this driver and its tunes 12–20.
