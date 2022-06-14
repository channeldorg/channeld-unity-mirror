using Channeldpb;
using Google.Protobuf;
using Mirror;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Channeld
{
    public class ChanneldTransport : Transport
    {
        [Header("Logging")]
        public bool showUserSpaceMessageLog = false;

        [Header("Server")]
        public string ServerAddressToChanneld = "127.0.0.1";
        public int ServerPortToChanneld = 11288;
        public int ServerConnectTimeoutMs = 1000;
        public string ServerChannelMetadata = "MirrorServer";

        [Header("Client")]
        // The client connects to the address Mirror passes to it. Use NetworkManager.networkAddress instead.
        //public string ClientAddressToChanneld = "127.0.0.1";
        public int ClientPortToChanneld = 12108;
        public int ClientConnectTimeoutMs = 3000;

        [Header("Misc.")]
        public float HeartbeatInterval = 0;

        internal ChanneldConnection serverConnection { get; private set; }
        internal ChanneldConnection clientConnection { get; private set; }

        public static ChanneldTransport Current
        {
            get{ return activeTransport as ChanneldTransport; }
        }

        // The connection has been authenticated by channeld and is ready to set up the channels.
        public static Action<ChanneldConnection> OnAuthenticated;
        // The GLOBAL channel owner (a.k.a. Master Server) receives the AuthResultMessage of ALL connections. It's for handling cases like 
        // 1) repeated unsuccessful login attempts
        // 2) the clients don't have authority to sub, so the Master Server will sub for them.
        public static Action<AuthResultMessage> OnGlobalOwnerReceivedAuthResult;

        // The channelId the ServerForwardMessage should be send to when calling ServerSend() 
        public static uint? ServerSendChannelId { get; internal set; } = null;
        // The channelId the ServerForwardMessage should be send to when calling ClientSend() 
        public static uint? ClientSendChannelId { get; private set; } = null;

        /*
        // The spawned object's netId mapping to the id of the channel that owns the object.
        private static Dictionary<uint, uint> netIdOwningChannels = new Dictionary<uint, uint>();

        public static uint SetOwningChannel(uint netId)
        {
            var transport = Transport.activeTransport as ChanneldTransport;
            uint channelId = 0;
            if (transport.serverConnection != null && ServerSendChannelId != null)
                channelId = ServerSendChannelId.Value;
            else if (transport.clientConnection != null && ClientSendChannelId != null)
                channelId = ClientSendChannelId.Value;
            netIdOwningChannels[netId] = channelId;

            Log.Info($"Set up mapping of netId:{netId} -> channelId:{channelId}");
            return channelId;
        }

        public static uint GetOwningChannel(uint netId)
        {
            var transport = Transport.activeTransport as ChanneldTransport;
            uint channelId = 0;
            if (transport.serverConnection != null && ServerSendChannelId != null)
                channelId = ServerSendChannelId.Value;
            else if (transport.clientConnection != null && ClientSendChannelId != null)
                channelId = ClientSendChannelId.Value;
            netIdOwningChannels.TryGetValue(netId, out channelId);
            return channelId;
        }

        public static void ResetOwningChannel(uint netId)
        {
            netIdOwningChannels.Remove(netId);
        }
        */

        void Awake()
        {
            var parser = CmdLineArgParser.Default;
            parser.GetOptionValue("--server-ip", "-sa", ref ServerAddressToChanneld);
            parser.GetOptionValue("--server-port", "-sp", ref ServerPortToChanneld);
            parser.GetOptionValue("--client-port", "-cp", ref ServerPortToChanneld);
            parser.GetOptionValue("--heartbeat", "-hb", ref HeartbeatInterval);

            // TODO: implement heartbeat to channeld
            NetworkTime.PingFrequency = HeartbeatInterval;

            Log.Debug("ChanneldTransport initialized.");
        }

        #region Server Logic

        public override void ServerStart()
        {
            if (serverConnection == null)
                serverConnection = new ChanneldConnection(ConnectionType.Server);

            serverConnection.ShowUserSpaceMessageLog = showUserSpaceMessageLog;
            serverConnection.ConnectTimeoutMs = ServerConnectTimeoutMs;
            serverConnection.UserSpaceMessageHandleFunc += (channelId, clientConnId, payload) =>
            {
                OnServerDataReceived((int)clientConnId, new ArraySegment<byte>(payload), Channels.Reliable);
            };
            /* Moved to ServerView
            serverConnection.AddMessageHandler((uint)MessageType.CreateChannel, (conn, channelId, msg) =>
            {
                var resultMsg = (CreateChannelResultMessage)msg;
                if (resultMsg.OwnerConnId == conn.Id)
                {
                    // Owned the target channel
                    ServerOwnedChannelId = channelId;
                    Log.Info($"Server owned channel: {ServerOwnedChannelId}");
                }
            });
            serverConnection.AddMessageHandler((uint)MessageType.RemoveChannel, (conn, channelId, msg) =>
            {
                var removeMsg = (RemoveChannelMessage)msg;
                Log.Info("Channel removed: " + channelId);
                if (ServerOwnedChannelId == removeMsg.ChannelId)
                {
                    Log.Info("Server no longer owns the channel: " + ServerOwnedChannelId);
                    ServerOwnedChannelId = null;
                }
            });
            serverConnection.AddMessageHandler((uint)MessageType.SubToChannel, (conn, channelId, msg) =>
            {
                var resultMsg = (SubscribedToChannelResultMessage)msg;
                if (resultMsg.ConnId != conn.Id)
                {
                    Log.Info($"Server received sub of other conn({resultMsg.ConnId}), connType={resultMsg.ConnType}, channelType={resultMsg.ChannelType}, channelId={channelId}");
                    if (resultMsg.ConnType == ConnectionType.Client)
                    {
                        // A client subscribed to the target channel
                        if (resultMsg.ChannelType == ServerChannelType)
                        {
                            if (!NetworkServer.connections.ContainsKey((int)resultMsg.ConnId))
                                this.OnServerConnected.Invoke((int)resultMsg.ConnId);
                        }
                    }
                }
            });
            serverConnection.AddMessageHandler((uint)MessageType.UnsubFromChannel, (conn, channelId, msg) =>
            {
                var resultMsg = (UnsubscribedFromChannelResultMessage)msg;
                if (resultMsg.ConnId == conn.Id)
                {
                    Log.Info("Server unsubbed from channel: " + channelId);
                    if (ServerOwnedChannelId == channelId)
                    {
                        Log.Info("Server no longer owns the channel: " + ServerOwnedChannelId);
                        ServerOwnedChannelId = null;
                    }
                }
                else
                {
                    Log.Info($"Server received unsub of other conn({resultMsg.ConnId}), connType={resultMsg.ConnType}, channelType={resultMsg.ChannelType}, channelId={channelId}");
                    if (resultMsg.ConnType == ConnectionType.Client)
                    {
                        // A client unsubscribed from the target channel
                        this.OnServerDisconnected.Invoke((int)resultMsg.ConnId);
                    }
                }
            });
            */

            NetworkServer.RegisterHandler<AddPlayerProxyMessage>(HandleAddPlayerProxyMessage);

            serverConnection.Connect(ServerAddressToChanneld, ServerPortToChanneld, () =>
            {
                Log.Info("Server connected to channeld.");

                serverConnection.Auth("test", "test", (msg) =>
                {
                    if (serverConnection.Id == msg.ConnId)
                    {
                        if (msg.Result == AuthResultMessage.Types.AuthResult.Successful)
                        {
                            Log.Info($"Server authenticated, connId: {msg.ConnId}");
                            /*
                            // The master server creates(owns) the Global channel after sign in.
                            if (ServerChannelType == ChannelType.Global)
                            {
                                serverConnection.CreateChannel(ServerChannelType, ServerChannelMetadata, new ChannelSubscriptionOptions
                                {
                                    CanUpdateData = true,
                                    FanOutIntervalMs = ServerFanoutIntervalMs
                                }, callback: (_) => OnServerAuthenticated?.Invoke(serverConnection));
                            }
                            // Other servers sub to the Global channel before creating their own channels.
                            else
                            {
                                serverConnection.SubToChannel(ChanneldConnection.GlobalChannelId, callback: (_) =>
                                {
                                    OnServerAuthenticated?.Invoke(serverConnection);
                                });
                            }
                            */
                            OnAuthenticated?.Invoke(serverConnection);
                        }
                        else
                        {
                            this.OnServerError((int)msg.ConnId, new Exception("Authentication failed:" + msg.Result.ToString()));
                        }
                    }
                    else
                    {
                        OnGlobalOwnerReceivedAuthResult?.Invoke(msg);
                    }
                });
            }, () =>
            {
                Log.Error("Server failed to connect to channeld.");
                NetworkServer.Shutdown();
            });
        }

        private void HandleAddPlayerProxyMessage(NetworkConnection conn, AddPlayerProxyMessage msg)
        {
            var networkManager = NetworkManager.singleton;
            GameObject player = Instantiate(networkManager.playerPrefab, msg.position, msg.rotation);
            player.transform.localScale = msg.scale;

            // instantiating a "Player" prefab gives it the name "Player(clone)"
            // => appending the connectionId is WAY more useful for debugging!
            player.name = $"{networkManager.playerPrefab.name} [connId={conn.connectionId}]";
            NetworkIdentity identity = player.GetComponent<NetworkIdentity>();
            NetworkServer.AddPlayerForConnection(conn, player);
            // Update the Proxy Player's netId with the Authority Player's netId
            NetworkServer.spawned.Remove(identity.netId);
            identity.SetNetId(msg.netId);
            NetworkServer.spawned[msg.netId] = identity;
        }

        // Which channel will the server sends message to?
        public static Func<int, uint> GetServerSendChannelId = (connectionId) => ServerSendChannelId ?? ChanneldConnection.GlobalChannelId;

        public override void ServerSend(int connectionId, ArraySegment<byte> segment, int reliable)
        {
            var msgType = MirrorUtils.GetChanneldMsgType(segment);
            // Send the packet to channeld and forward to the client connection.
            serverConnection?.Send(GetServerSendChannelId(connectionId), msgType, new ServerForwardMessage()
            {
                ClientConnId = (uint)connectionId,
                Payload = ByteString.CopyFrom(segment.Array, segment.Offset, segment.Count)
            } , BroadcastType.SingleConnection);
        }

        public override void ServerEarlyUpdate()
        {
            if (enabled) serverConnection?.TickIncoming();
        }

        public override void ServerLateUpdate()
        {
            serverConnection?.TickOutgoing();
        }

        public override Uri ServerUri()
        {
            if (serverConnection == null)
                return null;
            UriBuilder builder = new UriBuilder();
            // TODO: support more scheme (KCP, WebSocket)
            builder.Scheme = "tcp";
            builder.Host = serverConnection.RemoteAddress;
            builder.Port = serverConnection.RemotePort;
            return builder.Uri;
        }

        public override bool ServerActive()
        {
            return serverConnection == null ? false : serverConnection.Connected;
        }

        public override void ServerDisconnect(int connectionId)
        {
            serverConnection?.Send(ChanneldConnection.GlobalChannelId, (uint)MessageType.Disconnect, new DisconnectMessage()
            {
                ConnId = (uint)connectionId
            });
        }

        // Getting the address of the client connection in channeld is not supported.
        public override string ServerGetClientAddress(int connectionId) => null;

        public override void ServerStop()
        {
            serverConnection.UserSpaceMessageHandleFunc = null;
            serverConnection?.Disconnect();
            ServerSendChannelId = null;
        }

        #endregion

        #region Client Logic

        //private NetworkReader spawnMessageReader = new NetworkReader(new byte[0]);

        private void InitClientConnection()
        {
            if (clientConnection != null)
                return;

            clientConnection = new ChanneldConnection(ConnectionType.Client);
            clientConnection.ShowUserSpaceMessageLog = showUserSpaceMessageLog;
            clientConnection.ConnectTimeoutMs = ClientConnectTimeoutMs;
            clientConnection.UserSpaceMessageHandleFunc += (channelId, clientConnId, payload) =>
            {
                /* Moved to ChannelDataView
                 * 
                // The payload may contains multiple Mirror messages, making it hard to recognize the SpawnMessage inside.
                // We have to use this awkward way to make sure when handling SpawnMessage, the NetworkClient has the right channelId context.
                // FIXME: reduce memory allocation?
                Action<SpawnMessage> onSpawn = (msg) =>
                {
                    netIdOwningChannels[msg.netId] = channelId;
                    Log.Info($"Set up mapping of netId: {msg.netId} -> channelId: {channelId}");
                    NetworkClient.OnSpawn(msg);
                };
                NetworkClient.ReplaceHandler<SpawnMessage>(onSpawn, false);
                */

                var data = new ArraySegment<byte>(payload);
                OnClientDataReceived(data, Channels.Reliable);
            };
            /* Moved to ClientView
            clientConnection.AddMessageHandler((uint)MessageType.SubToChannel, (client, channelId, msg) =>
            {
                var resultMsg = msg as SubscribedToChannelResultMessage;
                if (resultMsg.ConnId == client.Id && resultMsg.ChannelType == ServerChannelType)
                {
                    OnClientSubToChannel(channelId);
                }
            });
            */
            clientConnection.AddMessageHandler((uint)MessageType.UnsubFromChannel, (conn, channelId, msg) =>
            {
                var resultMsg = (UnsubscribedFromChannelResultMessage)msg;
                if (resultMsg.ConnId == conn.Id && channelId == ClientSendChannelId)
                {
                    ClientSendChannelId = null;
                }
            });
        }

        public void OnClientSubToChannel(uint channelId)
        {
            if (ClientSendChannelId == null)
            { 
                ClientSendChannelId = channelId;

                this.OnClientConnected?.Invoke();

                if (NetworkManager.singleton.authenticator != null)
                {
                    // Fake an AuthRespondMessage and pass it to NetworkClient.OnTransportData()
                    OnClientMessageReceived(new Mirror.Authenticators.BasicAuthenticator.AuthResponseMessage()
                    {
                        code = 100,
                        message = "Success"
                    });
                }
            }
            else if (ClientSendChannelId != channelId)
            {
                ClientSendChannelId = channelId;

                // The client has already subscribed to a channel. We don't need to trigger NetworkManager.OnClientConnect() again.
                // Instead, send the AddPlayer message to the server that owns the new channel.
                // FIXME: if both channels are spatial channels, the new owner could be the same server!
                //NetworkClient.connection.Send(new AddPlayerMessage());
            }
        }

        private void OnClientConnectedChanneld()
        {
            Log.Info("Client connected to channeld.");

            if (NetworkManager.singleton.authenticator == null)
            {
                // channeld always requires authentication
                clientConnection.Auth("test", "test", (msg) =>
                {
                    if (msg.Result == AuthResultMessage.Types.AuthResult.Successful)
                    {
                        Log.Info($"Client authenticated, connId: {msg.ConnId}");
                        //this.OnClientConnected?.Invoke();

                        OnAuthenticated?.Invoke(clientConnection);
                    }
                    else
                    {
                        this.OnClientError(new Exception("Authentication failed:" + msg.Result.ToString()));
                    }
                });
            }
            else
            {
                this.OnClientConnected?.Invoke();
            }
        }

        private void OnClientConnectTimeout()
        {
            Log.Error("Client failed to connect to channeld.");
            NetworkClient.Shutdown();
        }

        public override void ClientConnect(string address)
        {
            InitClientConnection();
            clientConnection.Connect(address, ClientPortToChanneld, OnClientConnectedChanneld, OnClientConnectTimeout);
        }

        public override void ClientConnect(Uri uri)
        {
            InitClientConnection();
            clientConnection.Connect(uri.Host, uri.Port, OnClientConnectedChanneld, OnClientConnectTimeout);
        }

        public override void ClientSend(ArraySegment<byte> segment, int channelId)
        {
            clientConnection?.SendRaw(ClientSendChannelId ?? ChanneldConnection.GlobalChannelId, 
                MirrorUtils.GetChanneldMsgType(segment),
                ByteString.CopyFrom(segment.Array, segment.Offset, segment.Count));
        }

        public void OnClientMessageReceived<T>(T mirrorMsg, int reliable = Channels.Reliable) where T : struct, NetworkMessage
        {
            using (PooledNetworkWriter writer = NetworkWriterPool.GetWriter())
            {
                MessagePacking.Pack(mirrorMsg, writer);

                OnClientDataReceived(writer.ToArraySegment(), reliable);
            }
        }

        // Client sends the NetworkMessage directly to channeld (without batching in Mirror's NetworkConnection)
        public void ClientSendNetworkMessage<T>(uint channelId, T message) where T : struct, NetworkMessage
        {
            using (PooledNetworkWriter packetWriter = NetworkWriterPool.GetWriter())
            {
                // A packet consists of a timestamp and a series of NetworkMessage.
                packetWriter.WriteDouble(NetworkTime.localTime);
                MessagePacking.Pack(message, packetWriter);
                var segment = packetWriter.ToArraySegment();
                ChanneldTransport.Current.clientConnection.SendRaw(channelId,
                    MirrorUtils.GetChanneldMsgType(segment),
                    ByteString.CopyFrom(segment.Array, segment.Offset, segment.Count));
            }
        }

        public override void ClientEarlyUpdate()
        {
            clientConnection?.TickIncoming();
        }

        public override void ClientLateUpdate()
        {
            clientConnection?.TickOutgoing();
        }

        public override bool ClientConnected()
        {
            return clientConnection == null ? false : clientConnection.Connected;
        }

        public override void ClientDisconnect()
        {
            clientConnection?.Disconnect();
            ClientSendChannelId = null;
        }

        #endregion

        public override bool Available()
        {
            return true;
        }

        public override int GetMaxPacketSize(int channelId = 0)
        {
            // FIXME: Protect against allocation attacks by keeping the max message size small
            return 0xffffff;
        }

        public override void Shutdown()
        {
            clientConnection?.Disconnect();
            //clientConnection = null;
            serverConnection?.Disconnect();
            //serverConnection = null;
        }

        private void OnDestroy()
        {
            Shutdown();
        }
    }
}