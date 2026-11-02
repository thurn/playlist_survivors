using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Champions.EditorTools.EdgeCuts
{
    /// <summary>
    /// The edge-cut pipeline's batch-mode steps (plan.md, Rendering &gt; Edge cuts), which `champions edgecut` runs
    /// around the model's recipe: <see cref="Export"/> the vendor model for it, <see cref="Bake"/> its cut into the
    /// crowd's assets (a structure's into the structure field's), <see cref="Review"/> the cut beside the original. Each
    /// takes `-model ID`.
    /// </summary>
    public static class EdgeCutTasks
    {
        static string ModelArg() => TaskArgs.Get("-model") ?? throw new Exception("EdgeCutTasks: needs -model ID");

        /// <summary>Writes Assets/Baked/EdgeCuts/ID/source.json and .bytes from the model's prefab (model.json).</summary>
        public static void Export()
        {
            var model = EdgeCutModel.Load(ModelArg());
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = model.Spawn();
            try
            {
                var source = new EdgeCutSource(root);
                source.SamplePose(model);
                var path = $"{EdgeCutModel.Dir(model.id)}/source.json";
                source.Write(path, model);
                Debug.Log($"EdgeCutTasks.Export: {model.id}: {source.Positions.Count} vertices, {source.Indices.Count / 3} triangles, " +
                          $"{source.Entries.Count} entries, {source.Renderers.Count} renderers, {model.NativeHeightM:F3} m tall at scale 1 -> {path}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        /// <summary>
        /// Bakes Assets/Baked/EdgeCuts/ID/cut.json into the crowd's assets and rewrites the library (<see cref="EdgeCutBaker"/>),
        /// or a structure's into the structure field's (<see cref="StructureBaker"/>).
        /// </summary>
        public static void Bake()
        {
            var model = EdgeCutModel.Load(ModelArg());
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                if (model.Static) StructureBaker.Bake(model);
                else EdgeCutBaker.Bake(model);
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
        }

        /// <summary>Renders the cut's review sheet into `-out DIR` (<see cref="EdgeCutReview"/>, a structure's <see cref="StructureReview"/>).</summary>
        public static void Review()
        {
            var model = EdgeCutModel.Load(ModelArg());
            var dir = TaskArgs.Get("-out") ?? throw new Exception("EdgeCutTasks.Review: needs -out DIR");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                if (model.Static) StructureReview.Run(model, dir);
                else EdgeCutReview.Run(model, dir);
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
        }
    }
}
