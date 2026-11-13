using System.Collections.Generic;
using Unity.Mathematics;

namespace Siegeworks.Sim
{
    public enum PlaceResult { Ok, OutOfBounds, Blocked, NotEnoughWood, NotEnoughStone }

    public class Building
    {
        public int Id;
        public BuildingType Type;
        public BuildingDef Def;
        public int2 Cell;       // footprint min corner
        public int2 Size;
        public int Occupants;
    }

    public struct Citizen
    {
        public float2 Pos;
        public float2 Goal;
        public int Home;            // building id
        public int MissedMeals;     // consecutive daily meals missed
        public bool Hungry => MissedMeals > 0;
    }

    // The town core as plain C#: no scene, no rendering, so tests and the balance bot can run it headless.
    // Buildings are never removed yet, so a building's id is its index in Buildings.
    public class TownSim
    {
        public readonly TownConfig Cfg;
        public int Day = 1;
        public float DayTime;
        public float Elapsed;
        public int Food, Wood, Stone;
        public int FoodCap, GoodsCap, Housing;
        public int TotalBorn, TotalDied;
        public string LastMessage = "";
        public readonly List<Building> Buildings = new List<Building>();
        public readonly List<Citizen> Citizens = new List<Citizen>();

        readonly int[] _occupancy;      // cell index -> building id + 1, 0 = empty
        Random _rng;

        public TownSim(TownConfig cfg, uint seed = 1)
        {
            Cfg = cfg;
            _rng = new Random(math.max(1u, seed));
            _occupancy = new int[cfg.mapCells * cfg.mapCells];

            var hall = cfg.Get(BuildingType.TownHall);
            AddBuilding(BuildingType.TownHall, new int2(cfg.mapCells / 2) - new int2(hall.size.x, hall.size.y) / 2);
            Food = math.min(cfg.startFood, FoodCap);
            Wood = math.min(cfg.startWood, GoodsCap);
            Stone = math.min(cfg.startStone, GoodsCap);
            for (int i = 0; i < math.min(cfg.startPeople, Housing); i++) SpawnCitizen(FindHome());
        }

        public int Population => Citizens.Count;
        public float DayProgress => DayTime / Cfg.dayLength;

        public int HungryCount
        {
            get { int n = 0; foreach (var c in Citizens) if (c.Hungry) n++; return n; }
        }

        public int BirthBudget
        {
            get { int n = Cfg.globalBirthsPerDay; foreach (var b in Buildings) n += b.Def.birthsPerDay; return n; }
        }

        // The map is centred on the world origin. CellToWorld gives a cell's min corner on the XZ plane.
        public float2 CellToWorld(int2 cell) => ((float2)cell - Cfg.mapCells * 0.5f) * Cfg.cellSize;
        public int2 WorldToCell(float2 w) => (int2)math.floor(w / Cfg.cellSize + Cfg.mapCells * 0.5f);
        public float2 Center(Building b) => CellToWorld(b.Cell) + (float2)b.Size * Cfg.cellSize * 0.5f;

        public PlaceResult CanPlace(BuildingType type, int2 cell)
        {
            var d = Cfg.Get(type);
            int2 size = new int2(d.size.x, d.size.y);
            if (math.any(cell < 0) || math.any(cell + size > Cfg.mapCells)) return PlaceResult.OutOfBounds;
            for (int y = 0; y < size.y; y++)
            for (int x = 0; x < size.x; x++)
                if (_occupancy[Index(cell + new int2(x, y))] != 0) return PlaceResult.Blocked;
            if (Wood < d.woodCost) return PlaceResult.NotEnoughWood;
            if (Stone < d.stoneCost) return PlaceResult.NotEnoughStone;
            return PlaceResult.Ok;
        }

        public PlaceResult Place(BuildingType type, int2 cell)
        {
            var r = CanPlace(type, cell);
            var d = Cfg.Get(type);
            if (r != PlaceResult.Ok)
            {
                LastMessage = r switch
                {
                    PlaceResult.OutOfBounds => "Can't build outside the map",
                    PlaceResult.Blocked => "Something is already there",
                    PlaceResult.NotEnoughWood => $"{d.label} needs {d.woodCost} wood",
                    _ => $"{d.label} needs {d.stoneCost} stone",
                };
                return r;
            }
            Wood -= d.woodCost;
            Stone -= d.stoneCost;
            AddBuilding(type, cell);
            LastMessage = $"Built {d.label}";
            return r;
        }

        // Advances game time. Day boundaries are hit exactly, so a big dt behaves like many small ones.
        public void Step(float dt)
        {
            while (dt > 0f)
            {
                float h = math.min(dt, Cfg.dayLength - DayTime);
                Advance(h);
                dt -= h;
                if (DayTime >= Cfg.dayLength - 1e-4f) NewDay();
            }
        }

        void Advance(float h)
        {
            Elapsed += h;
            DayTime += h;
            for (int i = 0; i < Citizens.Count; i++)
            {
                var c = Citizens[i];
                float2 to = c.Goal - c.Pos;
                float d = math.length(to);
                float step = Cfg.citizenSpeed * h;
                if (d <= step) { c.Pos = c.Goal; c.Goal = WanderGoal(c.Home); }
                else c.Pos += to / d * step;
                Citizens[i] = c;
            }
        }

        void NewDay()
        {
            Day++;
            DayTime = 0f;
            int ate = Eat(out int starved);
            int born = Births();
            LastMessage = $"Day {Day}: ate {ate}, born {born}, hungry {HungryCount}" +
                          (starved > 0 ? $", {starved} starved" : "");
        }

        // One meal a day. Whoever the food doesn't cover misses a meal, and the already-hungry are
        // the first left unfed, so a shortfall of X food predictably costs X citizens after
        // starveAfterMissedMeals days unless the player fixes it.
        int Eat(out int starved)
        {
            int per = math.max(1, Cfg.foodPerCitizenPerDay);
            int fed = math.min(Citizens.Count, Food / per);
            Food -= fed * per;
            int unfed = Citizens.Count - fed;

            var order = new List<int>(Citizens.Count);
            for (int i = 0; i < Citizens.Count; i++) order.Add(i);
            order.Sort((a, b) => Citizens[b].MissedMeals != Citizens[a].MissedMeals
                ? Citizens[b].MissedMeals.CompareTo(Citizens[a].MissedMeals)
                : a.CompareTo(b));

            var dead = new List<int>();
            for (int k = 0; k < order.Count; k++)
            {
                int i = order[k];
                var c = Citizens[i];
                if (k < unfed)
                {
                    c.MissedMeals++;
                    if (c.MissedMeals >= Cfg.starveAfterMissedMeals) dead.Add(i);
                }
                else c.MissedMeals = 0;
                Citizens[i] = c;
            }

            dead.Sort();
            for (int k = dead.Count - 1; k >= 0; k--) Kill(dead[k]);   // highest index first: swap-remove stays safe
            starved = dead.Count;
            return fed * per;
        }

        public bool GrowthBlockedByFood =>
            HungryCount > 0 || Food < Citizens.Count * Cfg.foodPerCitizenPerDay * Cfg.birthFoodReserveDays;

        int Births()
        {
            if (GrowthBlockedByFood) return 0;
            int born = math.min(Housing - Citizens.Count, BirthBudget);
            for (int i = 0; i < born; i++) SpawnCitizen(FindHome());
            TotalBorn += math.max(0, born);
            return math.max(0, born);
        }

        // Houses fill before the town hall.
        int FindHome()
        {
            foreach (var b in Buildings)
                if (b.Type != BuildingType.TownHall && b.Occupants < b.Def.housing) return b.Id;
            foreach (var b in Buildings)
                if (b.Occupants < b.Def.housing) return b.Id;
            return -1;
        }

        void SpawnCitizen(int home)
        {
            if (home < 0) return;
            Buildings[home].Occupants++;
            float2 start = WanderGoal(home);
            Citizens.Add(new Citizen { Pos = start, Goal = WanderGoal(home), Home = home });
        }

        void Kill(int i)
        {
            Buildings[Citizens[i].Home].Occupants--;
            int last = Citizens.Count - 1;
            Citizens[i] = Citizens[last];
            Citizens.RemoveAt(last);
            TotalDied++;
        }

        float2 WanderGoal(int home)
        {
            var b = Buildings[home];
            float inner = math.cmax(b.Size) * Cfg.cellSize * 0.5f;
            float angle = _rng.NextFloat(0f, 2f * math.PI);
            float dist = _rng.NextFloat(inner, inner + Cfg.wanderRadius);
            return Center(b) + new float2(math.cos(angle), math.sin(angle)) * dist;
        }

        Building AddBuilding(BuildingType type, int2 cell)
        {
            var d = Cfg.Get(type);
            var b = new Building { Id = Buildings.Count, Type = type, Def = d, Cell = cell, Size = new int2(d.size.x, d.size.y) };
            for (int y = 0; y < b.Size.y; y++)
            for (int x = 0; x < b.Size.x; x++)
                _occupancy[Index(cell + new int2(x, y))] = b.Id + 1;
            Buildings.Add(b);

            Housing = FoodCap = GoodsCap = 0;
            foreach (var o in Buildings)
            {
                Housing += o.Def.housing;
                FoodCap += o.Def.foodStorage;
                GoodsCap += o.Def.goodsStorage;
            }
            return b;
        }

        int Index(int2 c) => c.y * Cfg.mapCells + c.x;
    }
}
