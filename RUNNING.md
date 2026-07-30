# Running Torchlight Depths

Torchlight Depths ships as **self-contained** builds — the .NET runtime, OpenTK,
and native audio (OpenAL-Soft) are all bundled, so there's **nothing to install**
to play a prebuilt download. There are also no external asset files: the whole
game is generated in code.

The game launches **fullscreen at 1920×1080**. Press `F11` for a window, `Esc` to
pause to the menu (which auto-saves), and `M` to toggle music.

---

## Windows (64-bit)

**Prebuilt:**

1. Download `TorchlightDepths-windows-x64.zip`.
2. Right-click the .zip → **Extract All…** (extract it somewhere real, like your
   Desktop — don't run it from inside the zip preview).
3. Double-click **`Dungeon.exe`**.

If Windows SmartScreen shows *"Windows protected your PC"* (the build is
unsigned), click **More info → Run anyway**.

The folder also contains `OpenAL-Soft-LICENSE.txt` — audio works out of the box,
no separate OpenAL install needed.

---

## macOS

There are two builds — pick the one for your Mac:

- **Apple Silicon** (M1/M2/M3/M4): `TorchlightDepths-macos-arm64.dmg` — macOS 11+
- **Intel**: `TorchlightDepths-macos-x64.dmg` — macOS 10.15+

> Not sure which? Apple menu → **About This Mac**. "Apple M…" chip = arm64;
> "Intel" = x64.

**Install:**

1. Double-click the `.dmg`.
2. Drag **Torchlight Depths** onto the **Applications** shortcut.
3. Launch it from Applications.

**First launch — the app is unsigned**, so Gatekeeper will block it with
*"…cannot be opened because the developer cannot be verified."* Get past it one of
these ways:

- **Right-click** (or Control-click) the app → **Open** → **Open** in the dialog.
  You only need to do this once.
- Or: **System Settings → Privacy & Security**, scroll down, and click
  **Open Anyway** next to the Torchlight Depths message.

If macOS still refuses (some newer versions quarantine downloaded apps hard), open
**Terminal** and run:

```bash
xattr -dr com.apple.quarantine "/Applications/TorchlightDepths.app"
```

then launch normally.

---

## Linux (64-bit)

There's no prebuilt Linux download, but it runs great from source — see below.
You'll need an OpenGL 4.1-capable GPU/driver and system OpenAL for sound
(`sudo apt install libopenal1` on Debian/Ubuntu; the game runs silently if it's
missing).

---

## Running from source (any platform)

Requires the **.NET 10 SDK** (<https://dotnet.microsoft.com/download>). From the
project folder:

```bash
dotnet run -c Release
```

The first run restores the OpenTK NuGet package, then builds and launches. That's
all you need on Windows, macOS, or Linux.

---

## Building your own packages

`build.sh` produces the distributable packages — a Windows `.zip` and drag-to-
install macOS `.dmg`s (arm64 + x64), each a single self-contained executable:

```bash
./build.sh            # version defaults to 1.0.0
./build.sh 1.1.0      # stamp a specific version
```

Requires the .NET SDK. The `.dmg` step uses `hdiutil`, so it only runs on a macOS
host — on Linux/Windows the script builds the macOS binaries but skips the `.dmg`
packaging.

---

## Saves

Your run auto-saves at the start of every floor and whenever you leave the game.
The save file lives at:

| Platform | Location |
|----------|----------|
| Windows  | `%APPDATA%\TorchlightDepths\save.json` |
| macOS    | `~/.config/TorchlightDepths/save.json` |
| Linux    | `~/.config/TorchlightDepths/save.json` |

Pick **Continue** from the menu to resume exactly where you left off. Dying
deletes the save — a fallen hero can't be continued (unless you spend gold to
revive on the death screen).
