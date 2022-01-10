using Google.Protobuf;
using Mirror;
using System;
using UnityEngine;

namespace Channeld.Examples.Tanks
{
    public class TankAISpawner : NetworkBehaviour
    {
        public TankChanneld tankPrefab;
        public int prespawnNum = 0;
        private int index = 0;

        private void Awake()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-spawnai")
                {
                    int.TryParse(args[i + 1], out prespawnNum);
                    break;
                }
            }

            if (prespawnNum > 0)
            {
                GameState.OnDataChanged += Prespawn;
            }
        }

        private void Prespawn(uint channelId, GameState state, IMessage msg)
        {
            GameState.OnDataChanged -= Prespawn;
            ServerSpawn(prespawnNum);
        }

        private void ServerSpawn(int num)
        {
            if (!isServer)
                return;

            for (int i = 0; i < num; i++)
            {
                var startPosition = NetworkManager.startPositions[index % NetworkManager.startPositions.Count];
                index++;
                var tank = Instantiate(tankPrefab, startPosition.position, startPosition.rotation);
                //tank.controller = tank.gameObject.AddComponent<TankAIController>();
                NetworkServer.Spawn(tank.gameObject);
            }
        }

        [Command(requiresAuthority = false)]
        public void Spawn(int num)
        {
            ServerSpawn(num);
        }

        private void OnGUI()
        {
            if (!isClient && !isServer)
                return;

            if (GUI.Button(new Rect(10, 10, 100, 20), "Spawn Tanks"))
            {
                if (isClient)
                {
                    Spawn(10);
                }
                else if (isServer)
                {
                    ServerSpawn(10);
                }
            }
        }
    }
}