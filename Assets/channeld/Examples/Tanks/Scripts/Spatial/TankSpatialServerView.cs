
using Channeld.Spatial;
using Channeldpb;
using Google.Protobuf;
using Mirror;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Channeld.Examples.Tanks.Scripts
{
    [CreateAssetMenu(fileName = "TankSpatialServerView", menuName = "ScriptableObjects/TankSpatialServerView", order = 2)]
    public class TankSpatialServerView : ChannelDataView
    {
        // Map the client to the channels, so ChanneldTransport.ServerSend() can set the right channelId.
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

            // Replace Mirror's NetworkConnectionToClient for customized spawning process
            NetworkServer.ConstructClientConnection = (connectionId) => new SpatialNetworkConnectionToClient(connectionId, (netId) =>
            {
                uint channelId;
                if (NetworkServer.spawned.TryGetValue(netId, out var ni))
                {
                    /* WRONG! The client connection's channelId for message forwarding doesn't necessarily equal the channelId a network object should be spawned in.
                     * Example: a player summons a network object that falls in another spatial channel.
                    if (clientInChannels.TryGetValue(connectionId, out channelId))
                    */
                    if (Connection.TryGetSpatialChannelId(ni.transform.position, out channelId))
                    {
                        Log.Info($"Spatial server found mapping of netId: {netId} -> channelId: {channelId}, connId: {connectionId}, spawned: {ni.gameObject.name}");
                        return channelId;
                    }
                }

                channelId = Connection.OwnedChannels.FirstOrDefault().Key;
                Log.Warning($"Spatial server cound not find mapping of netId: {netId}, using default channelId: {channelId}, connId: {connectionId}, spawned: {ni.gameObject.name}");
                return channelId;
            });

            Connection.AddMessageHandler((uint)MessageType.SubToChannel, (_, channelId, msg) =>
            {
                var subResultMsg = (SubscribedToChannelResultMessage)msg;
                // A client is subscribed to a spatial channel the server owns (by the Global server)
                if (subResultMsg.ConnType == ConnectionType.Client && subResultMsg.ChannelType == ChannelType.Spatial &&
                    subResultMsg.SubOptions.DataAccess == ChannelDataAccess.WriteAccess)
                {
                    int mirrorConnId = (int)subResultMsg.ConnId;
                    if (!NetworkServer.connections.ContainsKey(mirrorConnId))
                    {
                        ChanneldTransport.Current.OnServerConnected?.Invoke(mirrorConnId);
                    }
                    
                    clientInChannels[mirrorConnId] = channelId;

                    // MUST always use the EXACT same logic as the Global server
                    var startPos = NetworkManager.startPositions[mirrorConnId % NetworkManager.startPositions.Count];
                    // Code copied from NetworkManager.OnServerAddPlayer
                    var player = Instantiate(NetworkManager.singleton.playerPrefab, startPos.position, startPos.rotation); 
                    player.name = $"{NetworkManager.singleton.playerPrefab.name} [connId={mirrorConnId}]";
                    /* At this moment, the netId is still 0
                    // Set up the netId-channelId mapping before sending the spawn message to client
                    netIdOwningChannels[player.GetComponent<NetworkIdentity>().netId] = channelId;
                    */
                    NetworkServer.AddPlayerForConnection(NetworkServer.connections[mirrorConnId], player);

                    Log.Info($"Server set up mapping of connId: {mirrorConnId} -> channelId: {channelId}, netId: {player.GetComponent<NetworkIdentity>().netId}");
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

                    clientInChannels.Remove((int)resultMsg.ConnId);
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
                // If the handover objects are no longer in the interest area of current server, delete them.
                if (!Connection.SubscribedChannels.ContainsKey(handoverMsg.DstChannelId))
                {
                    foreach (var kv in channelData.TransformStates)
                    {
                        if (NetworkServer.spawned.TryGetValue(kv.Key, out var ni))
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
                        if (NetworkServer.spawned.TryGetValue(kv.Key, out var ni))
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
                    int connId = (int)handoverMsg.ContextConnId;
                    if (!NetworkServer.connections.TryGetValue(connId, out var clientConn))
                    {
                        ChanneldTransport.Current.OnServerConnected((int)handoverMsg.ContextConnId);
                        clientConn = NetworkServer.connections[connId];
                        clientInChannels[connId] = handoverMsg.DstChannelId;
                        Log.Info($"Server updated mapping of connId: {connId} -> channelId: {handoverMsg.DstChannelId}");
                    }

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
                // Update the netId-owningChannelId mapping
                netIdOwningChannels[kv.Key] = handoverMsg.DstChannelId;

                // Update the clientConnId-channelId mapping
                if (NetworkServer.spawned.TryGetValue(kv.Key, out var ni))
                {
                    if (ni.connectionToClient != null)
                        clientInChannels[ni.connectionToClient.connectionId] = handoverMsg.DstChannelId;
                }
            }

            /*
            */
            // Switch the client's authority from the srcChannel to dstChannel
            Connection.SubConnectionToChannel(handoverMsg.ContextConnId, handoverMsg.SrcChannelId, new ChannelSubscriptionOptions()
            {
                DataAccess = ChannelDataAccess.ReadAccess
            });
            Connection.SubConnectionToChannel(handoverMsg.ContextConnId, handoverMsg.DstChannelId, new ChannelSubscriptionOptions()
            {
                DataAccess = ChannelDataAccess.WriteAccess
            });
        }

        /* Tried to query the channelId from channeld - but it can be too later when the response reaches.
        public override void AddChannelDataProviderToDefaultChannel(IChannelDataProvider provider)
        {
            //base.AddChannelDataProviderToDefaultChannel(provider);

            var ni = (provider as MonoBehaviour).GetComponent<NetworkIdentity>();
            Connection.QuerySpatialChannel(new Vector3[]{ni.transform.position}, (resultMsg) =>
            {
                AddChannelDataProvider(resultMsg.ChannelId[0], provider);
            });
        }
        */


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
