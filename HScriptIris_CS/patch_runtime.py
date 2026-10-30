"""Post-process Haxe-generated C# runtime: case-insensitive member fallback for native C# objects.

Psych HScript scripts use Haxe camelCase names (game.boyfriend.x, game.defaultCamZoom) while the
MonoGame engine exposes PascalCase C# members (Boyfriend, X, DefaultCamZoom). Haxe's C# runtime
reflects with exact names only. We insert `field = __ciName(t, field, bf);` before each reflection
lookup: an exact match is always kept; otherwise the first case-insensitive member name is used.
"""
import pathlib, sys

p = pathlib.Path(__file__).parent / "gen" / "src" / "cs" / "internal" / "Runtime.cs"
s = p.read_text(encoding="utf-8")
if "__ciName" in s:
    print("runtime already patched"); sys.exit(0)

HELPER = """
		public static string __ciName(global::System.Type t, string field, global::System.Reflection.BindingFlags bf) {
			if (t == null || field == null) return field;
			if (t.GetMember(field, bf).Length > 0) return field;
			foreach (global::System.Reflection.MemberInfo m in t.GetMembers(bf)) {
				if (string.Equals(m.Name, field, global::System.StringComparison.OrdinalIgnoreCase)) return m.Name;
			}
			if (field == "length" && typeof(global::System.Collections.ICollection).IsAssignableFrom(t)) return "Count"; // Haxe Array.length on C# lists
			return field;
		}
"""

def inject(method_sig, anchor):
    global s
    i = s.index(method_sig)
    j = s.index(anchor, i)
    line_start = s.rfind("\n", 0, j) + 1
    indent = s[line_start:j]
    s = s[:line_start] + indent + "field = __ciName(t, field, bf);\n" + s[line_start:]

inject("public static object slowGetField(", "global::System.Reflection.FieldInfo f = t.GetField(")
inject("public static bool slowHasField(", "global::System.Reflection.MemberInfo[] mi = t.GetMember(")
inject("public static object slowSetField(", "global::System.Reflection.FieldInfo f = t.GetField(")
inject("public static object slowCallField(", "global::System.Reflection.MethodBase[] mis = t.GetMethods(")

k = s.index("public static object slowGetField(")
k = s.rfind("\n", 0, k) + 1
s = s[:k] + HELPER + s[k:]
p.write_text(s, encoding="utf-8")
print("runtime patched: case-insensitive member fallback")
