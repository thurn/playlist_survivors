using Champions.Sim;
using UnityEngine;

namespace Champions.Client.Loop
{
    /// <summary>
    /// The Boss Rush's act checkpoint on disk (boss-rush.md, Decisions 2): bossrush.checkpoint in the player's data folder,
    /// written as each act begins and after each death, removed by a victory or Abandon run, so quitting the game keeps
    /// the act. The title's Boss Rush entry offers to continue it.
    /// </summary>
    public static class RushSave
    {
        public static string Path => System.IO.Path.Combine(Application.persistentDataPath, "bossrush.checkpoint");

        /// <summary>The saved checkpoint, or null with none (or one that can't be read, which is logged).</summary>
        public static BossRushCheckpoint Load()
        {
            var c = BossRushCheckpoint.Load(Path, out string error);
            if (error != null) Debug.LogWarning("RushSave: " + error);
            return c;
        }

        public static void Save(BossRushCheckpoint checkpoint)
        {
            try
            {
                checkpoint.Save(Path);
                Debug.Log($"RushSave: boss {checkpoint.Boss + 1}, level {checkpoint.Level}, {checkpoint.Steps} build steps saved to {Path}");
            }
            catch (System.Exception e) when (e is System.IO.IOException || e is System.UnauthorizedAccessException)
            {
                Debug.LogWarning("RushSave: can't save to " + Path + ": " + e.Message);
            }
        }

        public static void Delete()
        {
            try { if (System.IO.File.Exists(Path)) System.IO.File.Delete(Path); }
            catch (System.Exception e) when (e is System.IO.IOException || e is System.UnauthorizedAccessException)
            {
                Debug.LogWarning("RushSave: can't remove " + Path + ": " + e.Message);
            }
        }
    }
}
