// Polyfills for C# 8 range/index syntax on UWP (.NET Standard 2.0 / .NET Native)
// These types are built into .NET Core 3+ / .NET 5+ but missing on UWP.
#if NETFX_CORE || WINDOWS_UAP
namespace System
{
    internal readonly struct Index
    {
        private readonly int _value;
        public Index(int value, bool fromEnd = false)
        {
            _value = fromEnd ? ~value : value;
        }
        public int GetOffset(int length) => _value < 0 ? length + ~_value + 1 - 1 : _value;
        // Compiler uses this for ^n syntax
        public static Index FromEnd(int value) => new Index(value, true);
        public static Index FromStart(int value) => new Index(value, false);
        public static implicit operator Index(int value) => new Index(value);
    }

    internal readonly struct Range
    {
        public Index Start { get; }
        public Index End { get; }
        public Range(Index start, Index end) { Start = start; End = end; }
        public static Range StartAt(Index start) => new Range(start, Index.FromEnd(0));
        public static Range EndAt(Index end) => new Range(Index.FromStart(0), end);
        public static Range All => new Range(Index.FromStart(0), Index.FromEnd(0));
        public (int Offset, int Length) GetOffsetAndLength(int length)
        {
            int s = Start.GetOffset(length);
            int e = End.GetOffset(length);
            return (s, e - s);
        }
    }
}

// Extension to make string[Range] and T[][Range] work (compiler synthesizes calls to these)
namespace System.Runtime.CompilerServices
{
    internal static class RuntimeHelpers
    {
        public static string Substring(string s, Range range)
        {
            var (offset, length) = range.GetOffsetAndLength(s.Length);
            return s.Substring(offset, length);
        }

        public static T[] GetSubArray<T>(T[] array, Range range)
        {
            var (offset, length) = range.GetOffsetAndLength(array.Length);
            T[] result = new T[length];
            Array.Copy(array, offset, result, 0, length);
            return result;
        }

        public static void RunClassConstructor(RuntimeTypeHandle type)
        {
            // No-op for UWP polyfill; required by generated XAML type info.
        }
    }
}
#endif

namespace FNF_MonoGame.Engine
{
    /// <summary>
    /// Xbox/UWP no-op stub for Discord Rich Presence. Mirrors the desktop API so scenes compile,
    /// but does nothing because Discord RPC is unavailable on Xbox.
    /// </summary>
    public class DiscordRPCManager : IDisposable
    {
        public bool IsInitialized => false;

        public void Initialize(string customClientId = null) { }
        public void Update(float deltaTime = 0f) { }

        public void SetMenuState(string menuName = "Main Menu") { }
        public void SetFreeplayState() { }
        public void SetStoryModeState(string weekName = null) { }
        public void SetCharacterSelectState() { }
        public void SetOptionsState() { }
        public void SetTitleState() { }

        public void SetGameplayStart(string songName, string difficulty, string album, string opponent) { }
        public void UpdateGameplayPresence(string songName, string difficulty, bool isStoryMode, string weekName, bool isPracticeMode = false, bool isBotPlayMode = false) { }
        public void SetPaused(string songName, string difficulty, bool isStoryMode, string weekName, bool isPracticeMode = false, bool isBotPlayMode = false) { }
        public void SetResumed(string songName, string difficulty, bool isStoryMode, string weekName, bool isPracticeMode = false, bool isBotPlayMode = false) { }
        public void SetGameOver(string songName, string difficulty, bool isStoryMode, string weekName, bool isPracticeMode = false, bool isBotPlayMode = false) { }
        public void SetResultsState(string songName, string difficulty, int score, float accuracy, bool isStoryMode, string weekName) { }

        public void Dispose() { }
    }
}
