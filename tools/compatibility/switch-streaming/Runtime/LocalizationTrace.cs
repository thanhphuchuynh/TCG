using System;
using System.Threading;
using UnityEngine;

namespace TCG.SwitchStreaming
{
    // Opt-in diagnostic sink. It receives primitive snapshots and never initializes localization.
    public static class LocalizationTrace
    {
        static int markers, sequence, firstFailure;
        [ThreadStatic] static string lastStage;
        public static void Stage(string stage, int sources, int globals, int languageLength, string code)
        {
            lastStage = stage;
            if (Interlocked.Increment(ref markers) > 64) return;
            try { Debug.Log(Format(Interlocked.Increment(ref sequence), stage, sources, globals, languageLength, code)); }
            catch (Exception) { /* A diagnostic output failure must not alter the observed operation. */ }
        }
        public static void Failure(string boundary, Exception error, int sources, int globals, int languageLength, string code)
        {
            if (Interlocked.CompareExchange(ref firstFailure, 1, 0) != 0) return;
            try { Debug.LogError(Format(Interlocked.Increment(ref sequence), boundary + ".first-exception", sources, globals, languageLength, code) + " lastStage=" + lastStage + "\n" + error); }
            catch (Exception) { /* Preserve the original exception even when diagnostic output is unavailable. */ }
        }
        static string Format(int sequence, string stage, int sources, int globals, int languageLength, string code)
        {
            return "[LOCALIZATION-PROBE] seq=" + sequence + " thread=" + Thread.CurrentThread.ManagedThreadId +
                " stage=" + stage + " sources=" + sources + " globals=" + globals +
                " languageNull=" + (languageLength < 0) + " languageLength=" + languageLength +
                " codeNull=" + (code == null) + " code=" + code;
        }
    }
}
