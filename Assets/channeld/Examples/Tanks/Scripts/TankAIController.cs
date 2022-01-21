using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Channeld.Examples.Tanks
{
    public class TankAIController : MonoBehaviour, ITankController
    {
        public TankChanneld tank;
        public float FiringPossibility = 0.1f;
        public float FiringStateDuration = 0.05f;
        public float RotatingPossibility = 0.2f;
        public float RotatingStateDuration = 1f;
        public float MovingPossibility = 0.2f;
        public float MovingStateDuration = 2f;
        public float IdleStateDuration = 1f;

        enum State { Idle, Moving, Rotating, Firing }
        State state = State.Idle;
        float dir = 0;
        float timer = 0;

        public bool GetFired()
        {
            return state == State.Firing;
        }

        public float GetMovement()
        {
            return state == State.Moving ? dir : 0;
        }

        public float GetRotation()
        {
            return state == State.Rotating ? dir : 0;
        }

        private void Awake()
        {
            if (tank == null)
            {
                tank = GetComponent<TankChanneld>();
            }
        }
        
        private void Update()
        {
            if (tank.isServer)
            {
                if (timer > 0)
                {
                    timer -= Time.deltaTime;
                    return;
                }

                var rnd = Random.value;
                float prob = 0;
                if (rnd < (prob += FiringPossibility))
                {
                    state = State.Firing;
                    timer = FiringStateDuration;
                }
                else if (rnd < (prob += RotatingPossibility))
                {
                    state = State.Rotating;
                    dir = Random.Range(-1f, 1f);
                    timer = RotatingStateDuration;
                }
                else if (rnd < (prob += MovingPossibility))
                {
                    state = State.Moving;
                    dir = Random.Range(-1f, 1f);
                    timer = MovingStateDuration;
                }
                else
                {
                    state = State.Idle;
                    timer = IdleStateDuration;
                }
            }
        }
    }
}