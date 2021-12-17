using Channeld;
using Google.Protobuf;
using Mirror;
using System;
using UnityEngine;

public class ChanneldTransport : Transport
{
    public string ServerAddressToChanneld = "127.0.0.1";
    public int ServerPortToChanneld = 11288;
    public ChannelType ServerChannelType = ChannelType.Global;
    public string ServerChannelMetadata = "MirrorServer";
    public uint ServerFanoutIntervalMs = 10;

    // The client connects to the address Mirror passes to it, generally NetworkManager.networkAddress
    //public string ClientAddressToChanneld = "127.0.0.1";
    public int ClientPortToChanneld = 12108;
    public uint ClientFanoutIntervalMs = 50;

    private ChanneldClient serverConnection;
    private ChanneldClient clientConnection;

    private uint? TargetChannelId { get; set; } = null;

    void Awake()
    {
        Channeld.Log.Info = Debug.Log;
        Channeld.Log.Warning = Debug.LogWarning;
        Channeld.Log.Error = Debug.LogError;

        NetworkTime.PingFrequency = 60f;

        Debug.Log("ChanneldTransport initialized!");
    }

    #region Server Logic

    public override void ServerStart()
    {
        serverConnection = new ChanneldClient();
        serverConnection.UserSpaceMessageHandleFunc = (channelId, sourceConnId, payload) =>
        {
            this.OnServerDataReceived((int)sourceConnId, new ArraySegment<byte>(payload), Channels.Reliable);
        };
        //serverConnection.SetMessageHandlerEntry((uint)MessageType.SubToChannel, SubscribedToChannelMessage.Parser, (client, channelId, msg) =>
        serverConnection.AddMessageHandler((uint)MessageType.SubToChannel, (client, channelId, msg) =>
        {
            var subMsg = msg as SubscribedToChannelMessage;
            if (subMsg.ConnId == client.Id)
            {
                // Owned the target channel
                TargetChannelId = channelId;
                Debug.Log("Server owned channel: " + TargetChannelId);
            }
            else
            {
                // A client subscribed to the target channel
                this.OnServerConnected.Invoke((int)subMsg.ConnId);
                Debug.Log("Server-owned channel has client sub: " + subMsg.ConnId);
            }
        });
        serverConnection.AddMessageHandler((uint)MessageType.UnsubToChannel, (client, channelId, msg) =>
        {
            var subMsg = msg as UnsubscribedToChannelMessage;
            if (subMsg.ConnId == client.Id)
            {
                Debug.Log("Server no longer owns the channel: " + TargetChannelId);
                TargetChannelId = null;
            }
            else
            {
                // A client unsubscribed from the target channel
                this.OnServerDisconnected.Invoke((int)subMsg.ConnId);
                Debug.Log("Server-owned channel has client unsub: " + subMsg.ConnId);
            }
        });

        serverConnection.Connect(ServerAddressToChanneld, ServerPortToChanneld);
        Debug.Log("Server connected.");

        serverConnection.Auth("test", "test", (msg) =>
        {
            if (msg.Result == AuthResultMessage.Types.AuthResult.Successful)
            {
                Debug.Log("Server authenticated.");
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
        // Send the packet to channeld and forward to the client connection.
        serverConnection?.SendRaw((uint)connectionId, MirrorUtils.GetChanneldMsgType(segment), 
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
        serverConnection?.Send(0, (uint)MessageType.Disconnect, new DisconnectMessage()
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

    private void InitClientConnection()
    {
        clientConnection = new ChanneldClient();
        clientConnection.UserSpaceMessageHandleFunc = (channelId, sourceConnId, payload) =>
        {
            this.OnClientDataReceived(new ArraySegment<byte>(payload), Channels.Reliable);
        };
        clientConnection.AddMessageHandler((uint)MessageType.SubToChannel, (client, channelId, m) =>
        {
            var msg = m as SubscribedToChannelMessage;
            if (msg.ConnId == client.Id && TargetChannelId == null)
            {
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
                    Debug.Log("Client authenticated.");
                    //this.OnClientConnected?.Invoke();

                    // Sub to the global channel, and then the server will proceed the client connection logic.
                    clientConnection.SubToChannel(0, new ChannelSubscriptionOptions()
                    {
                        CanUpdateData = true,
                        FanOutIntervalMs = ClientFanoutIntervalMs
                    }, (subMsg) => this.OnClientConnected?.Invoke());
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
        clientConnection?.SendRaw(TargetChannelId ?? 0, MirrorUtils.GetChanneldMsgType(segment),
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
