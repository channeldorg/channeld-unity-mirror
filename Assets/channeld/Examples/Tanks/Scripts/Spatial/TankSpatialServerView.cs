
using Channeldpb;
using Google.Protobuf;
using Mirror;
using System.Collections.Generic;
using UnityEngine;

namespace Channeld.Examples.Tanks.Scripts
{
    [CreateAssetMenu(fileName = "TankSpatialServerView", menuName = "ScriptableObjects/TankSpatialServerView", order = 2)]
    public class TankSpatialServerView : ChannelDataView
    {
        private Dictionary<int, uint> clientInChannels = new Dictionary<int, uint>();

        // TODO: Move to ChanneldNetworkManager
        private Dictionary<System.Type, GameObject> registeredPrefabs = new Dictionary<System.Type, GameObject>();
        public void RegisterPrefab(GameObject prefab)
        {
            var networkBehaviour = prefab.GetComponent<ChanneldNetworkBehaviour>();
            if (networkBehaviour != null)
            { 
                registeredPrefabs[networkBehaviour.GetType()] = prefab;
                Log.Info($"Registered prefab of type: {networkBehaviour.GetType()}");
            }
            else
                Log.Warning($"Failed to register prefab as it doesn't contain any NetworkBehaviour component");
        }

        protected override void InitChannels()
        {
            RegisterChannelDataParser(ChannelType.Spatial, new TankGameChannelData(), TankGameChannelData.Parser);

            if (NetworkManager.singleton.playerPrefab != null)
                RegisterPrefab(NetworkManager.singleton.playerPrefab);
            foreach (var prefab in NetworkManager.singleton.spawnPrefabs)
                RegisterPrefab(prefab);

            ChanneldTransport.GetServerSendChannelId = (mirrorConnId) =>
            {
                uint channelId;
                if (!clientInChannels.TryGetValue(mirrorConnId, out channelId))
                    channelId = ChanneldTransport.ServerSendChannelId ?? ChanneldConnection.GlobalChannelId;
                return channelId;
            };

            Connection.AddMessageHandler((uint)MessageType.SubToChannel, (_, channelId, msg) =>
            {
                var subResultMsg = (SubscribedToChannelResultMessage)msg;
                // A client is subscribed to a spatial channel the server owns (by the Global server)
                if (subResultMsg.ConnType == ConnectionType.Client && subResultMsg.ChannelType == ChannelType.Spatial)
                {
                    int mirrorConnId = (int)subResultMsg.ConnId;
                    if (!NetworkServer.connections.ContainsKey(mirrorConnId))
                    {
                        ChanneldTransport.Current.OnServerConnected?.Invoke(mirrorConnId);
                    }
                    
                    // Map the client to the channels, so ChanneldTransport.ServerSend() can set the right channelId.
                    clientInChannels[mirrorConnId] = channelId;

                    // MUST always use the EXACT same logic as the Global server
                    var startPos = NetworkManager.startPositions[mirrorConnId % NetworkManager.startPositions.Count];
                    // Code copied from NetworkManager.OnServerAddPlayer
                    var player = Instantiate(NetworkManager.singleton.playerPrefab, startPos.position, startPos.rotation); 
                    NetworkServer.AddPlayerForConnection(NetworkServer.connections[mirrorConnId], player);
                }
            });

            Connection.AddMessageHandler((uint)MessageType.UnsubFromChannel, (_, channelId, msg) =>
            {
                var resultMsg = (UnsubscribedFromChannelResultMessage)msg;
                Log.Info($"Server received unsub of conn({resultMsg.ConnId}), connType={resultMsg.ConnType}, channelType={resultMsg.ChannelType}, channelId={channelId}");
                // A client unsubscribed from the spatial channel
                if (resultMsg.ConnType == ConnectionType.Client && resultMsg.ChannelType == ChannelType.Spatial)
                {
                    ChanneldTransport.Current.OnServerDisconnected?.Invoke((int)resultMsg.ConnId);
                }
            });

            Connection.AddMessageHandler((uint)MessageType.ChannelDataHandover, HandleChannelDataHandover);


            Connection.SubToChannel(ChanneldConnection.GlobalChannelId, callback: (_) =>
            {
                Connection.CreateSpatialChannel("", callback: (resultMsg) =>
                {
                    Log.Info($"Created spatial channels: {resultMsg.SpatialChannelId.ToString()}");
                });
            });
        }

        private void HandleChannelDataHandover(ChanneldConnection _, uint channelId, IMessage msg)
        {
            var handoverMsg = (ChannelDataHandoverMessage)msg;
            var channelData = handoverMsg.Data.Unpack<TankGameChannelData>();
            Log.Info($"ChannelDataHandover from channel {handoverMsg.SrcChannelId} to {handoverMsg.DstChannelId}: {channelData.ToString()}");

            // Source spatial server - the channel data is handed over from
            if (Connection.SubscribedChannels.ContainsKey(handoverMsg.SrcChannelId))
            {
                /*
                foreach (var kv in channelData.TransformStates)
                {
                    NetworkIdentity ni;
                    if (!NetworkServer.spawned.TryGetValue(kv.Key, out ni))
                        continue;

                    // If the handover objects are no longer in the interest area of current server, delete them.
                    if (!Connection.SubscribedChannels.ContainsKey(handoverMsg.DstChannelId))
                    {
                        // NetworkServer.Destroy(ni.gameObject) will also destroy the gameObject in the client. We don't want that.
                        ServerDestroyObject(ni);
                    }
                    // If the handover objects are no longer in the authority area of current server,
                    // make sure them won't send ChannelDataUpdate message.
                    else if (!Connection.OwnedChannels.ContainsKey(handoverMsg.DstChannelId))
                    {
                        // Use Mirror's built-in authority property
                        ni.SetAuthority(false);
                    }

                    // Unsubscribe the client from the channel, if the server is the channel owner
                    if (ni.connectionToClient != null && Connection.OwnedChannels.ContainsKey(handoverMsg.SrcChannelId))
                    {
                        foreach (var conn in NetworkServer.connections)
                        {
                            if (conn.Value == ni.connectionToClient)
                            {
                                Connection.UnsubConnectionToChannel((uint)conn.Key, handoverMsg.SrcChannelId);
                            }
                        }
                    }
                }
                */

                // If the handover objects are no longer in the interest area of current server, delete them.
                if (!Connection.SubscribedChannels.ContainsKey(handoverMsg.DstChannelId))
                {
                    foreach (var kv in channelData.TransformStates)
                    {
                        NetworkIdentity ni;
                        if (NetworkServer.spawned.TryGetValue(kv.Key, out ni))
                        {
                            // NetworkServer.Destroy(ni.gameObject) will also destroy the gameObject in the client. We don't want that.
                            ServerDestroyObject(ni);
                        }

                        // Update the netId-owning channelId mapping
                        netIdOwningChannels.Remove(kv.Key);
                    }
                }
                // If the handover objects are no longer in the authority area of current server,
                // make sure them won't send ChannelDataUpdate message.
                else if (!Connection.OwnedChannels.ContainsKey(handoverMsg.DstChannelId))
                {
                    foreach (var kv in channelData.TransformStates)
                    {
                        NetworkIdentity ni;
                        if (NetworkServer.spawned.TryGetValue(kv.Key, out ni))
                        {
                            // Use Mirror's built-in authority property
                            ni.SetAuthority(false);
                        }
                    }
                }
            }

            // Destination spatial server - the channel data is handed over to
            if (Connection.SubscribedChannels.ContainsKey(handoverMsg.DstChannelId))
            {
                // Spawn the handover objects if them don't exist before
                if (!Connection.SubscribedChannels.ContainsKey(handoverMsg.SrcChannelId))
                {
                    foreach (var kv in channelData.TransformStates)
                    {
                        GameObject prefab;
                        // Tank
                        if (channelData.TankStates.ContainsKey(kv.Key))
                        {
                            var tankState = channelData.TankStates[kv.Key];
                            if (tankState.IsAI)
                                prefab = registeredPrefabs[typeof(TankChanneld)];
                            else
                                prefab = NetworkManager.singleton.playerPrefab;
                        }
                        // Projectile
                        else
                        {
                            prefab = registeredPrefabs[typeof(Projectile)];
                        }

                        GameObject spawned = Instantiate(prefab, kv.Value.GetUnityPosition().Value, kv.Value.GetUnityRotation() ?? Quaternion.identity);
                        if (kv.Value.Scale != null)
                            spawned.transform.localScale = kv.Value.GetUnityScale().Value;

                        spawned.name = $"{prefab.name} [channelId={handoverMsg.DstChannelId}]";
                        NetworkIdentity identity = spawned.GetComponent<NetworkIdentity>();

                        // Make sure the spawned object has the right owner connection.
                        int connId = (int)handoverMsg.ContextConnId;
                        NetworkConnectionToClient clientConn;
                        if (!NetworkServer.connections.TryGetValue(connId, out clientConn))
                        { 
                            ChanneldTransport.Current.OnServerConnected((int)handoverMsg.ContextConnId);
                            clientConn = NetworkServer.connections[connId];
                        }
                        
                        NetworkServer.Spawn(spawned, clientConn);
                        // Keep the object's netId the same when moving across the servers
                        NetworkServer.spawned.Remove(identity.netId);
                        identity.SetNetId(kv.Key);
                        NetworkServer.spawned[identity.netId] = identity;

                        // Apply the handover states to the spawned object
                        var dataProvider = spawned.GetComponent<IChannelDataProvider>();
                        dataProvider.OnChannelDataUpdated(channelData);
                    }
                }

            }


            foreach (var kv in channelData.TransformStates)
            {
                // Update the netId-owning channelId mapping
                netIdOwningChannels[kv.Key] = handoverMsg.DstChannelId;
            }
        }


        // Code copied from NetworkServer.DestroyObject()
        static void ServerDestroyObject(NetworkIdentity identity)
        {
            if (NetworkServer.aoi)
            {
                // This calls user code which might throw exceptions
                // We don't want this to leave us in bad state
                try
                {
                    NetworkServer.aoi.OnDestroyed(identity);
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                }
            }
            // Debug.Log($"DestroyObject instance:{identity.netId}");
            NetworkServer.spawned.Remove(identity.netId);

            identity.ClearObservers();

            // we are on the server. call OnStopServer.
            identity.OnStopServer();

            identity.SetDestroyCalled();
            UnityEngine.Object.Destroy(identity.gameObject);
        }

        protected override void UninitChannels()
        {
            if (Connection == null)
                return;

            foreach (var kv in Connection.OwnedChannels)
            {
                if (kv.Value.ChannelType == ChannelType.Spatial)
                {
                    Connection.RemoveChannel(kv.Key);
                }
            }
        }
    }
}
