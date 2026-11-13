using System.IO;
using Siegeworks.Sim;
using Siegeworks.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Siegeworks.EditorTools
{
    // Builds the gray-box Town scene. Called from the unity CLI via script eval.
    public static class TownSetup
    {
        const string ScenePath = "Assets/Scenes/Town.unity";
        const string MatDir = "Assets/Game/Materials";
        const string ConfigPath = "Assets/Game/Config/DefaultTown.asset";

        public static string CreateScene()
        {
            // NewScene unloads unreferenced assets, so open the scene BEFORE loading any asset we wire in.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Material lit = LitMaterial("GrayboxLit", Color.white, instancing: true);
            Material ground = LitMaterial("Ground", Color.white, instancing: false);
            TownConfig config = Config();
            Camera cam = Camera.main;
            cam.farClipPlane = 1500f;
            cam.gameObject.AddComponent<RtsCamera>().boundsHalfSize = config.mapCells * config.cellSize * 0.5f;

            Mesh cube = PrimitiveMesh(PrimitiveType.Cube);
            Mesh capsule = PrimitiveMesh(PrimitiveType.Capsule);

            var view = new GameObject("Town").AddComponent<TownView>();
            var so = new SerializedObject(view);
            so.FindProperty("config").objectReferenceValue = config;
            so.FindProperty("cubeMesh").objectReferenceValue = cube;
            so.FindProperty("citizenMesh").objectReferenceValue = capsule;
            so.FindProperty("litMaterial").objectReferenceValue = lit;
            so.FindProperty("groundMaterial").objectReferenceValue = ground;
            so.FindProperty("cam").objectReferenceValue = cam;
            so.ApplyModifiedPropertiesWithoutUndo();

            // A reference to an object that isn't a saved asset serializes as null: refuse to save that scene.
            foreach (string field in new[] { "config", "litMaterial", "groundMaterial" })
            {
                var r = so.FindProperty(field).objectReferenceValue;
                if (r == null || !EditorUtility.IsPersistent(r))
                    throw new IOException($"TownView.{field} is not a saved asset; scene not saved");
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true),
                new EditorBuildSettingsScene("Assets/Scenes/Bench.unity", true),
            };
            return $"scene={ScenePath} config={AssetDatabase.GetAssetPath(config)} buildings={config.buildings.Length} " +
                   $"lit={AssetDatabase.GetAssetPath(lit)} ground={AssetDatabase.GetAssetPath(ground)}";
        }

        static TownConfig Config()
        {
            var c = AssetDatabase.LoadAssetAtPath<TownConfig>(ConfigPath);
            if (c != null) return c;
            EnsureFolder(Path.GetDirectoryName(ConfigPath).Replace('\\', '/'));
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<TownConfig>(), ConfigPath);
            AssetDatabase.SaveAssets();
            return Reload<TownConfig>(ConfigPath);
        }

        static Material LitMaterial(string name, Color color, bool instancing)
        {
            EnsureFolder(MatDir);
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                AssetDatabase.CreateAsset(new Material(Shader.Find("Universal Render Pipeline/Lit")), path);
                m = Reload<Material>(path);
            }
            m.SetColor("_BaseColor", color);
            m.enableInstancing = instancing;
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        static T Reload<T>(string path) where T : Object
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null) throw new IOException($"Created {path} but it didn't import");
            return a;
        }

        // Creates the folder through the AssetDatabase so assets made in it are imported at once.
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static Mesh PrimitiveMesh(PrimitiveType type)
        {
            var temp = GameObject.CreatePrimitive(type);
            Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temp);
            return mesh;
        }
    }
}
