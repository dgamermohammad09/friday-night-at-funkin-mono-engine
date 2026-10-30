#!/usr/bin/env bash
# Minimal, non-admin Android toolchain for `dotnet build` of net9.0-android (no `dotnet workload install`, which needs
# admin rights for C:\Program Files\dotnet). Layout under $A (= scratch/android):
#   manifests/9.0.100/<manifest id>/   -> DOTNETSDK_WORKLOAD_MANIFEST_ROOTS   (mono toolchain manifests missing on this PC)
#   packs/packs/<pack id>/<version>/   -> DOTNETSDK_WORKLOAD_PACK_ROOTS       (Android SDK pack + ref + runtime packs)
#   jdk/                               -> JavaSdkDirectory  (Microsoft OpenJDK 17)
#   sdk/                               -> AndroidSdkDirectory (platforms;android-35, build-tools;35.0.0, platform-tools)
set -e
A=/c/fnfwt/w_weekend1/scratch/android
mkdir -p "$A/dl" "$A/manifests/9.0.100" "$A/packs/packs" "$A/sdk"
cd "$A/dl"
nupkg() { # id version destdir
  local id=$1 v=$2 d=$3 f="$1.$2.nupkg"
  [ -d "$d" ] && { echo "have $id"; return; }
  curl -sSL -o "$f" "https://api.nuget.org/v3-flatcontainer/$id/$v/$f"
  mkdir -p "$d"; (cd "$d" && unzip -qo "$A/dl/$f" -x '_rels/*' 'package/*' '[Content_Types].xml' '*.nupkg' '*.signature.p7s' ) ; rm -f "$f"; echo "ok $id $(du -sh "$d" | cut -f1)"
}
# manifests (nupkg/data/* = WorkloadManifest.json/.targets)
for m in current net8; do
  id=microsoft.net.workload.mono.toolchain.$m.manifest-9.0.100
  d="$A/manifests/9.0.100/microsoft.net.workload.mono.toolchain.$m"
  if [ ! -d "$d" ]; then
    curl -sSL -o m.zip "https://api.nuget.org/v3-flatcontainer/$id/9.0.20/$id.9.0.20.nupkg"
    mkdir -p "$d/9.0.20"; unzip -qo m.zip 'data/*' -d mtmp; cp mtmp/data/* "$d/9.0.20/"; rm -rf mtmp m.zip; echo "ok manifest $m"
  fi
done
P="$A/packs/packs"
nupkg microsoft.android.sdk.windows 35.0.78 "$P/Microsoft.Android.Sdk.Windows/35.0.78"
nupkg microsoft.android.ref.35 35.0.78 "$P/Microsoft.Android.Ref.35/35.0.78"
nupkg microsoft.android.runtime.35.android-arm64 35.0.78 "$P/Microsoft.Android.Runtime.35.android-arm64/35.0.78"
nupkg microsoft.netcore.app.runtime.mono.android-arm64 9.0.20 "$P/Microsoft.NETCore.App.Runtime.Mono.android-arm64/9.0.20"
nupkg microsoft.net.runtime.monotargets.sdk 9.0.20 "$P/Microsoft.NET.Runtime.MonoTargets.Sdk/9.0.20"
df -h /c | tail -1
# JDK 17 (Microsoft build)
if [ ! -d "$A/jdk" ]; then
  curl -sSL -o jdk.zip "https://aka.ms/download-jdk/microsoft-jdk-17.0.14-windows-x64.zip"
  unzip -q jdk.zip -d jdktmp; rm jdk.zip; mv jdktmp/* "$A/jdk"; rmdir jdktmp
  rm -rf "$A/jdk/lib/src.zip" "$A/jdk/jmods" "$A/jdk/demo"; echo "ok jdk $(du -sh "$A/jdk" | cut -f1)"
fi
df -h /c | tail -1
# Android SDK pieces straight from Google's repository (no cmdline-tools needed)
sdkzip() { # url destParent finalName
  local url=$1 parent=$2 name=$3
  [ -d "$parent/$name" ] && { echo "have $name"; return; }
  curl -sSL -o s.zip "$url"; mkdir -p "$parent" stmp; unzip -q s.zip -d stmp; rm s.zip
  mv stmp/* "$parent/$name"; rmdir stmp; echo "ok $name $(du -sh "$parent/$name" | cut -f1)"
}
sdkzip https://dl.google.com/android/repository/platform-35_r02.zip "$A/sdk/platforms" android-35
sdkzip https://dl.google.com/android/repository/build-tools_r35_windows.zip "$A/sdk/build-tools" 35.0.0
sdkzip https://dl.google.com/android/repository/platform-tools-latest-windows.zip "$A/sdk" platform-tools
df -h /c | tail -1
echo ALLDONE
