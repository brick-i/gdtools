# gdtools

**English** · [Русский](README.ru.md)

A tool for Geometry Dash 2.2: editing the save file (`CCGameManager.dat`) and the live
memory of a running process. Console plus a visual interface.

Built for Windows and .NET Framework 4.8, compiled with the very `csc.exe` that already
ships with the system — **nothing to install**, no Python, no runtimes, no Geode.

```
gd_currency.exe   console: parse save, patch save, work with memory
gd_gui.exe        visual interface (just double-click)
gd_tool.exe       console menu, for when you can't be bothered to remember commands
```

## Features

- **Save parsing.** `CCGameManager.dat` = `XOR 0x0B ( base64url( gzip( plist ) ) )`.
  The parser and serializer are byte-compatible with the original, verified by `verify`.
- **Save patching.** Writes through a temp file, reads it back and checks it — the
  original is only replaced after a successful verification. Backs up automatically.
- **Live memory patching.** In 2.2 the stats do not live in the plist strings but in the
  `GameStatsManager` hash table. The value on screen is `current − delta`, so we write
  `current = target + delta`.
- **`bulk`** — the whole wallet in one pass over memory (a full scan of ~900 MB takes
  20-40 seconds, so we scan once instead of once per stat).
- **`wallet`** — shows what is actually on screen, not what is in the file.
- **GUI** — dark and light themes, "Currency" / "Account" groups, search, drag-swipe
  row selection, a panel listing what is selected.

## Building

From the repository root:

```cmd
build.cmd
```

The script locates `csc.exe` in `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319`, builds
all three executables and places them next to itself. If `gd_gui.exe` is running at that
moment, the script renames it to `gd_gui_old.exe` and carries on — Windows permits
renaming a running executable.

The only requirement is .NET Framework 4.8 (present out of the box on Windows 10/11).

## Usage

### Console

```
  SAVE:
    gd_currency info                                   parse the save
    gd_currency stats                                  stat IDs + what they mean
    gd_currency ls [path] [-max N]                     flat list of values
    gd_currency get <path>                             one value, e.g. GS_value/14
    gd_currency set <path> <value>                     patch (game must be CLOSED)
    gd_currency setrun <stat> <value>                  CLOSE game -> patch -> relaunch
    gd_currency find <number|string> [-path prefix]    search by value
    gd_currency verify                                 round-trip, byte-for-byte check
    gd_currency dump [-o file.xml]                     dump the plist to xml
    gd_currency snapshot [-o b.json]                   snapshot of every value
    gd_currency diff <snapshot.json>                   what changed in the game

  MEM (live process, like Cheat Engine):
    gd_currency live <stat> <value>           PATCHES A STAT IN A RUNNING GAME (orbs too)
    gd_currency bulk <stat>=<number> [...]    PATCH THE WHOLE WALLET IN ONE PASS
    gd_currency wallet [-csv]                 all live stats: what is really on screen
    gd_currency nodedump <stat>               raw dump of a table node (debug)
    gd_currency statmap                       stat plist records in memory
    gd_currency live-scan <stat> <value> [-current N] [-watch N]
        the old approach: value search (when live fails)
    gd_currency live-narrow <new number>
        narrow the candidates after the value changed in game
    gd_currency live-probe -seconds N [-min N] [-want <number>] [-maxchanges N]
        does NOT know the number: watches all of memory and catches cells that
        change, grouping them by value. PLAY while it watches.
    gd_currency live-write <addresses> <value>
        write a value to specific addresses
    gd_currency mem-scan   <number> [-type int32|int64|float|double] [-writable] [-align N]
    gd_currency mem-scan   -bytes <hex>  (? as a wildcard byte)   AOB scan
    gd_currency mem-narrow <number>                    narrow after a change in game
    gd_currency mem-write  <number> [-seconds N]       write / hold (freeze)
    gd_currency mem-dump   <address> [length]          hex dump of memory

  Common:
    -f file       path to the .dat (default %LOCALAPPDATA%\GeometryDash\CCGameManager.dat)
    -proc name/PID target process for mem-*, defaults to GeometryDash
```

The built-in `--help` and all console messages are in Russian. Command names, paths and
flags are identical.

Examples:

```cmd
gd_currency wallet -csv
gd_currency bulk orbs=500000 diamonds=999 usercoins=999 stars=1000
gd_currency setrun stars 1000
```

### Interface

`gd_gui.exe` is launched with a `requireAdministrator` manifest, because otherwise
`WriteProcessMemory` into another process gets denied.

- the rail on the left switches groups: **Currency** / **Account** / reset / theme toggle;
- a row can be checked by clicking its checkbox, by dragging across checkboxes (the range
  extends from the first one, rows outside the range revert to what they were), or with
  the "check all" / "uncheck all" buttons;
- clicking the row name performs normal selection (Ctrl / Shift work), and the
  "patch selected" button sends the selection rather than the checked rows;
- mode (`set` / `add`) and the value live in the input bar, with presets next to them;
- the operation log sits in the centre and shows `gd_currency` output line by line.

### Stats

The IDs match the `GS_value/<id>` keys in the save.

| ID | stat | | ID | stat |
|---|---|---|---|---|
| 1 | jumps | | 17 | poison orbs |
| 2 | attempts | | 18 | fire orbs |
| 3 | completed levels | | 19 | ice orbs |
| 4 | completed online | | 20 | lava orbs |
| 5 | demons | | 21 | basement keys |
| 6 | stars | | 22 | total orbs collected |
| 7 | map packs | | 23 | earth orbs |
| 8 | secret coins | | 24 | blood orbs |
| 9 | players destroyed | | 25 | light orbs |
| 10 | liked | | 26 | metal orbs |
| 11 | rated | | 27 | soul orbs |
| 12 | user coins | | 28 | moons |
| 13 | diamonds | | 29 | diamond shards |
| 14 | orbs | | 30 | gauntlets |
| 15 | daily | | 31 | list rewards |
| 16 | shadow orbs | | | |

Aliases for `live` / `bulk`:
`orbs=14 stars=6 coins=8 diamonds=13 shards=29 practice=2`

## How it works

### Save

```
file  = XOR 0x0B ( base64url( gzip( plist ) ) )
plist = <?xml?><plist gjver="2.0"><dict>...
stats = GS_value/<number>, values stored as strings
```

Writes go into both files — `CCGameManager.dat` and `CCGameManager2.dat` — plus the Steam
AutoCloud copy (`userdata/<id>/322170/ac/WinAppDataLocal/GeometryDash/`), because Steam
syncs the local save on its own, without an option to disable it, overwriting your edits
with an older version.

### Memory

The `GameStatsManager` table:

```
node, 0x20 bytes:  +0x00 ptr, +0x08 next, +0x10 u32 stat id, +0x14 u32 value
StatInfo:          stats_instance + 0x248 (current) / +0x288 (delta)
                   +0x08 list_end, +0x18 table, +0x30 hash_mask
```

The number on screen is computed as `current − delta`, so when writing to memory you have
to write `current = target + delta`, otherwise the game recalculates after any action and
shows something other than what you expected.

A side effect in 2.2: some stats ignore the file and live in memory only (IDs 13, 14,
16-21, 23-29, 31 — diamonds, orbs, shards, keys and the like). Writing them to the save is
pointless, use `live` / `bulk`. Stars, secret coins and user coins persist through the save
just fine.

## Limitations

- Windows only.
- Live patching requires a running game and administrator rights.
- A full memory scan takes tens of seconds on every launch, hence `bulk`.
- Stat addresses change between launches, everything is looked up from scratch.

## Disclaimer

An educational tool for studying the storage format and working with process memory.
Editing the save or memory can cost you progress or get your account blocked — keep
backups; they are created automatically, but extra ones never hurt. The author accepts no
responsibility for the consequences.

## License

MIT, see [LICENSE](LICENSE).
