using System.Collections.Generic;
using Siegeworks.Sim;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Siegeworks.View
{
    // Gray-box presentation of TownSim: input, instanced drawing, IMGUI HUD. Holds no game rules.
    // Keys: H house, J storage, Esc/RMB cancel, LMB place, Space pause, 1-4 speed (1x/2x/4x/8x).
    public class TownView : MonoBehaviour
    {
        [SerializeField] TownConfig config;
        [SerializeField] Mesh cubeMesh;
        [SerializeField] Mesh citizenMesh;
        [SerializeField] Material litMaterial;      // URP Lit, GPU instancing on; tinted copies made at runtime
        [SerializeField] Material groundMaterial;
        [SerializeField] Camera cam;
        [SerializeField] uint seed = 1;

        public TownSim Sim { get; private set; }
        public float Speed = 1f;
        public BuildingType? BuildMode;

        readonly Dictionary<BuildingType, Material> _buildingMats = new Dictionary<BuildingType, Material>();
        readonly List<Matrix4x4> _fed = new List<Matrix4x4>(), _hungry = new List<Matrix4x4>(), _blocks = new List<Matrix4x4>();
        Material _citizenMat, _hungryMat, _ghostOk, _ghostBad;
        Matrix4x4[] _buf = new Matrix4x4[256];
        float _lastSpeed = 1f;
        int2 _hoverCell;
        bool _hoverOnMap;
        PlaceResult _hoverResult;
        Rect _hudTop, _hudBottom;
        GUIStyle _label, _button;

        void Start()
        {
            if (config == null)
            {
                Debug.LogWarning("[Town] No TownConfig assigned: using built-in defaults");
                config = TownConfig.CreateDefault();
            }
            Sim = new TownSim(config, seed);
            foreach (var d in config.buildings) _buildingMats[d.type] = Tint(d.color);
            _citizenMat = Tint(new Color(0.92f, 0.9f, 0.82f));
            _hungryMat = Tint(new Color(0.95f, 0.15f, 0.15f));
            _ghostOk = Tint(new Color(0.3f, 0.95f, 0.35f));
            _ghostBad = Tint(new Color(0.95f, 0.3f, 0.25f));
            BuildGround();
        }

        void Update()
        {
            HandleInput();
            Sim.Step(Time.deltaTime * Speed);
            Draw();
        }

        void HandleInput()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb != null)
            {
                if (kb.spaceKey.wasPressedThisFrame)
                {
                    if (Speed > 0f) { _lastSpeed = Speed; Speed = 0f; }
                    else Speed = _lastSpeed;
                }
                if (kb.digit1Key.wasPressedThisFrame) Speed = 1f;
                if (kb.digit2Key.wasPressedThisFrame) Speed = 2f;
                if (kb.digit3Key.wasPressedThisFrame) Speed = 4f;
                if (kb.digit4Key.wasPressedThisFrame) Speed = 8f;
                if (kb.hKey.wasPressedThisFrame) BuildMode = BuildingType.House;
                if (kb.jKey.wasPressedThisFrame) BuildMode = BuildingType.Storage;
                if (kb.escapeKey.wasPressedThisFrame) BuildMode = null;
            }
            if (mouse == null || BuildMode == null) return;
            if (mouse.rightButton.wasPressedThisFrame) { BuildMode = null; return; }

            Vector2 mp = mouse.position.ReadValue();
            var guiPoint = new Vector2(mp.x, Screen.height - mp.y);
            bool overHud = _hudTop.Contains(guiPoint) || _hudBottom.Contains(guiPoint);

            Ray ray = cam.ScreenPointToRay(mp);
            _hoverOnMap = !overHud && ray.direction.y < -1e-4f;
            if (!_hoverOnMap) return;
            Vector3 hit = ray.origin + ray.direction * (-ray.origin.y / ray.direction.y);
            var def = config.Get(BuildMode.Value);
            _hoverCell = Sim.WorldToCell(new float2(hit.x, hit.z)) - new int2(def.size.x, def.size.y) / 2;
            _hoverResult = Sim.CanPlace(BuildMode.Value, _hoverCell);

            if (mouse.leftButton.wasPressedThisFrame) Sim.Place(BuildMode.Value, _hoverCell);
        }

        void Draw()
        {
            foreach (var kv in _buildingMats)
            {
                _blocks.Clear();
                foreach (var b in Sim.Buildings)
                    if (b.Type == kv.Key) _blocks.Add(BlockMatrix(b.Cell, b.Size, b.Def.height));
                DrawList(kv.Value, cubeMesh, _blocks);
            }

            _fed.Clear();
            _hungry.Clear();
            var scale = new Vector3(0.6f, 0.6f, 0.6f);
            foreach (var c in Sim.Citizens)
            {
                var m = Matrix4x4.TRS(new Vector3(c.Pos.x, 0.6f, c.Pos.y), Quaternion.identity, scale);
                (c.Hungry ? _hungry : _fed).Add(m);
            }
            DrawList(_citizenMat, citizenMesh, _fed);
            DrawList(_hungryMat, citizenMesh, _hungry);

            if (BuildMode != null && _hoverOnMap)
            {
                var def = config.Get(BuildMode.Value);
                var ghost = BlockMatrix(_hoverCell, new int2(def.size.x, def.size.y), 0.3f);
                var rp = new RenderParams(_hoverResult == PlaceResult.Ok ? _ghostOk : _ghostBad);
                Graphics.RenderMesh(rp, cubeMesh, 0, ghost);
            }
        }

        Matrix4x4 BlockMatrix(int2 cell, int2 size, float height)
        {
            float2 min = Sim.CellToWorld(cell);
            float2 extent = (float2)size * config.cellSize;
            float2 centre = min + extent * 0.5f;
            return Matrix4x4.TRS(new Vector3(centre.x, height * 0.5f, centre.y), Quaternion.identity,
                new Vector3(extent.x * 0.94f, height, extent.y * 0.94f));
        }

        void DrawList(Material mat, Mesh mesh, List<Matrix4x4> list)
        {
            if (list.Count == 0) return;
            if (_buf.Length < list.Count) _buf = new Matrix4x4[Mathf.NextPowerOfTwo(list.Count)];
            list.CopyTo(_buf);
            var rp = new RenderParams(mat) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true };
            for (int start = 0; start < list.Count; start += 1023)
                Graphics.RenderMeshInstanced(rp, mesh, 0, _buf, Mathf.Min(1023, list.Count - start), start);
        }

        Material Tint(Color c)
        {
            var m = new Material(litMaterial) { enableInstancing = true };
            m.SetColor("_BaseColor", c);
            return m;
        }

        void BuildGround()
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Plane);
            g.name = "Ground";
            Destroy(g.GetComponent<Collider>());
            float size = config.mapCells * config.cellSize;
            g.transform.localScale = new Vector3(size / 10f, 1f, size / 10f);
            var m = new Material(groundMaterial);
            m.SetTexture("_BaseMap", GridTexture());
            m.SetTextureScale("_BaseMap", new Vector2(config.mapCells, config.mapCells));
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        static Texture2D GridTexture()
        {
            const int n = 32;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            var fill = new Color(0.55f, 0.57f, 0.52f);
            var line = new Color(0.44f, 0.46f, 0.42f);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                t.SetPixel(x, y, x == 0 || y == 0 ? line : fill);
            t.Apply();
            return t;
        }

        void OnGUI()
        {
            if (Sim == null) return;
            if (_label == null)
            {
                _label = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true };
                _button = new GUIStyle(GUI.skin.button) { fontSize = 15 };
            }

            int pop = Sim.Population;
            int perDay = pop * config.foodPerCitizenPerDay;
            string speed = Speed == 0f ? "<color=#ffcc44>PAUSED</color>" : $"{Speed:0}x";
            string growth = Sim.GrowthBlockedByFood ? "   <color=#ffcc44>Growth stalled: needs a food reserve</color>" : "";
            string lasts = perDay > 0 ? $"~{Sim.Food / perDay} days" : "-";

            _hudTop = new Rect(10, 10, 760, 132);
            GUI.Box(_hudTop, GUIContent.none);
            GUILayout.BeginArea(new Rect(_hudTop.x + 10, _hudTop.y + 6, _hudTop.width - 20, _hudTop.height - 12));
            GUILayout.Label($"<b>Day {Sim.Day}</b>  {Sim.DayProgress:P0}     Speed {speed}   (Space pause, 1-4 speed)", _label);
            GUILayout.Label($"Food {Sim.Food}/{Sim.FoodCap} (lasts {lasts})    Wood {Sim.Wood}/{Sim.GoodsCap}    Stone {Sim.Stone}/{Sim.GoodsCap}", _label);
            GUILayout.Label($"People {pop}/{Sim.Housing}    Hungry {Sim.HungryCount}    Born {Sim.TotalBorn}    Starved {Sim.TotalDied}{growth}", _label);
            GUILayout.Label(Sim.LastMessage, _label);
            GUILayout.EndArea();

            _hudBottom = new Rect(10, Screen.height - 90, 760, 80);
            GUI.Box(_hudBottom, GUIContent.none);
            GUILayout.BeginArea(new Rect(_hudBottom.x + 10, _hudBottom.y + 8, _hudBottom.width - 20, _hudBottom.height - 16));
            GUILayout.BeginHorizontal();
            foreach (var t in new[] { BuildingType.House, BuildingType.Storage })
            {
                var d = config.Get(t);
                string key = t == BuildingType.House ? "H" : "J";
                if (GUILayout.Button($"[{key}] {d.label}  {d.woodCost} wood", _button, GUILayout.Width(220), GUILayout.Height(30)))
                    BuildMode = t;
            }
            GUILayout.EndHorizontal();
            string mode = BuildMode == null
                ? "Pick a building.  WASD pan, wheel zoom, Q/E rotate."
                : $"Placing {config.Get(BuildMode.Value).label}: left-click to build, right-click/Esc to stop." +
                  (_hoverOnMap && _hoverResult != PlaceResult.Ok ? $"  <color=#ff7766>{_hoverResult}</color>" : "");
            GUILayout.Label(mode, _label);
            GUILayout.EndArea();
        }
    }
}
