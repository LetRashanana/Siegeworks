using System;
using UnityEngine;

namespace Siegeworks.Sim
{
    [Serializable]
    public struct BuildingDef
    {
        public BuildingType type;
        public string label;
        public Vector2Int size;         // footprint in cells
        public int woodCost;
        public int stoneCost;
        public int housing;             // citizens it can house
        public int birthsPerDay;        // extra births it adds to the daily budget
        public int goodsStorage;        // wood and stone capacity (each)
        public int foodStorage;
        public float height;            // gray-box block height, metres
        public Color color;
    }

    // Every gameplay knob for the town core. The numbers are placeholders for Austin's tuning pass,
    // not DINO's values.
    [CreateAssetMenu(menuName = "Siegeworks/Town Config")]
    public class TownConfig : ScriptableObject
    {
        [Header("Map")]
        public int mapCells = 128;
        public float cellSize = 2f;

        [Header("Time")]
        public float dayLength = 120f;          // seconds of game time per day

        [Header("Start")]
        public int startFood = 100;
        public int startWood = 100;
        public int startStone = 30;
        public int startPeople = 10;

        [Header("Citizens")]
        public int globalBirthsPerDay = 4;      // shared budget, split across buildings with room
        public int foodPerCitizenPerDay = 1;
        public int starveAfterMissedMeals = 2;  // dies at this many missed daily meals in a row
        // Growth gate (the Crownrot lesson: without it growth always outruns food). No births while
        // anyone is hungry or while stored food after the meal is under this many days of eating.
        public float birthFoodReserveDays = 1f;
        public float citizenSpeed = 2f;
        public float wanderRadius = 6f;

        [Header("Buildings")]
        public BuildingDef[] buildings = DefaultBuildings();

        public BuildingDef Get(BuildingType type)
        {
            foreach (var b in buildings) if (b.type == type) return b;
            throw new ArgumentException($"TownConfig has no BuildingDef for {type}");
        }

        public static TownConfig CreateDefault() => CreateInstance<TownConfig>();

        static BuildingDef[] DefaultBuildings() => new[]
        {
            new BuildingDef { type = BuildingType.TownHall, label = "Town Hall", size = new Vector2Int(6, 6),
                housing = 10, goodsStorage = 200, foodStorage = 200, height = 4f, color = new Color(0.85f, 0.7f, 0.3f) },
            new BuildingDef { type = BuildingType.House, label = "House", size = new Vector2Int(3, 3), woodCost = 20,
                housing = 6, birthsPerDay = 1, height = 2f, color = new Color(0.8f, 0.65f, 0.5f) },
            new BuildingDef { type = BuildingType.Storage, label = "Storage", size = new Vector2Int(3, 3), woodCost = 25,
                goodsStorage = 200, foodStorage = 100, height = 2.5f, color = new Color(0.5f, 0.4f, 0.3f) },
        };
    }
}
