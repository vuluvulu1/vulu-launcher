# vulu Launcher

<img width="128" height="128" alt="vulu Launcher" src="https://github.com/user-attachments/assets/8566a06d-6fe5-4a4e-aeb1-e812ab9e67b7" />

A private Minecraft launcher for me and my friends.

## Install

Download `vulu-launcher-setup.exe` from the [latest release](https://github.com/vuluvulu1/vulu-launcher/releases/latest) and run it.
The setup downloads the newest version, creates shortcuts and registers an uninstaller in Windows' "Apps" list.
After that the launcher updates itself on every start.

Windows may show a SmartScreen warning because the files are not code-signed: *More info → Run anyway*.

## Features

- Vanilla and your own instances (Fabric, Quilt, Forge, NeoForge), with Java downloaded automatically
- Modrinth browser for mods, resource packs, shaders and modpacks
- **vulu Packs**: share an instance with friends and install or update it in one click; feedback and requests per pack
- Profiles and a community list
- Three themes with their own layouts: Classic, Neon and Atlas

## Project layout

| Folder | What it is |
| --- | --- |
| `Launcher/` | The launcher (Photino.NET + WebView2, UI in `wwwroot/`) |
| `Updater/` | Checks GitHub Releases on start, applies updates, handles uninstall |
| `Installer/` | `vulu-launcher-setup.exe`, the online installer |
| `Shared/` | Code shared by the three programs (release feed, install info, window icon) |
| `tools/` | Release and local test scripts |

The API lives in a separate repository: [vulu-api](https://github.com/vuluvulu1/vulu-api).

## Releasing

```powershell
# Builds everything into artifacts/release/<version>/ and uploads it as a GitHub release
.\tools\publish-release.ps1 -Version 0.9.0 -NotesFile notes.txt -Publish
```

To try a release on this machine without GitHub:

```powershell
.\tools\publish-release.ps1 -Version 0.9.1 -Local   # stage as the local "latest"
.\tools\local-test.ps1                               # serve it and point setup/updater at it
.\tools\local-test.ps1 -Off                          # back to GitHub
```
