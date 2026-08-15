# GameLock

A Windows parental-control tool that locks specific games at the process level. Kids click the normal game shortcut like always — if it's locked, it gets terminated immediately. No special child-facing launcher, no BAT file that has to stay open.

- **Background enforcement** — a real Windows Service (`GameLockService`), not a console window that has to stay open
- **Runs as SYSTEM, starts at boot** — protection is active before anyone logs in
- **Auto-relocks every reboot** — unlocking is temporary by design; there's no way to make it permanent
- **Password-protected unlock**, hash-only storage (PBKDF2-SHA256, 200k iterations, random salt — no plaintext, ever)
- **Simple WPF GUI** for the parent: add/remove games, lock/unlock, change password, install/uninstall protection

## Table of contents

- [How it works](#how-it-works)
- [Requirements](#requirements)
- [Installation](#installation)
- [Using GameLock](#using-gamelock)
- [Uninstalling](#uninstalling)
- [Project structure](#project-structure)
- [Building from source](#building-from-source)
- [Testing](#testing)
- [Troubleshooting](#troubleshooting)
- [Security model & known limitations](#security-model--known-limitations)
- [License](#license)

## How it works

A BAT file that "watches" for a game only works while its console window is open — close it, or just never run it, and there's no protection left. It also can't run at boot with elevated rights, can't intercept process creation reliably, and a kid can simply end the `.bat` process.

GameLock instead installs a proper **Windows Service**:

- Registered with the Service Control Manager, `start= auto`, runs as `LocalSystem`
- Watches for new processes via WMI's `Win32_ProcessStartTrace` (event-driven, near-zero idle CPU), backed by a 5-second sweep as a safety net
- Kills any process matching a locked game's full executable path, immediately if via the watcher, within ~5s via the sweep otherwise
- Keeps its "locked/unlocked" state **in memory only** — when the service process ends (i.e. on shutdown/restart), that state is gone. The service always starts back up locked. There is no on-disk "unlocked" flag to tamper with.

The GUI never unlocks anything itself — it sends the password to the service over a named pipe, and the **service** verifies the password hash and flips its own in-memory state. The pipe's ACL only allows Administrators/SYSTEM to connect at all.

## Requirements

- Windows 10 or 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on the machine you build/install from
- Local administrator rights (installation and all management actions require elevation)

## Installation

1. Download/clone this repo and unzip it if needed.
2. Open **PowerShell as Administrator**:
   - Press `Win`, type `PowerShell`, **right-click → Run as administrator**, accept the UAC prompt.
   - Confirm the window title bar says `Administrator: Windows PowerShell`.
3. Go to the repo folder (quote the path if it contains spaces or apostrophes):
   ```powershell
   cd "C:\path\to\GameLock"
   ```
4. Allow the installer to run for this session:
   ```powershell
   Set-ExecutionPolicy -Scope Process Bypass -Force
   ```
   If the folder came from a downloaded zip, Windows may still flag the scripts as coming from the internet. If you hit a script-blocked error, run:
   ```powershell
   Get-ChildItem -Path . -Recurse | Unblock-File
   ```
5. Run the installer:
   ```powershell
   .\install\install.ps1
   ```
   This publishes both apps, registers `GameLockService`, starts it, and adds a **GameLock** shortcut to the Start Menu.
6. Launch **GameLock** from the Start Menu (accept the UAC prompt — the GUI always requires admin). On first run it will ask you to create the parent password.

## Using GameLock

- **Add Game** — pick one or more `.exe` files via the file picker.
- **Remove Game / Clear Games** — remove a selected entry or wipe the whole list.
- **Lock Now** — force protection on immediately (also kills the game if it's already running).
- **Unlock** — enter the parent password to allow configured games to run until the next reboot.
- **Change Password** — requires the current password first.
- **Install Protection / Uninstall Protection** — (re)register or remove the background service.
- **Protection Status** — shows `LOCKED`, `UNLOCKED (until next restart)`, or that the service isn't running.

## Uninstalling

From an elevated PowerShell prompt, in the repo folder:

```powershell
.\install\uninstall.ps1                # removes the service + program files, keeps saved config/games/password
.\install\uninstall.ps1 -RemoveConfig  # also wipes the saved configuration entirely
```

## Project structure

```
GameLock/
├── GameLock.sln
├── src/
│   ├── GameLock.Common/     # shared config model, PBKDF2 hashing, named-pipe protocol
│   ├── GameLock.Service/    # Windows Service: process watcher + pipe server
│   └── GameLock.Gui/        # WPF management app (always runs elevated)
└── install/
    ├── install.ps1          # publish + register service + create shortcut
    └── uninstall.ps1        # stop/remove service + program files
```

## Building from source

```powershell
dotnet restore
dotnet build -c Release
```

To publish standalone output for each component manually:

```powershell
dotnet publish src\GameLock.Service\GameLock.Service.csproj -c Release -r win-x64 --self-contained false -o publish\Service
dotnet publish src\GameLock.Gui\GameLock.Gui.csproj -c Release -r win-x64 --self-contained false -o publish\Gui
```

## Testing

1. Install and set a password (see above).
2. Add a test executable (e.g. `notepad.exe`) as a "game."
3. Confirm status shows `LOCKED`; launch it — it should be terminated within a few seconds.
4. Click **Unlock**, enter the password; launch it again — it should now run and stay running.
5. Restart Windows; reopen the GUI — status should read `LOCKED` again automatically.
6. While a locked game is already running, click **Lock Now** — it should be terminated within the sweep interval.
7. Optional: log in with a standard (non-admin) account and confirm the GUI refuses to run without a UAC prompt, and that `C:\ProgramData\GameLock\config.json` can't be edited from that account.

## Troubleshooting

| Symptom | What to check |
|---|---|
| `'Set-ExecutionPolicy' is not recognized` | You're in `cmd.exe`, not PowerShell — reopen an elevated PowerShell window. |
| `script cannot be run... "#requires" for Administrator` | The PowerShell window itself isn't elevated — right-click → **Run as administrator**, confirm the UAC prompt, confirm the title bar says "Administrator." |
| `Start-Service` fails with a generic error | Run `sc.exe start GameLockService` for a more specific error code, or run `"C:\Program Files\GameLock\Service\GameLockService.exe"` directly in a console to see the startup exception printed live. Also check Event Viewer → Windows Logs → Application, source `GameLockService`. |
| GUI says "protection service is not running" | Check `services.msc` for `GameLockService`; reinstall if missing. |
| Game isn't blocked instantly, only after ~5s | The WMI process-start watcher failed to start (logged as a warning); the periodic sweep still catches it within 5 seconds — check the service log for why the watcher didn't start. |
| "Access denied" writing config from the GUI | The GUI didn't actually launch elevated — confirm you accepted the UAC prompt. |
| Password rejected but you're sure it's correct | Passwords are case-sensitive; verify `C:\ProgramData\GameLock\config.json` exists and is valid (admin access required to view it). |
| Service won't reinstall | `sc.exe delete GameLockService`, wait a few seconds, then re-run `install.ps1` (it does this automatically on reinstall). |
| `dotnet --list-runtimes` shows nothing | Install the [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) on the target machine. |

## Security model & known limitations

This assumes the child's Windows account is a **standard (non-admin)** account. A local administrator — including a child who has or obtains admin credentials — can bypass GameLock the same way they could bypass any OS-level control, e.g.:

- Stopping/deleting `GameLockService` via `sc.exe` or `services.msc`
- Editing or deleting `C:\ProgramData\GameLock\config.json` directly (admins bypass its ACL)
- Booting into Safe Mode, a recovery environment, or another OS/USB drive
- Copying the game executable to a new path/name not in the configured list
- Uninstalling GameLock via the GUI's own Uninstall Protection (itself admin-gated)
- Using a different admin account, or elevating the child account
- Attaching a debugger or killing the SYSTEM-level service process with admin tools

None of this is specific to GameLock — it's true of essentially all local, software-based parental controls. Treat this as a deterrent/friction layer, not a security boundary against a technically capable admin user on the same machine. The real control is account hygiene: keep the child's account standard, and keep the admin password known only to the parent.

## License

Not Allow to Edit,Free To Use
