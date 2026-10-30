# Friday Night Funkin' (MonoGame port) on Xbox / Windows (UWP)

This folder builds the UWP version of the port. The same `.msix` runs on an **Xbox One / Series X|S in
Developer Mode** and on a **Windows 10/11 PC**.

## What you get

`build_xbox.ps1 -Release` produces, in `AppPackages\FNF_Xbox_<version>_x64_Test\`:

| file | what it is |
|---|---|
| `FNF_Xbox_<version>_x64.msix` | the game code only (~14 MB). Needs the game content separately (see "Content" below). |
| `FNF_Xbox_<version>_x64_full.msix` | made by `make_full_package.ps1`: code **plus the whole `Content` folder** (~1.2 GB). Nothing else to upload. **Use this one.** |
| `Dependencies\x64\*.appx` | 3 framework packages the game needs: `Microsoft.NET.Native.Framework.2.2`, `Microsoft.NET.Native.Runtime.2.2`, `Microsoft.VCLibs.x64.14.00`. |
| `FNF_Xbox_<version>_x64.cer` | the public certificate (`CN=mohammad aljafari`) — only needed on a Windows PC. |

Both packages are signed with the `CN=mohammad aljafari` certificate (thumbprint
`0CF8C36AC5F8A1D97017E9FB18CE21AFDD8176A3`). The `Publisher` in `Package.appxmanifest` must stay exactly
`CN=mohammad aljafari`, otherwise signing fails.

---

## 1. Install on a real Xbox

### 1.1 Put the Xbox in Developer Mode (one time)
1. Make a (paid, one-time) Microsoft **Partner Center** developer account: <https://partner.microsoft.com/dashboard/registration>.
2. On the Xbox open the Microsoft Store, install **"Xbox Dev Mode"**, start it and note the activation code.
3. On a PC go to Partner Center > **Xbox devices** (<https://partner.microsoft.com/xboxconsoles>), add the console with that code.
4. Back on the Xbox, press **Switch and restart**. The console boots into **Dev Home**.
5. In Dev Home > **Remote Access Settings**: turn on **Enable Xbox Device Portal**, set a user name and password.
   Note the console IP shown in Dev Home (e.g. `192.168.1.50`).

### 1.2 Install the game with Device Portal
1. On the PC open `https://<XBOX_IP>:11443` (accept the certificate warning, log in).
2. **Home > My games & apps > Add**.
3. **Choose file** -> `FNF_Xbox_<version>_x64_full.msix` -> **Next**.
4. **Choose dependency files** -> select the three files from `Dependencies\x64\` -> **Next** -> **Start**.
   The upload of the 1.2 GB package takes a few minutes on wired LAN.
5. No certificate install is needed on Xbox (Device Portal accepts the signed package directly; the `.cer` is
   only for Windows PCs).
6. **Important – run it as a Game:** in Dev Home select *Friday Night Funkin'* > **View details** (or the
   Menu button) > **Change app type: Game**. Apps get far less memory/GPU than games on Xbox.
7. Launch it from Dev Home (or *Collections* on the Xbox).

Updating: install a package with a higher version over the old one – saves, mods and uploaded content in
LocalState are kept. **Uninstalling deletes LocalState** (saves, mods, uploaded content).

### 1.3 Content (only if you install the small code-only `.msix`)
The game looks for content in this order:
1. `LocalState\Content` (if it contains a `data` folder),
2. the package's own `Content` folder (the `_full.msix`),
3. otherwise it downloads `content.zip` from the GitHub release set in `ContentDownloader.cs` (`CONTENT_URL`).

To give the code-only package its content without internet:
1. `.\package_content.ps1` -> creates `content.zip` (the whole `Content` folder).
2. Device Portal > **File explorer** > `LocalAppData` > `FridayNightFunkin.MonoGame_<...>` > `LocalState` >
   **Upload** `content.zip`.
3. Start the game: it unpacks `LocalState\content.zip` into `LocalState\Content` on the loading screen and
   deletes the zip. (Device Portal can only upload files into existing folders, so a zip is the practical
   way; uploading the 2 500 files one folder at a time is not.)

`LocalState\Content` always wins over the packaged content, so the same zip upload can also be used to
patch/replace single content files later (the zip is merged, nothing is deleted).

### 1.4 Mods
Mods live in **`LocalState\mods\<ModName>\`** (same layout as Psych Engine `mods/`, e.g.
`LocalState\mods\MyMod\songs`, `...\data`, `...\images`, `pack.json`).
To install: zip the mod folder(s) as `mods.zip` (either `MyMod\...` or `mods\MyMod\...` at the zip root),
upload it into `LocalState` with Device Portal > File explorer, start the game – it is unpacked into
`LocalState\mods` and the zip is deleted. Then enable the mod in the main menu **Mods** screen.

### 1.5 Where the save data is
`LocalState\save_data.json` (scores, options, key binds) and the Psych prefs/achievements files next to it.
Device Portal > File explorer > LocalState lets you download/back them up.

---

## 2. Install on a Windows 10/11 PC (same package)
1. Settings > System > For developers: **Developer Mode** on (or "Install apps from any source").
2. Trust the certificate once (admin PowerShell):
   ```powershell
   Import-Certificate -FilePath .\FNF_Xbox_<version>_x64.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```
3. Install with the dependencies:
   ```powershell
   Add-AppxPackage -Path .\FNF_Xbox_<version>_x64_full.msix -DependencyPath (Get-ChildItem .\Dependencies\x64\*.appx).FullName
   ```
   If that fails with `0x80073D02` (another app, e.g. Gaming Services, uses VCLibs) the frameworks are already
   installed: run the same command without `-DependencyPath`.
4. Start "Friday Night Funkin'" from the Start menu. LocalState is
   `%LOCALAPPDATA%\Packages\FridayNightFunkin.MonoGame_spbm5kqrqhs8c\LocalState` (mods go in `LocalState\mods`).

---

## 3. Building

Prerequisites: Visual Studio 2022 with **Universal Windows Platform development** (+ Windows 10 SDK
10.0.22621), the signing certificate in `Cert:\CurrentUser\My` (import `mohammad_aljafari.pfx` once).

```powershell
cd FNF_MonoGame_Full\Xbox
.\build_xbox.ps1 -Release -Full   # .NET Native Release build + signed .msix (10-15 min) + ..._full.msix (3-5 min)
# or separately: .\build_xbox.ps1 -Release ; .\make_full_package.ps1
```
`build_xbox.ps1` without `-Release` makes a quick Debug build (no .NET Native) for compile checks.

Release builds use the .NET Native tool chain (`UseDotNetNativeToolchain`), required for Xbox/Store-style
sideloading. Shared code must stay UWP-safe: no WinForms/WPF/NAudio, and APIs missing from .NET Native's
netcore 6.2 surface (e.g. `MathF.Log2`) do not compile there.

### Content paths in code
Never build content paths from `AppDomain.CurrentDomain.BaseDirectory` – on UWP that is the read-only
package folder, which does not contain uploaded content. Use `AssetManager.ContentRoot` (content) and
`AssetManager.GameRoot` (writable: saves, `mods`) – desktop behaviour is unchanged.

### Test hooks (UWP)
UWP apps get no environment variables, so the desktop `FNF_*` dev hooks are read from
`LocalState\devhooks.txt` (one `KEY=VALUE` per line, used for ONE launch, then renamed to
`devhooks.used.txt`). Examples:
```
FNF_LOG=1                      # Console -> LocalState\game_log.txt
FNF_START_SCENE=freeplay       # mainmenu | options | story | freeplay | multiplayer | mods | achievements
FNF_AUTOPLAY_SONG=ugh          # boot into a song (botplay); FNF_AUTOPLAY_WEEKSONGS=ugh,guns,stress = story mode
FNF_SHOTS=5,10,20              # save the 1280x720 frame to LocalState\shots\shot_NN.png at these seconds
FNF_DEVKEYS=6:Enter,8:Down     # inject key presses at seconds
```

## Controller mapping

### Menus
| Action    | Controller         | Keyboard         |
|-----------|--------------------|------------------|
| Navigate  | D-Pad / Left Stick | Arrow Keys / WASD|
| Confirm   | A / Start          | Enter / Space    |
| Back      | B / Back           | Escape           |

### Gameplay
| Note      | Controller              | Keyboard     |
|-----------|-------------------------|--------------|
| Left      | D-Pad Left / X / LT     | D / Left     |
| Down      | D-Pad Down / A / LB     | F / Down     |
| Up        | D-Pad Up / Y / RB       | J / Up       |
| Right     | D-Pad Right / B / RT    | K / Right    |
| Pause     | Start                   | Escape       |

## Troubleshooting
* **"No game content found" on the loading screen** – you installed the code-only `.msix` and there is no
  internet/zip: install the `_full.msix`, or upload `content.zip` (section 1.3).
* **Install fails with a dependency error** – also select the three `Dependencies\x64\*.appx` files.
* **Install fails "package already installed with a different version/signature"** – uninstall the old
  build first (Dev Home > select > Uninstall; this deletes LocalState – back up saves/mods first).
* **Stutter / out of memory on Xbox** – set the app type to **Game** (section 1.2 step 6).
* **Windows: "the publisher certificate could not be verified"** – import the `.cer` into
  `LocalMachine\TrustedPeople` (section 2).
