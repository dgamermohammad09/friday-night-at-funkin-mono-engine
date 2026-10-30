#!/usr/bin/env bash
# Compile OpenGL (DesktopGL + Android GLES) versions of FNF_MonoGame_Full/Shaders/*.fx into Platforms/Shared/shaders_gl.
# The .fx sources stay untouched (Windows DX bytecode in Content/shaders is built from them with /Profile:DirectX_11);
# GL needs ps_3_0 (MojoShader HLSL->GLSL) and VPOS instead of SV_POSITION, so a patched temp copy is compiled.
# Usage: bash Platforms/Shared/build_shaders_gl.sh      (needs `dotnet tool install -g dotnet-mgfxc --version 3.8.1.303`)
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
SRC="$HERE/../../FNF_MonoGame_Full/Shaders"
OUT="$HERE/shaders_gl"
TMP="${TMPDIR:-/tmp}/fnf_gl_fx"
MGFXC="${MGFXC:-$(command -v mgfxc || echo "$HOME/.dotnet/tools/mgfxc")}"
mkdir -p "$OUT" "$TMP"
w() { if command -v cygpath >/dev/null; then cygpath -w "$1"; else echo "$1"; fi; }
fail=0
for f in "$SRC"/*.fx; do
  n=$(basename "$f" .fx)
  sed -E -e 's/compile[[:space:]]+ps_4_0(_level_9_[13])?/compile ps_3_0/g' \
         -e 's/compile[[:space:]]+vs_4_0(_level_9_[13])?/compile vs_3_0/g' \
         -e 's/:[[:space:]]*SV_POSITION/: VPOS/g' -e 's/:[[:space:]]*SV_Position/: VPOS/g' "$f" > "$TMP/$n.fx"
  if "$MGFXC" "$(w "$TMP/$n.fx")" "$(w "$OUT/$n.mgfx")" /Profile:OpenGL >"$TMP/$n.log" 2>&1; then echo "ok   $n"
  else echo "FAIL $n"; cat "$TMP/$n.log"; fail=1; fi
done
exit $fail
