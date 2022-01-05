using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Channeld.Examples.Tanks
{
    public class TankAIController : MonoBehaviour, ITankController
    {
        public TankChanneld tank;

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
                if (rnd < 0.1f)
                {
                    state = State.Firing;
                    timer = 0.05f;
                }
                else if (rnd < 0.3f)
                {
                    state = State.Rotating;
                    dir = Random.Range(-1f, 1f);
                    timer = 1f;
                }
                else if (rnd < 0.5f)
                {
                    state = State.Moving;
                    dir = Random.Range(-1f, 1f);
                    timer = 2f;
                }
                else
                {
                    state = State.Idle;
                    timer = 1f;
                }
            }
        }
    }
}