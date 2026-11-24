# OpenGoldBox

An open-source engine for SSI's "Gold Box" turn-based party CRPGs (1988–1993), written in C# on .NET 10 and MonoGame.

## Goals

- **Play any module**: modules converted from original games you own, and new modules made by the community.
- **Bring your own files.** OpenGoldBox ships no original game data. A built-in importer converts your own copy of
  a game into the open module format, on your machine.
- **Faithful first.** Imported games play 1:1 with the DOS originals. The original program is studied routine by
  routine and its behaviour ported whole, checked against the original running in DOSBox-X (and FS-UAE for Amiga
  art and sound). Anything not yet confirmed is marked `[?]` in the specs and the code; nothing is guessed.
- **Quality of life as options.** Features from the later games and from companion tools (heal-the-party FIX,
  automap, faster animation, better menus, the Gold Box Companion's aids) are settings you switch on. Off means
  the original.
- **Keyboard, mouse and gamepad**: each one plays the whole game on its own.
- **Best-looking art**: each game can use the art of its best-looking port (for Pool of Radiance, the Amiga version),
  read from your own copy of that port.
- A **character manager** that moves characters between games and modules, following each sequel's transfer rules.
- An open, documented **module format**, and later an editor, a script language for new adventures, and import of
  Unlimited Adventures (FRUA) and Dungeon Craft designs.

## Status

Pre-release, under active development. No binaries yet; you build from source.

- **Pool of Radiance (DOS v1.3)** is the first game. It imports and plays: introduction, party creation, training
  hall, the 3D view and area map, the overland map, encounters, the full tactical combat (every spell, monster AI,
  items), shops, temples, treasure, the camp and saving. Screens are compared pixel for pixel and frame for frame
  against the original in DOSBox-X. The remaining work toward a fully faithful play-through is tracked as a
  routine-by-routine coverage ledger ([docs/re/routine-ledger.md](docs/re/routine-ledger.md)).
- **Amiga art and sound** for Pool of Radiance are available when you also have the Amiga v1.0 version
  (`--art amiga` or `--art best`).
- A handful of the original's bugs that crash or break the game (for example a double free when a revived wild boar
  expires, a text routine that hangs on long strings) are fixed; each is written up in the specs.
- **Secret of the Silver Blades**: its scripts decode and run headless; not playable yet.
- **Not started**: the other games, the character manager, the editor, community module import.

Development has been on Linux. Other platforms that .NET 10 and MonoGame support should work but are untested.

## Getting started

You need the .NET 10 SDK and your own copy of Pool of Radiance for DOS (v1.3).

```
./play.sh --game ~/games/POOLRAD       # first run: imports the game into ~/.cache/goldbox/modules/, then plays
./play.sh                              # later runs: the game folder is remembered
./play.sh --game ~/games/POOLRAD --amiga ~/games/POOLRAD-AMIGA --art best
./play.sh --help                       # more options (--module, --save, --scale, --sound, --seed, ...)
```

`--amiga` takes the unpacked Amiga v1.0 disks (`program` from disk A, the `.dax` files from disk B). The importer
also runs on its own:

```
dotnet run --project src/GoldBox.Importer -- por GAMEDIR OUTDIR [--amiga AMIGADIR]
```

It writes the module and checks that every imported block rebuilds byte for byte from it. It also converts saves
between the original format and the engine's (`save-import`, `save-export`).

Tests: `dotnet test`. Tests that need game data skip unless `GOLDBOX_POR_DIR` (and `GOLDBOX_POR_AMIGA_DIR` for the Amiga
tests) point at your game folders (see
[src/GoldBox.Core/README.md](src/GoldBox.Core/README.md)).

## Layout

| Path | What it is |
|---|---|
| `src/GoldBox.Core` | Rules, the ECL script VM, game state, combat, saves. No graphics; unit-tested. |
| `src/GoldBox.Data` | The module format, schemas and loaders. |
| `src/GoldBox.Importer` | Converts original games (and saves) into modules. |
| `src/GoldBox.Client` | The MonoGame front end. |
| `src/GoldBox.Tools` | Developer tools: run and compare scripts, views and the overland map. |
| `docs/module-format.md` | The module format spec (draft). |
| `docs/re/` | Our own specs of the original formats and program behaviour, with what is still unconfirmed. |
| `tools/scripts/` | Python tools for the original formats and for testing against emulators. |

## License

MIT (see [LICENSE](LICENSE)). This covers OpenGoldBox's own code and documentation only. The original games, their
content and their trademarks belong to their respective owners; OpenGoldBox is not affiliated with or endorsed by them.
