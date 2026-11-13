using NUnit.Framework;
using Siegeworks.Sim;
using Unity.Mathematics;
using UnityEngine;

namespace Siegeworks.Tests
{
    public class TownSimTests
    {
        TownConfig _cfg;

        [SetUp]
        public void SetUp() => _cfg = TownConfig.CreateDefault();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_cfg);

        TownSim NewSim() => new TownSim(_cfg, 1234);

        // A free spot on the map well away from the town hall.
        static int2 Spot(int i) => new int2(10 + i * 4, 10);

        void StepDays(TownSim s, int days) => s.Step(days * _cfg.dayLength);

        [Test]
        public void NewTown_HasTownHallStartPeopleAndResources()
        {
            var s = NewSim();
            Assert.AreEqual(1, s.Buildings.Count);
            Assert.AreEqual(BuildingType.TownHall, s.Buildings[0].Type);
            Assert.AreEqual(_cfg.startPeople, s.Population);
            Assert.AreEqual(_cfg.Get(BuildingType.TownHall).housing, s.Housing);
            Assert.AreEqual(_cfg.startFood, s.Food);
            Assert.AreEqual(_cfg.startWood, s.Wood);
        }

        [Test]
        public void PlaceHouse_SpendsWoodAndAddsHousing()
        {
            var s = NewSim();
            var house = _cfg.Get(BuildingType.House);
            Assert.AreEqual(PlaceResult.Ok, s.Place(BuildingType.House, Spot(0)));
            Assert.AreEqual(_cfg.startWood - house.woodCost, s.Wood);
            Assert.AreEqual(_cfg.Get(BuildingType.TownHall).housing + house.housing, s.Housing);
        }

        [Test]
        public void Place_RejectsOverlapOutOfBoundsAndShortWood()
        {
            var s = NewSim();
            Assert.AreEqual(PlaceResult.Blocked, s.Place(BuildingType.House, s.Buildings[0].Cell));
            Assert.AreEqual(PlaceResult.OutOfBounds, s.Place(BuildingType.House, new int2(-1, 5)));
            Assert.AreEqual(PlaceResult.OutOfBounds, s.Place(BuildingType.House, new int2(_cfg.mapCells - 2, 5)));
            s.Wood = 0;
            Assert.AreEqual(PlaceResult.NotEnoughWood, s.Place(BuildingType.House, Spot(0)));
            Assert.AreEqual(1, s.Buildings.Count, "a rejected placement must not add a building");
        }

        [Test]
        public void NewDay_EveryCitizenEats()
        {
            var s = NewSim();
            int pop = s.Population;
            StepDays(s, 1);
            Assert.AreEqual(2, s.Day);
            Assert.AreEqual(_cfg.startFood - pop * _cfg.foodPerCitizenPerDay, s.Food);
            Assert.AreEqual(0, s.HungryCount);
        }

        [Test]
        public void Births_FillFreeHousingUpToTheDailyBudget()
        {
            var s = NewSim();
            Assert.AreEqual(PlaceResult.Ok, s.Place(BuildingType.House, Spot(0)));
            Assert.AreEqual(PlaceResult.Ok, s.Place(BuildingType.House, Spot(1)));
            int budget = _cfg.globalBirthsPerDay + 2 * _cfg.Get(BuildingType.House).birthsPerDay;
            int before = s.Population;
            StepDays(s, 1);
            Assert.AreEqual(before + math.min(budget, s.Housing - before), s.Population);
        }

        [Test]
        public void Births_StopAtHousingCapacity()
        {
            var s = NewSim();
            s.Place(BuildingType.House, Spot(0));
            s.Food = s.FoodCap;
            StepDays(s, 10);
            Assert.AreEqual(s.Housing, s.Population);
            foreach (var b in s.Buildings) Assert.LessOrEqual(b.Occupants, b.Def.housing);
        }

        [Test]
        public void Births_NeedAFoodReserve()
        {
            var s = NewSim();
            s.Place(BuildingType.House, Spot(0));
            s.Food = s.Population;          // exactly one meal: nothing left in reserve after eating
            StepDays(s, 1);
            Assert.AreEqual(0, s.TotalBorn, "no reserve, no births");
            s.Food = 100;
            StepDays(s, 1);
            Assert.Greater(s.TotalBorn, 0, "with a reserve, births resume");
        }

        [Test]
        public void Starvation_TheUnfedDieOnTheirSecondMissedMeal()
        {
            _cfg.starveAfterMissedMeals = 2;
            var s = NewSim();
            s.Food = 3;
            int pop = s.Population;
            StepDays(s, 1);
            Assert.AreEqual(pop - 3, s.HungryCount, "food for 3 leaves the rest hungry");
            Assert.AreEqual(0, s.TotalDied, "nobody dies on their first missed meal");
            StepDays(s, 1);     // no food left: the hungry miss a second meal and die, the 3 go hungry
            Assert.AreEqual(pop - 3, s.TotalDied);
            Assert.AreEqual(3, s.Population);
            Assert.AreEqual(3, s.HungryCount);
        }

        [Test]
        public void Feeding_AgainClearsHunger()
        {
            var s = NewSim();
            s.Food = 0;
            StepDays(s, 1);
            Assert.AreEqual(s.Population, s.HungryCount);
            s.Food = 100;
            StepDays(s, 1);
            Assert.AreEqual(0, s.TotalDied, "food arrived in time");
            Assert.AreEqual(0, s.HungryCount, "a fed citizen is no longer hungry");
        }

        [Test]
        public void Shortfall_AlwaysHitsTheAlreadyHungryFirst()
        {
            var s = NewSim();
            int pop = s.Population;
            s.Food = pop - 2;           // two short every day
            StepDays(s, 1);
            Assert.AreEqual(2, s.HungryCount);
            s.Food = pop - 2;
            StepDays(s, 1);
            Assert.AreEqual(2, s.TotalDied, "the same two stay unfed and starve");
            Assert.AreEqual(0, s.HungryCount);
        }

        [Test]
        public void SameSeed_SameTown()
        {
            var a = NewSim();
            var b = NewSim();
            a.Place(BuildingType.House, Spot(0));
            b.Place(BuildingType.House, Spot(0));
            StepDays(a, 3);
            StepDays(b, 3);
            Assert.AreEqual(a.Population, b.Population);
            Assert.AreEqual(a.Food, b.Food);
            Assert.AreEqual(a.Citizens[0].Pos, b.Citizens[0].Pos);
        }
    }
}
