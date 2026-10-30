#!/usr/bin/env bash
# Regenerates the C# HScript runtime from the ORIGINAL hscript-iris Haxe source (Psych Engine 1.0's
# HScript library) using the Haxe C# target, then applies fixes for known Haxe->C# codegen bugs.
set -eu
cd "$(dirname "$0")"
TK="$(cygpath -u "$LOCALAPPDATA")/HaxeToolkit"
export PATH="$TK/haxe:$TK/neko:$PATH"
export HAXEPATH="$(cygpath -w "$TK/haxe")" HAXE_STD_PATH="$(cygpath -w "$TK/haxe/std")"

rm -rf gen
haxe build.hxml

# Haxe C# codegen bug: a do-while with a labelled break emits `while (cond)label1: {};`, which is
# invalid C#. The label is the loop's exit point (SBreak), so it belongs after the loop statement.
find gen/src -name '*.cs' -print0 | xargs -0 sed -i -E 's/^([[:space:]]*)(while \(.*\))(label[0-9]+): \{\};$/\1\2;\n\1\3: {};/'

python patch_runtime.py
echo "Generated $(find gen/src -name '*.cs' | wc -l) C# files"
