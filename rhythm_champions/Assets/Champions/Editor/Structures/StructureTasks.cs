using System;
using System.IO;

namespace Champions.EditorTools.Structures
{
    /// <summary>
    /// The structure tasks `champions structure` runs (tools/src/champions_tools/cli.py), cold or through the warm editor:
    /// `-kind KIND` (or `all`), `-force` to bake a current kind again, `-out DIR` for a review.
    /// </summary>
    public static class StructureTasks
    {
        public static void Bake()
        {
            string kind = TaskArgs.Get("-kind", "all");
            bool force = TaskArgs.Has("-force");
            bool ok = kind == "all" ? StructureBaker.BakeAll(force) : StructureBaker.Bake(kind, force);
            if (!ok) throw new Exception($"StructureTasks.Bake: {kind}: vendor files missing (champions setup)");
        }

        public static void Review()
        {
            string kind = TaskArgs.Get("-kind", "all");
            string outDir = Path.GetFullPath(TaskArgs.Get("-out") ?? throw new Exception("StructureTasks: -out is required"));
            if (!StructureBaker.BakeAll()) throw new Exception("StructureTasks.Review: vendor files missing (champions setup)");
            StructureReview.Run(kind, outDir);
        }
    }
}
