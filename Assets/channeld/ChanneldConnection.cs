using Channeldpb;
using Google.Protobuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Threading;
using Google.Protobuf.WellKnownTypes;
using UnityEngine;

namespace Channeld
{
    public static class Log
    {
        public static Action<string> Debug = Console.WriteLine;
        public static Action<string> Info = Console.WriteLine;
        public static Action<string> Warning = Console.WriteLine;
        public static Action<string> Error = Console.Error.WriteLine;
    }

    public delegate void MessageHandlerFunc(ChanneldConnection conn, uint channelId, IMessage msg);

    public class ChanneldConnection
    {
        public const uint GlobalChannelId = 0;

        struct MessageHandlerEntry
        {
            public MessageParser parser;
            public MessageHandlerFunc handleFunc;
        }

        struct MessageQueueEntry
        {
            public IMessage msg;
            public uint channelId;
            public uint stubId;
            public MessageHandlerFunc handleFunc;
        }

        struct RpcCallback
        {
            public MessageHandlerFunc handleFunc;
            public TaskCompletionSource<IMessage> tcs;
        }

        public ConnectionType ConnectionType { get; private set; }
        public string RemoteAddress { get; private set; }
        public int RemotePort { get; private set; }
        public int ConnectTimeoutMs { get; set; } = 3000;
        public uint Id { get; private set; } = 0;
        public Channeldpb.CompressionType CompressionType { get; private set; } = Channeldpb.CompressionType.NoCompression;
        public Dictionary<uint, SubscribedToChannelResultMessage> SubscribedChannels { get; private set; } = new Dictionary<uint, SubscribedToChannelResultMessage>();
        public Dictionary<uint, CreateChannelResultMessage> OwnedChannels { get; private set; } = new Dictionary<uint, CreateChannelResultMessage>();
        public Dictionary<uint, ListChannelResultMessage.Types.ChannelInfo> ListedChannels { get; private set; } =
            new Dictionary<uint, ListChannelResultMessage.Types.ChannelInfo>();
        public MessageHandlerFunc DefaultMessageHandleFunc = (client, channelId, msg) => { };
        public Action<uint, uint, byte[]> UserSpaceMessageHandleFunc = (channelId, clientConnId, payload) => { };
        public bool ShowUserSpaceMessageLog { get; set; }
        public bool Connected => tcp.Connected;

        private TcpClient tcp;
        private NetworkStream netStream;
        private Thread receiveThread;
        private BlockingCollection<MessageQueueEntry> incomingQueue = new BlockingCollection<MessageQueueEntry>();
        private BlockingCollection<MessagePack> outgoingQueue = new BlockingCollection<MessagePack>();
        private Dictionary<uint, MessageHandlerEntry> handlers = new Dictionary<uint, MessageHandlerEntry>();
        private MessageHandlerEntry userSpaceMessageHandlerEntry;

        // Key is the StubId.
        private Dictionary<uint, RpcCallback> rpcCallbacks = new Dictionary<uint, RpcCallback>()
        {
            // 0 is reserved.
            {0, default}
        };

        public static ChanneldConnection Instance {get; private set;}

        public ChanneldConnection(ConnectionType connectionType)
        {
            // FIXME: Not thread-safe
            if (Instance == null)
            {
                Instance = this; 
            }
            else
            {
                Log.Error("ChanneldClient can only be created once in a process.");
            }

            ConnectionType = connectionType;
            tcp = new TcpClient();
            receiveThread = new Thread(Receive);
            receiveThread.IsBackground = true;
            userSpaceMessageHandlerEntry = new MessageHandlerEntry()
            {
                parser = ServerForwardMessage.Parser,
                handleFunc = HandleServerForwardMessage
            };

            SetMessageHandlerEntry((uint)MessageType.Auth, AuthResultMessage.Parser, HandleAuth);
            SetMessageHandlerEntry((uint)MessageType.CreateChannel, CreateChannelResultMessage.Parser, HandleCreateChannel);
            SetMessageHandlerEntry((uint)MessageType.RemoveChannel, RemoveChannelMessage.Parser, HandleRemoveChannel);
            SetMessageHandlerEntry((uint)MessageType.ListChannel, ListChannelResultMessage.Parser, HandleListChannel);
            SetMessageHandlerEntry((uint)MessageType.SubToChannel, SubscribedToChannelResultMessage.Parser, HandleSubToChannel);
            SetMessageHandlerEntry((uint)MessageType.UnsubFromChannel, UnsubscribedFromChannelResultMessage.Parser, HandleUnsubToChannel);
            SetMessageHandlerEntry((uint)MessageType.ChannelDataUpdate, ChannelDataUpdateMessage.Parser);
            SetMessageHandlerEntry((uint)MessageType.CreateSpatialChannel, CreateSpatialChannelsResultMessage.Parser, HandleCreateSpatialChannel);
            SetMessageHandlerEntry((uint)MessageType.QuerySpatialChannel, QuerySpatialChannelResultMessage.Parser);
            SetMessageHandlerEntry((uint)MessageType.ChannelDataHandover, ChannelDataHandoverMessage.Parser);
        }

        private void HandleAuth(ChanneldConnection conn, uint channelId, IMessage msg)
        {
            var resultMsg = (AuthResultMessage)msg;
            if (resultMsg.Result == AuthResultMessage.Types.AuthResult.Successful)
            {
                if (Id == 0)
                {
                    Id = resultMsg.ConnId;
                    CompressionType = resultMsg.CompressionType;
                }
            }
        }

        private void HandleCreateChannel(ChanneldConnection conn, uint channelId, IMessage msg)
        {
            var resultMsg = (CreateChannelResultMessage)msg;
            if (resultMsg.OwnerConnId == Id)
            {
                OwnedChannels.Add(channelId, resultMsg);
            }

            ListedChannels[channelId] = new ListChannelResultMessage.Types.ChannelInfo()
            {
                ChannelId = channelId,
                ChannelType = resultMsg.ChannelType,
                Metadata = resultMsg.Metadata
            };
        }

        private void HandleCreateSpatialChannel(ChanneldConnection conn, uint channelId, IMessage msg)
        {
            var resultMsg = (CreateSpatialChannelsResultMessage)msg;
            foreach (var spatialChannelId in resultMsg.SpatialChannelId)
            {
                if (resultMsg.OwnerConnId == Id)
                {
                    OwnedChannels.Add(spatialChannelId, new CreateChannelResultMessage()
                    {
                        ChannelType = ChannelType.Spatial,
                        Metadata = resultMsg.Metadata,
                        OwnerConnId = resultMsg.OwnerConnId,
                    });
                }

                ListedChannels[spatialChannelId] = new ListChannelResultMessage.Types.ChannelInfo()
                {
                    ChannelId = spatialChannelId,
                    ChannelType = ChannelType.Spatial,
                    Metadata = resultMsg.Metadata
                };
            }
        }

        private void HandleRemoveChannel(ChanneldConnection conn, uint channelId, IMessage msg)
        {
            var removeMsg = (RemoveChannelMessage)msg;
            SubscribedChannels.Remove(removeMsg.ChannelId);
            OwnedChannels.Remove(removeMsg.ChannelId);
            ListedChannels.Remove(removeMsg.ChannelId);
        }

        private void HandleListChannel(ChanneldConnection conn, uint channelId, IMessage msg)
        {
            var resultMsg = msg as ListChannelResultMessage;
            foreach (var channelInfo in resultMsg.Channels)
            {
                ListedChannels[channelInfo.ChannelId] = channelInfo;
            }
        }

        private void HandleSubToChannel(ChanneldConnection conn, uint channelId, IMessage msg)
        {
            var subMsg = (SubscribedToChannelResultMessage)msg;
            if (subMsg.ConnId == conn.Id)
            {
                SubscribedChannels.Add(channelId, subMsg);
            }
        }

        private void HandleUnsubToChannel(ChanneldConnection conn, uint channelId, IMessage msg)
        {
            var unsubMsg = (UnsubscribedFromChannelResultMessage)msg;
            if (unsubMsg.ConnId == conn.Id)
            {
                SubscribedChannels.Remove(channelId);
            }
        }

        private void HandleServerForwardMessage(ChanneldConnection conn, uint channelId, IMessage msg)
        {
            var usm = (ServerForwardMessage)msg;
            UserSpaceMessageHandleFunc(channelId, usm.ClientConnId, usm.Payload.ToByteArray());
        }

        // Remarks: handleFunc will always be invoked BEFORE the RPC callback
        public void SetMessageHandlerEntry(uint msgType, MessageParser parser, MessageHandlerFunc handleFunc = null)
        {
            handlers[msgType] = new MessageHandlerEntry()
            {
                parser = parser,
                handleFunc = (client, channelId, msg) =>
                {
                    if (handleFunc == null)
                        DefaultMessageHandleFunc(client, channelId, msg);
                    else
                        handleFunc(client, channelId, msg);
                }
            };
        }

        // Remarks: handleFunc will always be invoked BEFORE the RPC callback
        public void AddMessageHandler(uint msgType, MessageHandlerFunc handleFunc)
        {
            MessageHandlerEntry entry;
            if (!handlers.TryGetValue(msgType, out entry))
            {
                throw new Exception("No parser registered for msgType:" + msgType);
            }
            entry.handleFunc += handleFunc;
            // The entry is a value type, so it should be stored back to the map.
            handlers[msgType] = entry;
        }

        public void RemoveMessageHandler(uint msgType, MessageHandlerFunc handleFunc)
        {
            MessageHandlerEntry entry;
            if (!handlers.TryGetValue(msgType, out entry))
            {
                throw new Exception("No parser registered for msgType:" + msgType);
            }
            entry.handleFunc -= handleFunc;
            // The entry is a value type, so it should be stored back to the map.
            handlers[msgType] = entry;
        }

        public void Connect(string host, int port, Action onConnected = null, Action onTimeout = null)
        {
            RemoteAddress = host;
            RemotePort = port;

#if UNITY_SERVER
            if (tcp.ConnectAsync(host, port).Wait(ConnectTimeoutMs))
            {
                netStream = tcp.GetStream();
                receiveThread.Start();
                onConnected?.Invoke();
            }
            else
            {
                onTimeout?.Invoke();
            }
#else
            // Calling TcpClient.Connect() causes the thread to halt, which is not a good practice in an application with UI.
            Task.Run(async () =>
            {
                var connectTask = tcp.ConnectAsync(host, port);
                if (await Task.WhenAny(connectTask, Task.Delay(ConnectTimeoutMs)) == connectTask)
                {
                    netStream = tcp.GetStream();
                    receiveThread.Start();
                    onConnected?.Invoke();
                }
                else
                {
                    onTimeout?.Invoke();
                }
            });
#endif
        }

        public Task ConnectAsync(string host, int port)
        {
            RemoteAddress = host;
            RemotePort = port;
            var task = tcp.ConnectAsync(host, port);
            task.ContinueWith((t) =>
            {
                netStream = tcp.GetStream();
                receiveThread.Start();
            });
            return task;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="flushAll">If true, send all outgoing messages before closing the socket.</param>
        public void Disconnect(bool flushAll = true)
        {
            if (!Connected)
                return;

            if (flushAll)
            {
                TickOutgoing();
                netStream.Flush();
            }

            receiveThread.Abort();
            netStream.Close();
            tcp.Close();
        }

        private MemoryStream netBuffer = new MemoryStream();

        private void Receive()
        {
            while (netStream.CanRead)
            {
                byte[] buffer = new byte[512];
                do
                {
                    int bytesRead = netStream.Read(buffer, 0, buffer.Length);
                    netBuffer.Write(buffer, 0, bytesRead);
                }
                while (netStream.DataAvailable);

                if (netBuffer.Length <= 5)
                    continue;

                var bytes = netBuffer.ToArray();
                if (bytes[0] != 67)
                {
                    Log.Error("Invalid tag: " + bytes[0]);
                    // Discard the invalid packet
                    netBuffer.SetLength(0);
                    continue;
                }

                int size = bytes[3];
                if (bytes[1] != 72)
                {
                    size = size | bytes[1] << 16 | bytes[2] << 8;
                }
                else if (bytes[2] != 78)
                {
                    size = size | bytes[2] << 8;
                }

                if (bytes.Length < size + 5)
                {
                    Log.Warning($"[channeld] Segment doesn't have a complete packet, size in header: {size}, actual packet size: {bytes.Length - 5}");
                    continue;
                }

                var ros = new System.ReadOnlySpan<byte>(bytes, 5, size);

                if (bytes[4] == (byte)Channeldpb.CompressionType.Snappy)
                {
                    try
                    {
                        ros = IronSnappy.Snappy.Decode(ros);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[channeld] Snappy.Decode: " + ex.ToString());
                        continue;
                    }
                }

                Packet p;
                try
                {
                    p = Packet.Parser.ParseFrom(ros);
                }
                catch (Exception ex)
                {
                    Log.Error("[channeld] Packet.Parse: " + ex.ToString());
                    continue;
                }

                foreach (var mp in p.Messages)
                {
                    MessageHandlerEntry entry;
                    if (!handlers.TryGetValue(mp.MsgType, out entry))
                    {
                        if (mp.MsgType >= (uint)MessageType.UserSpaceStart)
                        {
                            entry = userSpaceMessageHandlerEntry;
                        }
                        else
                        {
                            Log.Error("[channeld] No parser registered for msgType:" + mp.MsgType);
                            continue;
                        }
                    }

                    IMessage msg;
                    try
                    {
                        msg = entry.parser.ParseFrom(mp.MsgBody);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[channeld] Message.Parse: " + ex.ToString());
                        continue;
                    }

                    incomingQueue.Add(new MessageQueueEntry()
                    {
                        msg = msg,
                        channelId = mp.ChannelId,
                        stubId = mp.StubId,
                        handleFunc = entry.handleFunc,
                    });

                    if (mp.MsgType < (uint)MessageType.UserSpaceStart || ShowUserSpaceMessageLog)
                        Log.Debug($"[channeld] Receive message(channelId={mp.ChannelId}, stubId={mp.StubId}, type={mp.MsgType}, bodySize={mp.MsgBody.Length}): {msg}");
                }

                // Reset the buffer after read the packet completely
                netBuffer.SetLength(0);
                int leftSize = bytes.Length - size - 5;
                if (leftSize > 0)
                {
                    // Write in the leftover for future use
                    netBuffer.Write(bytes, size + 5, leftSize);
                    Log.Warning($"[channeld] Remained {leftSize} bytes after reading the packet.");
                }
            }
        }

        private uint AddRpcCallback(MessageHandlerFunc handleFunc, TaskCompletionSource<IMessage> tcs)
        {
            uint stubId = 0;
            while (rpcCallbacks.ContainsKey(stubId))
            {
                stubId++;
            }
            rpcCallbacks[stubId] = new RpcCallback() { handleFunc = handleFunc, tcs = tcs };
            return stubId;
        }

        // Remarks: callback will always be invoked AFTER the message handler function
        public void SendRaw(uint channelId, uint msgType, ByteString msgBody, BroadcastType broadcast = BroadcastType.NoBroadcast, MessageHandlerFunc callback = null, TaskCompletionSource<IMessage> tcs = null)
        {
            uint stubId = callback == null && tcs == null ? 0 : AddRpcCallback(callback, tcs);

            outgoingQueue.Add(new MessagePack()
            {
                ChannelId = channelId,
                Broadcast = broadcast,
                StubId = stubId,
                MsgType = msgType,
                MsgBody = msgBody
            });

            if (msgType >= (uint)MessageType.UserSpaceStart && ShowUserSpaceMessageLog)
                Log.Debug($"[channeld] Send message(channelId={channelId}, stubId={stubId}, type={msgType}, bodySize={msgBody.Length})");
        }

        // Remarks: callback will always be invoked AFTER the message handler function
        public void Send(uint channelId, uint msgType, IMessage msg, BroadcastType broadcast = BroadcastType.NoBroadcast, MessageHandlerFunc callback = null, TaskCompletionSource<IMessage> tcs = null)
        {
            SendRaw(channelId, msgType, msg.ToByteString(), broadcast, callback, tcs);
            if (msgType < (uint)MessageType.UserSpaceStart)
                Log.Debug($"[channeld] Send message(channelId={channelId}, type={msgType}): {msg}");
        }

        public void TickIncoming()
        {
            while (incomingQueue.Count > 0)
            {
                var entry = incomingQueue.Take();
                entry.handleFunc?.Invoke(this, entry.channelId, entry.msg);

                if (entry.stubId > 0)
                {
                    RpcCallback callback;
                    if (rpcCallbacks.TryGetValue(entry.stubId, out callback))
                    {
                        Log.Debug($"Handling RPC callback of {entry.msg.GetType().Name}, stubId: {entry.stubId}");
                        if (callback.handleFunc != null)
                            callback.handleFunc(this, entry.channelId, entry.msg);
                        if (callback.tcs != null)
                            callback.tcs.SetResult(entry.msg);
                        rpcCallbacks.Remove(entry.stubId);
                    }
                }
            }
        }

        public void TickOutgoing()
        {
            if (!Connected)
                return;

            if (outgoingQueue.Count == 0)
                return;

           if (!netStream.CanWrite)
                return;

            var p = new Packet();
            uint size = 0;
            while (outgoingQueue.Count > 0)
            {
                var mp = outgoingQueue.Take();
                if (size + mp.CalculateSize() >= 0xfffff0)
                    break;
                p.Messages.Add(mp);
            }

            var bytes = p.ToByteArray();

            if (CompressionType == Channeldpb.CompressionType.Snappy)
            {
                bytes = IronSnappy.Snappy.Encode(bytes);
            }

            var tag = new byte[] { 67, 72, 78, 76, (byte)CompressionType };
            tag[3] = (byte)(bytes.Length & 0xff);
            if (bytes.Length > 0xff) tag[2] = (byte)((bytes.Length >> 8) & 0xff);
            if (bytes.Length > 0xffff) tag[1] = (byte)((bytes.Length >> 16) & 0xff);
            netStream.Write(tag, 0, 5);
            netStream.Write(bytes, 0, bytes.Length);
        }

        public void Tick()
        {
            TickIncoming();
            TickOutgoing();
        }

        private MessageHandlerFunc WrapMessageHandler<T>(Action<T> callback)
        {
            if (callback == null)
                return null;
            return (client, channelId, msg) => callback?.Invoke((T)msg);
        }

        public void Auth(string pit, string lt, Action<AuthResultMessage> callback = null)
        {
            Send(GlobalChannelId, (uint)MessageType.Auth, new AuthMessage()
            {
                LoginToken = lt,
                PlayerIdentifierToken = pit
            }, BroadcastType.NoBroadcast, WrapMessageHandler(callback));
        }


        //#if NET40_OR_GREATER
        public async Task<AuthResultMessage> AuthAsync(string pit, string lt)
        {
            var tcs = new TaskCompletionSource<IMessage>();
            Send(GlobalChannelId, (uint)MessageType.Auth, new AuthMessage()
            {
                LoginToken = lt,
                PlayerIdentifierToken = pit
            }, tcs: tcs);
            var msg = await tcs.Task;
            return msg as AuthResultMessage;
        }
        //#endif

        public void CreateChannel(ChannelType channelType, string metadata, ChannelSubscriptionOptions subOptions = null, IMessage data = null, ChannelDataMergeOptions mergeOptions = null, Action<CreateChannelResultMessage> callback = null)
        {
            Send(GlobalChannelId, (uint)MessageType.CreateChannel, new CreateChannelMessage()
            {
                ChannelType = channelType,
                Metadata = metadata,
                SubOptions = subOptions,
                Data = data == null ? null : Any.Pack(data),
                MergeOptions = mergeOptions,
            }, BroadcastType.NoBroadcast, WrapMessageHandler(callback));
        }

        public void CreateSpatialChannel(string metadata, ChannelSubscriptionOptions subOptions = null, IMessage data = null, ChannelDataMergeOptions mergeOptions = null, Action<CreateSpatialChannelsResultMessage> callback = null)
        {
            Send(GlobalChannelId, (uint)MessageType.CreateChannel, new CreateChannelMessage()
            {
                ChannelType = ChannelType.Spatial,
                Metadata = metadata,
                SubOptions = subOptions,
                Data = data == null ? null : Any.Pack(data),
                MergeOptions = mergeOptions,
            }, BroadcastType.NoBroadcast, WrapMessageHandler(callback));
        }

        public void RemoveChannel(uint channelId, Action<RemoveChannelMessage> callback = null)
        {
            Send(GlobalChannelId, (uint)MessageType.RemoveChannel, new RemoveChannelMessage()
            {
                ChannelId = channelId
            }, BroadcastType.NoBroadcast, WrapMessageHandler(callback));
        }

        // Using ChannelType.Unknown will try to match all the channel types.
        public void ListChannel(ChannelType typeFilter = ChannelType.Unknown, string[] metadataFilters = null, Action<ListChannelResultMessage> callback = null)
        {
            var listMsg = new ListChannelMessage()
            {
                TypeFilter = typeFilter,
            };
            if (metadataFilters != null)
                listMsg.MetadataFilters.Add(metadataFilters);
            Send(GlobalChannelId, (uint)MessageType.ListChannel, listMsg, BroadcastType.NoBroadcast, WrapMessageHandler(callback));
        }

        public void SubToChannel(uint channelId, ChannelSubscriptionOptions options = null, Action<SubscribedToChannelResultMessage> callback = null)
        {
            SubConnectionToChannel(Id, channelId, options, callback);
        }

        // Subscribe another connection to a channel. Only the owner of the GLOBAL channel or the target channel has the authority to do so.
        public void SubConnectionToChannel(uint connId, uint channelId, ChannelSubscriptionOptions options = null, Action<SubscribedToChannelResultMessage> callback = null)
        {
            Send(channelId, (uint)MessageType.SubToChannel, new SubscribedToChannelMessage()
            {
                ConnId = connId,
                SubOptions = options
            }, BroadcastType.NoBroadcast, WrapMessageHandler(callback));
        }

        public void UnsubFromChannel(uint channelId, Action<UnsubscribedFromChannelMessage> callback = null)
        {
            Send(channelId, (uint)MessageType.UnsubFromChannel, new UnsubscribedFromChannelMessage()
            {
                ConnId = Id
            }, BroadcastType.NoBroadcast, WrapMessageHandler(callback));
        }

        public void QuerySpatialChannel(Vector3[] positions, Action<QuerySpatialChannelResultMessage> callback = null)
        {
            var msg = new QuerySpatialChannelMessage();
            msg.SpatialInfo.AddRange(Array.ConvertAll(positions, pos => new SpatialInfo()
            {
                X = pos.x, Y = pos.y, Z = pos.z,
            }));
            Send(GlobalChannelId, (uint)MessageType.QuerySpatialChannel, msg,
                BroadcastType.NoBroadcast, WrapMessageHandler(callback));
        }
    }
}
