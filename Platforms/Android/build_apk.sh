#!/usr/bin/env bash
# Build the Android debug APK with the private toolchain from setup_toolchain.sh (no admin, no `dotnet workload install`).
#   bash Platforms/Android/build_apk.sh [Debug|Release]
# Output: Platforms/Android/bin/<cfg>/net9.0-android/com.fnfmonogame.engine-Signed.apk
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
A="${FNF_ANDROID_TOOLCHAIN:-$HERE/../../scratch/android}"
w() { cygpath -w "$1" 2>/dev/null || echo "$1"; }
export DOTNETSDK_WORKLOAD_MANIFEST_ROOTS="$(w "$A/manifests")"
export DOTNETSDK_WORKLOAD_PACK_ROOTS="$(w "$A/packs")"
export JAVA_HOME="$(w "$A/jdk")"
export ANDROID_HOME="$(w "$A/sdk")"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
cd "$HERE"
# always re-sign (an interrupted build can leave an aligned but UNSIGNED *-Signed.apk that MSBuild considers up to date)
rm -f bin/"${1:-Debug}"/net9.0-android/*-Signed.apk
dotnet build FNF_Android.csproj -c "${1:-Debug}" -p:AndroidSdkDirectory="$ANDROID_HOME" -p:JavaSdkDirectory="$JAVA_HOME" \
  -p:AcceptAndroidSDKLicenses=true -p:AndroidSdkBuildToolsVersion=35.0.0 -nologo -v:m "${@:2}"
APK=$(ls bin/"${1:-Debug}"/net9.0-android/*-Signed.apk)
"$A/jdk/bin/java" -jar "$(w "$A/sdk/build-tools/35.0.0/lib/apksigner.jar")" verify "$(w "$APK")" && echo "APK signed+verified: $APK"
