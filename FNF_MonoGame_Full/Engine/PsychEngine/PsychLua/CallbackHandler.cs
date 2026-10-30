using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;

namespace FNF_MonoGame.Engine.PsychEngine.PsychLua;

/// <summary>
/// Psych Engine Lua callback dispatcher.
/// Original: psychlua/CallbackHandler.hx
/// </summary>
public static class CallbackHandler
{
    /// <summary>
    /// Invokes a registered Lua callback by name with the provided arguments.
    /// </summary>
    public static int Call(Script script, string fname, params object[] args)
    {
        try
        {
            if (script == null) return 0;

            object cbf = null;

            // Try the global callback table first
            var globals = script.Globals;
            if (globals[fname] is Closure closure)
                cbf = closure;

            if (cbf == null) return 0;

            object ret = null;
            if (cbf is Closure c)
            {
                var callArgs = args ?? System.Array.Empty<object>();
                ret = c.Call(callArgs);
            }
            else if (cbf is Delegate del)
            {
                ret = del.DynamicInvoke(args);
            }

            if (ret != null)
            {
                script.Globals["__ret"] = ret;
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CALLBACK ERROR] {ex.Message}");
        }
        return 0;
    }

    /// <summary>
    /// Register a global function accessible as a callback.
    /// </summary>
    public static void Register(Script script, string name, Delegate callback)
    {
        script?.Globals.Set(name, DynValue.FromObject(script, callback));
    }

    /// <summary>
    /// Register a global function accessible as a callback.
    /// </summary>
    public static void Register(Script script, string name, Func<object> callback)
    {
        Register(script, name, (Delegate)callback);
    }
}
