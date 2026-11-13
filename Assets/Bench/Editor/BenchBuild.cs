using System.IO;
using Siegeworks.Bench;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Siegeworks.BenchTools
{
    // Editor entry points for the Phase 1 size test, called from the unity CLI via script eval.
    public static class BenchBuild
    {
        const string ScenePath = "Assets/Scenes/Bench.unity";
        const string MatDir = "Assets/Bench/Materials";
        const string BuildPath = "Builds/Bench/Siegeworks.exe";

        // Creates (or recreates) the bench scene: camera, sun, ground, and a wired BenchRunner.
        public static string CreateScene()
        {
            Material a = TeamMaterial("UnitTeamA", new Color(0.25f, 0.45f, 0.9f));
            Material b = TeamMaterial("UnitTeamB", new Color(0.85f, 0.25f, 0.2f));

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(100f, 1f, 100f);
            Object.DestroyImmediate(ground.GetComponent<Collider>());

            var temp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Mesh capsule = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temp);

            var runner = new GameObject("Bench").AddComponent<BenchRunner>();
            var so = new SerializedObject(runner);
            so.FindProperty("unitMesh").objectReferenceValue = capsule;
            so.FindProperty("teamA").objectReferenceValue = a;
            so.FindProperty("teamB").objectReferenceValue = b;
            so.FindProperty("cam").objectReferenceValue = Camera.main;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            return $"scene={ScenePath} mesh={capsule.name} tris={capsule.triangles.Length / 3} " +
                   $"instancing={a.enableInstancing}/{b.enableInstancing} cam={(Camera.main != null)}";
        }

        // Sets the runner's defaults (used when no command-line args are given, e.g. Play mode).
        public static string Configure(string mode, int count, float seconds, float warmup)
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var runner = Object.FindFirstObjectByType<BenchRunner>();
            var so = new SerializedObject(runner);
            so.FindProperty("mode").stringValue = mode;
            so.FindProperty("count").intValue = count;
            so.FindProperty("seconds").floatValue = seconds;
            so.FindProperty("warmup").floatValue = warmup;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene);
            return $"configured mode={mode} count={count} seconds={seconds} warmup={warmup}";
        }

        public static string Build()
        {
            PlayerSettings.enableFrameTimingStats = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            AssetDatabase.SaveAssets();

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = BuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            var s = report.summary;
            return $"result={s.result} errors={s.totalErrors} warnings={s.totalWarnings} " +
                   $"size={s.totalSize / 1e6:F0}MB time={s.totalTime.TotalSeconds:F0}s path={Path.GetFullPath(BuildPath)}";
        }

        static Material TeamMaterial(string name, Color color)
        {
            Directory.CreateDirectory(MatDir);
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }
    }
}
