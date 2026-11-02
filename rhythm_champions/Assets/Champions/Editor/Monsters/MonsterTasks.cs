using System;
using System.IO;
using Champions.Render.Crowd;

namespace Champions.EditorTools.Monsters
{
    /// <summary>
    /// The monster tasks `champions monster` runs (tools/src/champions_tools/monster.py), cold or through the warm editor:
    /// `-type TYPE` (or `all` for Bake), `-force` to bake a current type again, `-look default|trailer` and `-out DIR` for
    /// a review.
    /// </summary>
    public static class MonsterTasks
    {
        public static void Bake()
        {
            string type = TaskArgs.Get("-type", "all");
            bool force = TaskArgs.Has("-force");
            bool ok = type == "all" ? MonsterBaker.BakeAll(force) : MonsterBaker.Bake(type, force);
            if (!ok) throw new Exception($"MonsterTasks.Bake: {type}: vendor files missing (champions setup)");
        }

        public static void Export() => MonsterBaker.Export(Required("-type"));

        public static void Review() =>
            MonsterReview.Run(Required("-type"), MonsterLooks.Parse(TaskArgs.Get("-look")), Path.GetFullPath(Required("-out")));

        static string Required(string name) => TaskArgs.Get(name) ?? throw new Exception($"MonsterTasks: {name} is required");
    }
}
