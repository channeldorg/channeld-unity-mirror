using Google.Protobuf;
using Mirror;
using System;
using System.Linq;
using UnityEngine;

namespace Channeld.Examples.Tanks
{
    public class TankAISpawner : NetworkBehaviour
    {
        public TankChanneld tankPrefab;
        public int prespawnNum = 0;
        public int batchSpawnNum = 10;
        private int index = 0;

        private void Awake()
        {
            CmdLineArgParser.Default.GetOptionValue("--spawn-ai", "-spawnai", ref prespawnNum);

            //TankChannelDataProvider.OnGenericDataChanged += OnFullChannelDataReceived;
        }

        /* TODO: move to ServerView
        private void OnFullChannelDataReceived(uint channelId, TankGameChannelData data)
        {
            TankChannelDataProvider.OnGenericDataChanged -= OnFullChannelDataReceived;

            if (!isServer)
                return;

            // Cases that there are tank states already exist when a server joins the channel:
            // 1. It's a spatial channel and the server has been launched to load-balance the channel. The tank states could be either belong to player's tanks, or AI's.
            // 2. The tank states were created by the previous owner of the channel. When disconnected from channeld, the server either failed to remove the states, or did that on purpose.
            // For now, we create the AI tanks from the states.
            foreach (var kv in data.TankStates)
            {
                TransformState transformState;
                if (data.TransformStates.TryGetValue(kv.Key, out transformState))
                {
                    var tank = Instantiate(tankPrefab, transformState.GetUnityPosition() ?? Vector3.zero, transformState.GetUnityRotation() ?? Quaternion.identity);
                    tank.health = kv.Value.Health;
                    NetworkServer.Spawn(tank.gameObject);
                }
            }

            if (prespawnNum > 0)
            {
                ServerSpawn(prespawnNum);
            }
        }
        */

        private void ServerSpawn(int num)
        {
            if (!isServer)
                return;


            if (index == 0 && ChanneldConnection.Instance != null)
            {
                var ownedChannelIds = ChanneldConnection.Instance.OwnedChannels.Keys;
                // Offset the first spawning point with the server 'index'
                if (ownedChannelIds.Count > 0)
                    index = batchSpawnNum * (int)ownedChannelIds.First();
            }

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
                    ServerSpawn(batchSpawnNum);
                }
            }
        }
    }
}