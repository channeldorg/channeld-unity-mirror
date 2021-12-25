using Channeld;
using Google.Protobuf;
using Mirror;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Channeld
{
    public class ChanneldTransport : Transport
    {
        public enum LogLevel { Debug, Info, Warning, Error }

        [Header("Logging")]
        public LogLevel logLevel = LogLevel.Info;
        public bool showUserSpaceMessageLog = false;

        [Header("Server")]
        public string ServerAddressToChanneld = "127.0.0.1";
        public int ServerPortToChanneld = 11288;
        public ChannelType ServerChannelType = ChannelType.Global;
        public string ServerChannelMetadata = "MirrorServer";
        public uint ServerFanoutIntervalMs = 10;

        [Header("Client")]
        // The client connects to the address Mirror passes to it, generally NetworkManager.networkAddress
        //public string ClientAddressToChanneld = "127.0.0.1";
        public int ClientPortToChanneld = 12108;
        public uint ClientFanoutIntervalMs = 50;

        private ChanneldClient serverConnection;
        private ChanneldClient clientConnection;

        // The first channel the server/client subs to.
        public uint? TargetChannelId { get; set; } = null;

        // The spawned object's netId mapping to the id of the channel that owns the object.
        private static Dictionary<uint, uint> netIdOwningChannels = new Dictionary<uint, uint>();

        public static uint GetOwningChannel(uint netId)
        {
            var transport = Transport.activeTransport as ChanneldTransport;
            uint channelId = transport?.TargetChannelId ?? 0;
            netIdOwningChannels.TryGetValue(netId, out channelId);
            return channelId;
        }

        void Awake()
        {
            Log.Debug = (t) => { if (logLevel <= LogLevel.Debug) Debug.Log(t); };
            Log.Info = (t) => { if (logLevel <= LogLevel.Info) Debug.Log(t); };
            Log.Warning = (t) => { if (logLevel <= LogLevel.Warning) Debug.LogWarning(t); };
            Log.Error = (t) => { if (logLevel <= LogLevel.Error) Debug.LogError(t); };

            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                switch (args[i])
                {
                    case "-sa":
                        ServerAddressToChanneld = args[i + 1];
                        Log.Info($"Read ServerAddressToChanneld from command line: {ServerAddressToChanneld}");
                        continue;
                    case "-sp":
                        if (int.TryParse(args[i + 1], out ServerPortToChanneld))
                        {
                            Log.Info($"Read ServerPortToChanneld from command line: {ServerPortToChanneld}");
                            continue;
                        }
                        break;
                    case "-sc":
                        if (Enum.TryParse<ChannelType>(args[i + 1], out ServerChannelType))
                        {
                            Log.Info($"Read ServerChannelType from command line: {ServerChannelType}");
                            continue;
                        }
                        break;
                    case "-cp":
                        if (int.TryParse(args[i + 1], out ClientPortToChanneld))
                        {
                            Log.Info($"Read ClientPortToChanneld from command line: {ClientPortToChanneld}");
                            continue;
                        }
                        break;
                    default:
                        continue;
                }
                Log.Warning($"Invalid value of command line arg '{args[i]}': {args[i + 1]}");
            }

            Log.Debug("ChanneldTransport initialized!");
        }

        #region Server Logic

        public override void ServerStart()
        {
            serverConnection = new ChanneldClient();
            serverConnection.ShowUserSpaceMessageLog = showUserSpaceMessageLog;
            serverConnection.UserSpaceMessageHandleFunc = (channelId, sourceConnId, payload) =>
            {
                this.OnServerDataReceived((int)sourceConnId, new ArraySegment<byte>(payload), Channels.Reliable);
            };
            serverConnection.AddMessageHandler((uint)MessageType.CreateChannel, (client, channelId, msg) =>
            {
                var resultMsg = msg as CreateChannelResultMessage;
                if (resultMsg.OwnerConnId == client.Id)
                {
                    // Owned the target channel
                    TargetChannelId = channelId;
                    Log.Info($"Server owned channel: {TargetChannelId}");
                }
            });
            serverConnection.AddMessageHandler((uint)MessageType.SubToChannel, (client, channelId, msg) =>
            {
                var resultMsg = msg as SubscribedToChannelResultMessage;
                if (resultMsg.ConnId != client.Id)
                {
                    // A client subscribed to the target channel
                    Log.Info($"Server-owned channel({resultMsg.ChannelType} {channelId}) has client sub: {resultMsg.ConnId}");
                    if (resultMsg.ChannelType == ServerChannelType)
                    {
                        this.OnServerConnected.Invoke((int)resultMsg.ConnId);
                    }
                }
            });
            serverConnection.AddMessageHandler((uint)MessageType.UnsubFromChannel, (client, channelId, msg) =>
            {
                var subMsg = msg as UnsubscribedFromChannelMessage;
                if (subMsg.ConnId == client.Id)
                {
                    Log.Info("Server no longer owns the channel: " + TargetChannelId);
                    TargetChannelId = null;
                }
                else
                {
                // A client unsubscribed from the target channel
                this.OnServerDisconnected.Invoke((int)subMsg.ConnId);
                    Log.Info("Server-owned channel has client unsub: " + subMsg.ConnId);
                }
            });

            serverConnection.Connect(ServerAddressToChanneld, ServerPortToChanneld);
            Log.Info("Server connected.");

            serverConnection.Auth("test", "test", (msg) =>
            {
                if (msg.Result == AuthResultMessage.Types.AuthResult.Successful)
                {
                    Log.Info("Server authenticated.");
                    serverConnection.CreateChannel(ServerChannelType, ServerChannelMetadata, new ChannelSubscriptionOptions
                    {
                        CanUpdateData = true,
                        FanOutIntervalMs = ServerFanoutIntervalMs
                    });
                }
                else
                {
                    this.OnServerError((int)msg.ConnId, new Exception("Authentication failed:" + msg.Result.ToString()));
                }
            });
        }

        public override void ServerSend(int connectionId, ArraySegment<byte> segment, int channelId)
        {
            var msgType = MirrorUtils.GetChanneldMsgType(segment);
            // Send the packet to channeld and forward to the client connection.
            serverConnection?.SendRaw((uint)connectionId, msgType,
                ByteString.CopyFrom(segment.Array, segment.Offset, segment.Count), BroadcastType.SingleConnection);
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
            serverConnection?.Send(ChanneldClient.GlobalChannelId, (uint)MessageType.Disconnect, new DisconnectMessage()
            {
                ConnId = (uint)connectionId
            });
        }

        // Getting the address of the client connection in channeld is not supported.
        public override string ServerGetClientAddress(int connectionId) => null;

        public override void ServerStop()
        {
            serverConnection?.Disconnect();
        }

        #endregion

        #region Client Logic

        private NetworkReader spawnMessageReader = new NetworkReader(new byte[0]);

        private void InitClientConnection()
        {
            clientConnection = new ChanneldClient();
            clientConnection.ShowUserSpaceMessageLog = showUserSpaceMessageLog;
            clientConnection.UserSpaceMessageHandleFunc = (channelId, sourceConnId, payload) =>
            {
                var data = new ArraySegment<byte>(payload);
                spawnMessageReader.SetBuffer(data);
                // We can only set up the netId-channelId mapping by capturing the SpawnMessage from server.
                if (MirrorUtils.IsMessage<SpawnMessage>(data, spawnMessageReader))
                {
                    var msg = spawnMessageReader.Read<SpawnMessage>();
                    // By this far, the object has not yet spawned in the client, so we could only store the netId.
                    netIdOwningChannels[msg.netId] = channelId;
                    Log.Info($"Set up mapping of netId:{msg.netId} -> channelId:{channelId}");
                }
                this.OnClientDataReceived(data, Channels.Reliable);
            };
            clientConnection.AddMessageHandler((uint)MessageType.SubToChannel, (client, channelId, msg) =>
            {
                var resultMsg = msg as SubscribedToChannelResultMessage;
                if (resultMsg.ConnId == client.Id && resultMsg.ChannelType == ServerChannelType)
                {
                    this.OnClientConnected?.Invoke();

                    TargetChannelId = channelId;

                    if (NetworkManager.singleton.authenticator != null)
                    {
                        // Fake an AuthRespondMessage and pass it to NetworkClient.OnTransportData()
                        using (PooledNetworkWriter writer = NetworkWriterPool.GetWriter())
                        {
                            var arm = new Mirror.Authenticators.BasicAuthenticator.AuthResponseMessage()
                            {
                                code = 100,
                                message = "Success"
                            };

                            MessagePacking.Pack(arm, writer);

                            this.OnClientDataReceived(writer.ToArraySegment(), Channels.Reliable);
                        }
                    }
                }
            });
        }

        private void OnClientConnectedChanneld()
        {
            if (NetworkManager.singleton.authenticator == null)
            {
                // channeld always requires authentication
                clientConnection.Auth("test", "test", (msg) =>
                {
                    if (msg.Result == AuthResultMessage.Types.AuthResult.Successful)
                    {
                        Log.Info("Client authenticated.");
                        //this.OnClientConnected?.Invoke();

                        // Sub to the global channel, and then the server will proceed the client connection logic.
                        clientConnection.SubToChannel(ChanneldClient.GlobalChannelId, new ChannelSubscriptionOptions()
                        {
                            CanUpdateData = true,
                            FanOutIntervalMs = ClientFanoutIntervalMs
                        });
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

        public override void ClientConnect(string address)
        {
            InitClientConnection();
            clientConnection.Connect(address, ClientPortToChanneld, OnClientConnectedChanneld);
        }

        public override void ClientConnect(Uri uri)
        {
            InitClientConnection();
            clientConnection.Connect(uri.Host, uri.Port, OnClientConnectedChanneld);
        }

        public override void ClientSend(ArraySegment<byte> segment, int channelId)
        {
            clientConnection?.SendRaw(TargetChannelId ?? ChanneldClient.GlobalChannelId, 
                MirrorUtils.GetChanneldMsgType(segment),
                ByteString.CopyFrom(segment.Array, segment.Offset, segment.Count));
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
            clientConnection = null;
            serverConnection?.Disconnect();
            serverConnection = null;
        }
    }
}