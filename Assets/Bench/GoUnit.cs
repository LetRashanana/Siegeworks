using Unity.Mathematics;
using UnityEngine;

namespace Siegeworks.Bench
{
    // One unit in plain-GameObject mode. Same rules as SimJob, run as a MonoBehaviour.Update.
    public class GoUnit : MonoBehaviour
    {
        public float2 Pos;
        public float Hp;
        public bool HasTarget => _target >= 0;

        GoArmy _army;
        Transform _t;
        int _index, _target = -1;
        float _cooldown, _searchTimer;

        public void Setup(GoArmy army, int index, float2 pos, float searchDelay)
        {
            _army = army;
            _t = transform;
            _index = index;
            Pos = pos;
            Hp = SimSettings.StartHp;
            _searchTimer = searchDelay;
            _t.position = new Vector3(pos.x, SimSettings.UnitY, pos.y);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            byte team = _army.Team[_index];
            float2 p = _army.Pos[_index];

            _searchTimer -= dt;
            if (_searchTimer <= 0f)
            {
                _target = _army.FindNearestEnemy(p, team);
                _searchTimer = SimSettings.NextSearchDelay(_index, _army.Frame);
            }

            float2 move = SimSettings.Forward(team);
            float2 facing = move;
            _cooldown = math.max(0f, _cooldown - dt);
            if (_target >= 0)
            {
                float2 to = _army.Pos[_target] - p;
                float d = math.length(to);
                facing = d > 1e-4f ? to / d : facing;
                if (d > SimSettings.AttackRange)
                {
                    move = facing;
                }
                else
                {
                    move = float2.zero;
                    if (_cooldown <= 0f)
                    {
                        _army.Units[_target].Hp -= SimSettings.Damage;
                        _cooldown = SimSettings.AttackCooldown;
                    }
                }
            }

            Pos = p + move * (SimSettings.Speed * dt) + _army.Separation(_index, p);
            _t.SetPositionAndRotation(
                new Vector3(Pos.x, SimSettings.UnitY, Pos.y),
                Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.y), Vector3.up));
        }
    }
}
