# Linux, macOS and Android builds

The engine source in `FNF_MonoGame_Full/` is shared by every build. Nothing is copied:

| Build | Project | MonoGame | Notes |
|---|---|---|---|
| Windows PC (main) | `FNF_MonoGame_Full/FNF_MonoGame.csproj` | WindowsDX | unchanged |
| Xbox / UWP | `FNF_MonoGame_Full/Xbox/FNF_Xbox.csproj` | WindowsUniversal | unchanged |
| Linux / macOS (and Windows-OpenGL) | `Platforms/DesktopGL/FNF_DesktopGL.csproj` | DesktopGL (SDL2 + OpenGL + OpenAL) | same UI as PC |
| Android | `Platforms/Android/FNF_Android.csproj` | Android (OpenGL ES) | own touch UI |

Platform code is chosen with `#if` symbols: `DESKTOPGL`, `ANDROID`, `FNF_CROSSPLATFORM` (both of them), `XBOX_UWP`.
The PC and Xbox builds do not define any of the new symbols, so their code and UI are exactly as before.

## Linux / macOS

Build / run (any OS with the .NET 8 SDK):

    dotnet build Platforms/DesktopGL/FNF_DesktopGL.csproj -c Debug
    dotnet publish Platforms/DesktopGL/FNF_DesktopGL.csproj -p:PublishProfile=linux-x64    # or osx-x64, osx-arm64, win-x64
    -> Platforms/DesktopGL/publish/<rid>/   (self-contained, no .NET install needed)

The ~1.1 GB `Content` folder is **not** copied into the publish folder. Put it next to the executable:

    ./link_content.sh /path/to/FNF_MonoGame_Full/Content          # symlink (or add --copy)
    ./FNF_DesktopGL

On macOS, `link_content.sh` also removes the download quarantine flag and ad-hoc signs the app host
(Apple Silicon will not start an unsigned arm64 binary). Linux needs the usual desktop libs (X11/Wayland, OpenGL);
SDL2 and OpenAL come with the build.

What differs from the Windows build:
* **Shaders**: `Content/shaders/*.mgfx` are DirectX bytecode. OpenGL versions of every `Shaders/*.fx` are in
  `Platforms/Shared/shaders_gl` (rebuild them with `bash Platforms/Shared/build_shaders_gl.sh` after editing a .fx);
  they are shipped as `Content-gl/shaders` and picked automatically.
* **Video cutscenes**: MonoGame DesktopGL has no video player. The game uses the system `ffmpeg`
  (Linux: `sudo apt install ffmpeg`, macOS: `brew install ffmpeg`, or put an `ffmpeg` binary next to the game /
  set `FNF_FFMPEG=/path/to/ffmpeg`). Without ffmpeg the cutscenes are skipped and the song starts normally.
* **File names**: Linux file systems are case-sensitive; asset lookups fall back to a case-insensitive match.
* **Audio**: same decoder (NAudio.Core + NVorbis) and MonoGame output as on Windows.
* Logs / saves / mods are next to the executable (log falls back to `~/.local/share/FNF_MonoGame` if that folder is read-only).

## Android

Build (no admin rights and no `dotnet workload install` needed):

    bash Platforms/Android/setup_toolchain.sh    # once: Android workload packs, OpenJDK 17, Android SDK 35 -> scratch/android (~0.9 GB)
    bash Platforms/Android/build_apk.sh          # -> Platforms/Android/bin/Debug/net9.0-android/com.fnfmonogame.engine-Signed.apk

(With a normal setup — `dotnet workload install android` + Android Studio SDK — a plain
`dotnet build Platforms/Android/FNF_Android.csproj` works too.)

Install: `adb install -r com.fnfmonogame.engine-Signed.apk` (arm64 phones/tablets, Android 7+).

**Game data**: the 1.1 GB Content is too big for an APK, so it is installed once next to the app:

    adb push FNF_MonoGame_Full/Content /sdcard/Android/data/com.fnfmonogame.engine/files/

or copy a `Content.zip` into that folder (USB file transfer) - the app unpacks it on first start. Until the data is
there the app shows these instructions instead of crashing. Saves, mods and `game_log.txt` live in the same folder.
(For a Play Store release the same folder would be filled by a Play Asset Delivery pack or a download on first run.)

**Android UI** (Android only - `Platforms/Android/TouchControls.cs`, the PC/Xbox UI is unchanged):
* In a song: the screen is split into 4 tall touch lanes (left / down / up / right, like the FNF mobile ports;
  multi-touch for chords and holds, you can slide between lanes) and a PAUSE button at the top centre.
  The Android Back button also pauses.
* In menus, pause and game over: big on-screen buttons - note-arrow D-pad (bottom left), A = accept and
  B = back (bottom right). Swipe anywhere to move (drag up = next item, drag sideways = change page/difficulty),
  tap anywhere else to accept. The Android Back button = back.
* The pad hides itself when a gamepad is used.

Known limits on Android: no video cutscenes (skipped), no on-screen keyboard for text fields (online lobby names),
very large stages need a phone with plenty of RAM (the PC build caches up to ~2.5 GB of textures in heavy songs).

## Testing on a PC

* `dotnet build Platforms/DesktopGL/FNF_DesktopGL.csproj -p:TouchTest=true -o <dir>` builds the desktop OpenGL
  version WITH the Android touch layer (mouse = finger, `FNF_DEVTOUCH="t:x,y[:dur[:x2,y2]];..."` = scripted fingers),
  to check the Android UI without a phone.
