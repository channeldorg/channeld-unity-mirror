using Mirror;
using UnityEngine;

namespace Channeld.Examples.Tanks
{
    public class TankAISpawner : MonoBehaviour
    {
        public TankChanneld tankPrefab;
        private int index = 0;

        public void Spawn(int num)
        {
            for (int i = 0; i < num; i++)
            {
                var startPosition = NetworkManager.startPositions[index % NetworkManager.startPositions.Count];
                index++;
                var tank = Instantiate(tankPrefab, startPosition.position, startPosition.rotation);
                //tank.controller = tank.gameObject.AddComponent<TankAIController>();
                NetworkServer.Spawn(tank.gameObject);
            }
        }

        private void OnGUI()
        {
            if (!NetworkServer.active)
                return;

            if (GUI.Button(new Rect(10, 10, 100, 20), "Spawn Tanks"))
            {
                Spawn(10);
            }
        }
    }
}