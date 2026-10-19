using System;
using UnityEngine;

namespace LustHouse.Core
{
    /// <summary>
    /// Crash-proof logging.
    ///
    /// The save-recovery path must never take the app down (plan §2.13: "Never a crash
    /// or blocking dialog"). Unity's <c>Debug.Log</c> routes through a native handler that
    /// can itself fail — during shutdown, on a torn-down player loop, or in a headless
    /// test host. A logging failure inside an error handler must not become a fatal
    /// exception, so every call is guarded.
    /// </summary>
    public static class LustLog
    {
        /// <summary>Set false in headless/QA contexts to suppress output entirely.</summary>
        public static bool Enabled = true;

        public static void Info(string message)
        {
            if (!Enabled) return;
            try { Debug.Log(message); }
            catch (Exception) { /* a logging failure must never propagate */ }
        }

        public static void Warn(string message)
        {
            if (!Enabled) return;
            try { Debug.LogWarning(message); }
            catch (Exception) { }
        }

        public static void Error(string message)
        {
            if (!Enabled) return;
            try { Debug.LogError(message); }
            catch (Exception) { }
        }

        /// <summary>Logs with a Unity object context for click-to-ping in the editor.</summary>
        public static void Error(string message, UnityEngine.Object context)
        {
            if (!Enabled) return;
            try { Debug.LogError(message, context); }
            catch (Exception) { }
        }

        public static void Exception(string context, Exception e)
        {
            if (!Enabled) return;
            try { Debug.LogError($"[LustHouse] {context}: {e.GetType().Name}: {e.Message}"); }
            catch (Exception) { }
        }
    }
}