using System;
using System.Collections.Generic;
using System.Text;

namespace FNF_MonoGame.Engine.Colyseus;

// Dynamic decoder for @colyseus/schema v3/v4 (Colyseus 0.16 / 0.17) state patches. Types are NOT compiled in:
// they come from the server's reflection handshake (JOIN_ROOM), exactly like the JS SDK's Reflection.decode.
// Ported from @colyseus/schema src/decoder/{Decoder,DecodeOperation}.ts, encoding/decode.ts, Reflection.ts.
// Reflection-free (no System.Reflection / Activator) so it compiles and runs under .NET Native (UWP / Xbox).

public sealed class SchemaField
{
    public string Name;
    public string Type;          // primitive name ("string","number","boolean","int8",...), "ref", "map", "array", "set", "collection"
    public string ChildPrimitive; // collections of primitives ("string" for array:string)
    public int ChildTypeId = -1;  // schema type id for ref / collections of schemas
}

public sealed class SchemaType
{
    public int Id; public int ExtendsId = -1;
    public readonly List<SchemaField> Fields = new List<SchemaField>(); // index = field index (inherited fields first)
}

public interface IRefNode { }

/// <summary>A Schema instance: field values by name.</summary>
public sealed class SchemaObject : IRefNode
{
    public SchemaType Type;
    public readonly Dictionary<string, object> Values = new Dictionary<string, object>();
    public object Get(string name) => Values.TryGetValue(name, out var v) ? v : null;
    public string Str(string n) => Get(n) as string;
    public double Num(string n) => MsgPack.Num(Get(n));
    public bool Bool(string n) => Get(n) is bool b && b;
    public SchemaMap Map(string n) => Get(n) as SchemaMap;
    public SchemaArray Array(string n) => Get(n) as SchemaArray;
}

public sealed class SchemaMap : IRefNode
{
    public SchemaField Child;   // describes the value type
    public readonly Dictionary<string, object> Items = new Dictionary<string, object>();
    public readonly Dictionary<long, string> Indexes = new Dictionary<long, string>();
    public readonly List<string> Order = new List<string>(); // insertion order (MapSchema keeps insertion order)
}

public sealed class SchemaArray : IRefNode
{
    public SchemaField Child;
    public readonly List<object> Items = new List<object>();
    public List<string> AsStrings() { var l = new List<string>(); foreach (var x in Items) if (x != null) l.Add(x as string ?? x.ToString()); return l; }
}

public sealed class SchemaDecoder
{
    const byte SWITCH_TO_STRUCTURE = 255, TYPE_ID = 213;
    const int OP_ADD = 128, OP_REPLACE = 0, OP_DELETE = 64, OP_DELETE_AND_MOVE = 96, OP_MOVE_AND_ADD = 160, OP_DELETE_AND_ADD = 192,
              OP_CLEAR = 10, OP_REVERSE = 15, OP_DELETE_BY_REFID = 33, OP_ADD_BY_REFID = 129;

    public readonly Dictionary<int, SchemaType> Types = new Dictionary<int, SchemaType>();
    public readonly Dictionary<long, IRefNode> Refs = new Dictionary<long, IRefNode>();
    public SchemaObject State;
    public int Changes;   // incremented on every decoded field change (cheap "dirty" counter)

    public SchemaDecoder(Dictionary<int, SchemaType> types, int rootType)
    {
        foreach (var kv in types) Types[kv.Key] = kv.Value;
        State = new SchemaObject { Type = Types[rootType] };
        Refs[0] = State;
    }

    // ---------------------------------------------------------------- primitives (encoding/decode.ts)
    public static double Number(byte[] b, ref int o)
    {
        byte p = b[o++];
        if (p < 0x80) return p;
        switch (p)
        {
            case 0xca: { float f = BitConverter.ToSingle(b, o); o += 4; return f; }
            case 0xcb: { double d = BitConverter.ToDouble(b, o); o += 8; return d; }
            case 0xcc: return b[o++];
            case 0xcd: { int v = b[o] | b[o + 1] << 8; o += 2; return v; }
            case 0xce: { uint v = BitConverter.ToUInt32(b, o); o += 4; return v; }
            case 0xcf: { ulong v = BitConverter.ToUInt64(b, o); o += 8; return v; }
            case 0xd0: return (sbyte)b[o++];
            case 0xd1: { short v = BitConverter.ToInt16(b, o); o += 2; return v; }
            case 0xd2: { int v = BitConverter.ToInt32(b, o); o += 4; return v; }
            case 0xd3: { long v = BitConverter.ToInt64(b, o); o += 8; return v; }
        }
        if (p > 0xdf) return (0xff - p + 1) * -1;
        return 0; // undefined in JS
    }

    public static string String(byte[] b, ref int o)
    {
        byte p = b[o++]; int len = 0;
        if (p < 0xc0) len = p & 0x1f;
        else if (p == 0xd9) len = b[o++];
        else if (p == 0xda) { len = b[o] | b[o + 1] << 8; o += 2; }
        else if (p == 0xdb) { len = BitConverter.ToInt32(b, o); o += 4; }
        if (len > b.Length - o) len = b.Length - o;
        var s = Encoding.UTF8.GetString(b, o, len); o += len; return s;
    }

    public static bool StringCheck(byte[] b, int o) { byte p = b[o]; return (p < 0xc0 && p > 0xa0) || p == 0xd9 || p == 0xda || p == 0xdb; }

    private static object Primitive(string type, byte[] b, ref int o)
    {
        switch (type)
        {
            case "string": return String(b, ref o);
            case "number": return Number(b, ref o);
            case "boolean": return b[o++] > 0;
            case "int8": return (double)(sbyte)b[o++];
            case "uint8": return (double)b[o++];
            case "int16": { short v = BitConverter.ToInt16(b, o); o += 2; return (double)v; }
            case "uint16": { ushort v = BitConverter.ToUInt16(b, o); o += 2; return (double)v; }
            case "int32": { int v = BitConverter.ToInt32(b, o); o += 4; return (double)v; }
            case "uint32": { uint v = BitConverter.ToUInt32(b, o); o += 4; return (double)v; }
            case "int64": { long v = BitConverter.ToInt64(b, o); o += 8; return (double)v; }
            case "uint64": { ulong v = BitConverter.ToUInt64(b, o); o += 8; return (double)v; }
            case "float32": { float v = BitConverter.ToSingle(b, o); o += 4; return (double)v; }
            case "float64": { double v = BitConverter.ToDouble(b, o); o += 8; return v; }
            case "bigint64": { long v = BitConverter.ToInt64(b, o); o += 8; return (double)v; }
            case "biguint64": { ulong v = BitConverter.ToUInt64(b, o); o += 8; return (double)v; }
        }
        throw new FormatException("schema: unknown primitive type " + type);
    }

    // ---------------------------------------------------------------- Decoder.decode
    public void Decode(byte[] b, int offset)
    {
        int o = offset;
        IRefNode cur = State;
        while (o < b.Length)
        {
            if (b[o] == SWITCH_TO_STRUCTURE)
            {
                o++;
                long nextRefId = (long)Number(b, ref o);
                if (!Refs.TryGetValue(nextRefId, out var next)) { Console.WriteLine($"[COLYSEUS] refId {nextRefId} not found"); SkipStructure(b, ref o); continue; }
                FinishArray(cur);
                cur = next;
                continue;
            }
            bool ok;
            if (cur is SchemaObject so) ok = DecodeSchemaOp(so, b, ref o);
            else if (cur is SchemaMap sm) ok = DecodeMapOp(sm, b, ref o);
            else ok = DecodeArrayOp((SchemaArray)cur, b, ref o);
            if (!ok) { Console.WriteLine("[COLYSEUS] schema definition mismatch"); SkipStructure(b, ref o); }
        }
        FinishArray(cur);
    }

    private static void FinishArray(IRefNode n) { if (n is SchemaArray a) a.Items.RemoveAll(x => x == null); }

    private void SkipStructure(byte[] b, ref int o)
    {
        while (o < b.Length)
        {
            if (b[o] == SWITCH_TO_STRUCTURE)
            {
                int t = o + 1;
                if (Refs.ContainsKey((long)Number(b, ref t))) break;
            }
            o++;
        }
    }

    private bool DecodeSchemaOp(SchemaObject so, byte[] b, ref int o)
    {
        int first = b[o++];
        int op = (first >> 6) << 6;
        int index = first % (op == 0 ? 255 : op);
        if (index >= so.Type.Fields.Count) return false;
        var f = so.Type.Fields[index];
        so.Values.TryGetValue(f.Name, out var prev);
        var v = DecodeValue(op, f, prev, b, ref o, out bool deleted);
        if (deleted && v == null) so.Values.Remove(f.Name);
        if (v != null) so.Values[f.Name] = v;
        Changes++;
        return true;
    }

    private bool DecodeMapOp(SchemaMap m, byte[] b, ref int o)
    {
        int op = b[o++];
        if (op == OP_CLEAR) { m.Items.Clear(); m.Indexes.Clear(); m.Order.Clear(); Changes++; return true; }
        long index = (long)Number(b, ref o);
        string key;
        if ((op & OP_ADD) == OP_ADD) { key = String(b, ref o); m.Indexes[index] = key; }
        else m.Indexes.TryGetValue(index, out key);
        object prev = key != null && m.Items.TryGetValue(key, out var pv) ? pv : null;
        var v = DecodeValue(op, m.Child, prev, b, ref o, out bool deleted);
        if (deleted && key != null && op != OP_DELETE_AND_ADD) { m.Items.Remove(key); m.Indexes.Remove(index); m.Order.Remove(key); }
        if (v != null && key != null) { if (!m.Items.ContainsKey(key)) m.Order.Add(key); m.Items[key] = v; }
        Changes++;
        return true;
    }

    private bool DecodeArrayOp(SchemaArray a, byte[] b, ref int o)
    {
        int op = b[o++];
        int index;
        if (op == OP_CLEAR) { a.Items.Clear(); Changes++; return true; }
        if (op == OP_REVERSE) { a.Items.Reverse(); Changes++; return true; }
        if (op == OP_DELETE_BY_REFID)
        {
            long refId = (long)Number(b, ref o);
            Refs.TryGetValue(refId, out var pr);
            int i = a.Items.IndexOf(pr);
            if (i >= 0) a.Items[i] = null;
            Changes++; return true;
        }
        if (op == OP_ADD_BY_REFID)
        {
            long refId = (long)Number(b, ref o);
            index = -1;
            if (Refs.TryGetValue(refId, out var it)) index = a.Items.IndexOf(it);
            if (index < 0) index = a.Items.Count;
            // the encoder writes the refId again as the value (encodeArray + encodeValue)
            var v2 = DecodeValue(OP_ADD, a.Child, index < a.Items.Count ? a.Items[index] : null, b, ref o, out _);
            SetAt(a, index, v2, OP_ADD);
            Changes++; return true;
        }
        index = (int)Number(b, ref o);
        object prev = index < a.Items.Count ? a.Items[index] : null;
        var v = DecodeValue(op, a.Child, prev, b, ref o, out bool deleted);
        if (deleted && op != OP_DELETE_AND_ADD && index < a.Items.Count) a.Items[index] = null;
        if (v != null && !ReferenceEquals(v, prev)) SetAt(a, index, v, op);
        Changes++;
        return true;
    }

    private static void SetAt(SchemaArray a, int index, object value, int op)
    {
        if (index == 0 && op == OP_ADD && a.Items.Count > 0 && a.Items[0] != null) { a.Items.Insert(0, value); return; }
        if (op == OP_DELETE_AND_MOVE) { if (index < a.Items.Count) a.Items.RemoveAt(index); }
        while (a.Items.Count <= index) a.Items.Add(null);
        a.Items[index] = value;
    }

    /// <summary>DecodeOperation.decodeValue: returns the new value (null for DELETE).</summary>
    private object DecodeValue(int op, SchemaField f, object prev, byte[] b, ref int o, out bool deleted)
    {
        deleted = (op & OP_DELETE) == OP_DELETE;
        if (op == OP_DELETE) return null;
        if (f.Type == "ref")
        {
            long refId = (long)Number(b, ref o);
            Refs.TryGetValue(refId, out var value);
            if ((op & OP_ADD) == OP_ADD)
            {
                int typeId = f.ChildTypeId;
                if (o < b.Length && b[o] == TYPE_ID) { o++; typeId = (int)Number(b, ref o); }
                if (value == null) { value = NewObject(typeId); Refs[refId] = value; }
            }
            return value;
        }
        if (f.Type == "map" || f.Type == "array" || f.Type == "set" || f.Type == "collection")
        {
            long refId = (long)Number(b, ref o);
            if (!Refs.TryGetValue(refId, out var col))
            {
                var child = new SchemaField { Name = f.Name, Type = f.ChildPrimitive ?? "ref", ChildTypeId = f.ChildTypeId };
                if (f.Type == "map") col = new SchemaMap { Child = child };
                else col = new SchemaArray { Child = child };
                Refs[refId] = col;
            }
            return col;
        }
        return Primitive(f.Type, b, ref o);
    }

    private SchemaObject NewObject(int typeId)
    {
        Types.TryGetValue(typeId, out var t);
        return new SchemaObject { Type = t ?? new SchemaType { Id = typeId } };
    }

    // ---------------------------------------------------------------- Reflection.decode (handshake)
    /// <summary>Build a decoder from the reflection bytes sent in JOIN_ROOM.</summary>
    public static SchemaDecoder FromReflection(byte[] b, int offset)
    {
        // bootstrap types of the Reflection schema itself (Reflection.ts):
        //   0 Reflection { types: [ReflectionType], rootType: number }
        //   1 ReflectionType { id: number, extendsId: number, fields: [ReflectionField] }
        //   2 ReflectionField { name: string, type: string, referencedType: number }
        var rf = new SchemaType { Id = 2 };
        rf.Fields.Add(new SchemaField { Name = "name", Type = "string" });
        rf.Fields.Add(new SchemaField { Name = "type", Type = "string" });
        rf.Fields.Add(new SchemaField { Name = "referencedType", Type = "number" });
        var rt = new SchemaType { Id = 1 };
        rt.Fields.Add(new SchemaField { Name = "id", Type = "number" });
        rt.Fields.Add(new SchemaField { Name = "extendsId", Type = "number" });
        rt.Fields.Add(new SchemaField { Name = "fields", Type = "array", ChildTypeId = 2 });
        var r = new SchemaType { Id = 0 };
        r.Fields.Add(new SchemaField { Name = "types", Type = "array", ChildTypeId = 1 });
        r.Fields.Add(new SchemaField { Name = "rootType", Type = "number" });
        var boot = new SchemaDecoder(new Dictionary<int, SchemaType> { [0] = r, [1] = rt, [2] = rf }, 0);
        boot.Decode(b, offset);

        var refl = boot.State;
        var rtypes = refl.Array("types")?.Items ?? new List<object>();
        var raw = new Dictionary<int, SchemaObject>();
        foreach (var x in rtypes) if (x is SchemaObject so) raw[(int)so.Num("id")] = so;
        var types = new Dictionary<int, SchemaType>();
        foreach (var kv in raw)
        {
            var chain = new List<SchemaObject>();
            var cur = kv.Value;
            while (cur != null)
            {
                chain.Insert(0, cur);
                object ext = cur.Get("extendsId");
                cur = ext != null && raw.TryGetValue((int)MsgPack.Num(ext), out var p) && !ReferenceEquals(p, cur) ? p : null;
                if (chain.Count > 32) break;
            }
            var st = new SchemaType { Id = kv.Key, ExtendsId = kv.Value.Get("extendsId") != null ? (int)kv.Value.Num("extendsId") : -1 };
            foreach (var t in chain)
                foreach (var fo in t.Array("fields")?.Items ?? new List<object>())
                {
                    var fs = (SchemaObject)fo;
                    string name = fs.Str("name"), type = fs.Str("type");
                    var field = new SchemaField { Name = name };
                    if (fs.Get("referencedType") != null)
                    {
                        int refType = (int)fs.Num("referencedType");
                        if (refType >= 0 && raw.ContainsKey(refType)) { field.Type = type.Split(':')[0]; field.ChildTypeId = refType; }
                        else { var parts = type.Split(':'); field.Type = parts[0]; field.ChildPrimitive = parts.Length > 1 ? parts[1] : "string"; }
                    }
                    else field.Type = type;
                    st.Fields.Add(field);
                }
            types[kv.Key] = st;
        }
        int root = refl.Get("rootType") != null ? (int)refl.Num("rootType") : 0;
        if (!types.ContainsKey(root)) types[root] = new SchemaType { Id = root };
        return new SchemaDecoder(types, root);
    }
}
