using System;
using UnityEditor;
using UnityEngine;

namespace HeroCap.EditorTools
{
    /// <summary>
    /// Batch-mode entry points, run cold (`herocap unity X`). A task throws on failure, which a
    /// cold launch turns into exit code 1.
    /// </summary>
    public static class Tasks
    {
        /// <summary>Nothing: opens the project, imports and compiles (setup's license check).</summary>
        public static void Noop()
        {
            if (EditorUtility.scriptCompilationFailed)
                throw new Exception("Tasks.Noop: scripts have compile errors");
            Debug.Log("Tasks.Noop: project opened, Unity " + Application.unityVersion);
        }

        /// <summary>
        /// Captures the recipes' clips to PNG frames (`herocap capture`): `-out &lt;dir&gt;`,
        /// `-recipe &lt;atlas&gt;` and `-clip &lt;name&gt;` (repeatable, default all), `-fps N`.
        /// </summary>
        public static void Capture() => Capturer.Run();
    }
}
