using System;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;

namespace TCG.SwitchStreaming
{
    // Observer-only sink. No localization types, getters, initialization or fallback data.
    public static class LocalizationTrace
    {
        static int markers, sequence, firstFailure;
        [ThreadStatic] static string lastStage;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Stage(string stage, int sources, int globals, int languageLength, string code)
        {
            try
            {
                lastStage = stage;
                Write(stage, sources, globals, languageLength, code, null);
            }
            catch (Exception) { /* Only observer failures are isolated. */ }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Boundary(string stage)
        {
            try { Write(stage, -1, -1, -1, null, null); }
            catch (Exception) { /* Handler sentinels do not replace the last production stage. */ }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Snapshot(string stage, int sources, int globals, int languageLength, string code)
        {
            try { Write(stage, sources, globals, languageLength, code, null); }
            catch (Exception) { /* Snapshot observation must not affect the observed call. */ }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ObjectState(string stage, bool managedNull, bool sourceNull)
        {
            try { Write(stage, -1, -1, -1, null, " managedNull=" + managedNull + " sourceNull=" + sourceNull); }
            catch (Exception) { /* These flags are diagnostic only. */ }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Write(string stage, int sources, int globals, int languageLength, string code, string extra)
        {
            try
            {
                int ordinal = Interlocked.Increment(ref markers);
                if (ordinal <= 64)
                    Debug.Log(Format(Interlocked.Increment(ref sequence), stage, sources, globals, languageLength, code) + extra);
                else if (ordinal == 65)
                    Debug.Log(Format(Interlocked.Increment(ref sequence), "limit-reached", -1, -1, -1, null) + " limit=64 lastStage=" + lastStage);
            }
            catch (Exception) { /* Includes counters, TLS, formatting and output. */ }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Failure(string boundary, Exception error, int sources, int globals, int languageLength, string code)
        {
            try
            {
                if (Interlocked.CompareExchange(ref firstFailure, 1, 0) != 0) return;
                try
                {
                    Debug.LogError(Format(Interlocked.Increment(ref sequence), boundary + ".first-exception", sources, globals, languageLength, code) + " lastStage=" + lastStage + "\n" + error);
                    Interlocked.Exchange(ref firstFailure, 2);
                }
                catch (Exception)
                {
                    // A failed observer may retry at the next enclosing boundary.
                    Interlocked.CompareExchange(ref firstFailure, 0, 1);
                }
            }
            catch (Exception) { /* The original production exception is rethrown by its caller. */ }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static string Format(int sequence, string stage, int sources, int globals, int languageLength, string code)
        {
            return "[LOCALIZATION-PROBE] seq=" + sequence + " thread=" + Thread.CurrentThread.ManagedThreadId +
                " stage=" + stage + " sources=" + sources + " globals=" + globals +
                " languageNull=" + (languageLength < 0) + " languageLength=" + languageLength +
                " codeNull=" + (code == null) + " code=" + code;
        }
    }
}
