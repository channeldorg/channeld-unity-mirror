using Google.Protobuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Threading;
using Google.Protobuf.WellKnownTypes;

namespace Channeld
{
    public static class Log
    {
        public static Action<string> Debug = Console.WriteLine;
        public static Action<string> Info = Console.WriteLine;
        public static Action<string> Warning = Console.WriteLine;
        public static Action<string> Error = Console.Error.WriteLine;
    }

    public delegate void MessageHandlerFunc(ChanneldClient client, uint channelId, IMessage msg);

    public class ChanneldClient
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

        public string RemoteAddress { get; private set; }
        public int RemotePort { get; private set; }
        public uint Id { get; private set; } = 0;
        public HashSet<uint> SubscribedChannels { get; private set; } = new HashSet<uint>();
        public HashSet<uint> OwnedChannels { get; private set; } = new HashSet<uint>();
        public Dictionary<uint, ListChannelResultMessage.Types.ChannelInfo> ListedChannels { get; private set; } =
            new Dictionary<uint, ListChannelResultMessage.Types.ChannelInfo>();
        public MessageHandlerFunc DefaultMessageHandleFunc = (client, channelId, msg) => { };
        public Action<uint, uint, byte[]> UserSpaceMessageHandleFunc = (channelId, sourceConnId, payload) => { };
        public bool ShowUserSpaceMessageLog { get; set; }
        public bool Connected => tcp.Connected;
        public static Action<ChanneldClient> OnAuthenticated;

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

        public static ChanneldClient Instance {get; private set;}

        public ChanneldClient()
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

            tcp = new TcpClient();
            receiveThread = new Thread(Receive);
            receiveThread.IsBackground = true;
            userSpaceMessageHandlerEntry = new MessageHandlerEntry()
            {
                parser = UserSpaceMessage.Parser,
                handleFunc = HandleUserSpaceMessage
            };

            SetMessageHandlerEntry((uint)MessageType.Auth, AuthResultMessage.Parser, HandleAuth);
            SetMessageHandlerEntry((uint)MessageType.CreateChannel, CreateChannelResultMessage.Parser, HandleCreateChannel);
            SetMessageHandlerEntry((uint)MessageType.RemoveChannel, RemoveChannelMessage.Parser, HandleRemoveChannel);
            SetMessageHandlerEntry((uint)MessageType.ListChannel, ListChannelResultMessage.Parser, HandleListChannel);
            SetMessageHandlerEntry((uint)MessageType.SubToChannel, SubscribedToChannelResultMessage.Parser, HandleSubToChannel);
            SetMessageHandlerEntry((uint)MessageType.UnsubFromChannel, UnsubscribedFromChannelMessage.Parser, HandleUnsubToChannel);
            SetMessageHandlerEntry((uint)MessageType.ChannelDataUpdate, ChannelDataUpdateMessage.Parser);
        }

        private void HandleAuth(ChanneldClient client, uint channelId, IMessage msg)
        {
            var resultMsg = (AuthResultMessage)msg;
            if (resultMsg.Result == AuthResultMessage.Types.AuthResult.Successful)
            {
                Id = resultMsg.ConnId;
                OnAuthenticated?.Invoke(this);
            }
        }

        private void HandleCreateChannel(ChanneldClient client, uint channelId, IMessage msg)
        {
            var resultMsg = (CreateChannelResultMessage)msg;
            if (resultMsg.OwnerConnId == Id)
            {
                OwnedChannels.Add(channelId);
            }
        }

        private void HandleRemoveChannel(ChanneldClient client, uint channelId, IMessage msg)
        {
            var removeMsg = (RemoveChannelMessage)msg;
            SubscribedChannels.Remove(removeMsg.ChannelId);
            OwnedChannels.Remove(removeMsg.ChannelId);
            ListedChannels.Remove(removeMsg.ChannelId);
        }

        private void HandleListChannel(ChanneldClient client, uint channelId, IMessage msg)
        {
            var resultMsg = msg as ListChannelResultMessage;
            foreach (var channelInfo in resultMsg.Channels)
            {
                ListedChannels[channelInfo.ChannelId] = channelInfo;
            }
        }

        private void HandleSubToChannel(ChanneldClient client, uint channelId, IMessage msg)
        {
            var subMsg = (SubscribedToChannelResultMessage)msg;
            if (subMsg.ConnId == client.Id)
            {
                SubscribedChannels.Add(channelId);
            }
        }

        private void HandleUnsubToChannel(ChanneldClient client, uint channelId, IMessage msg)
        {
            var unsubMsg = (UnsubscribedFromChannelMessage)msg;
            if (unsubMsg.ConnId == client.Id)
            {
                SubscribedChannels.Add(channelId);
            }
        }

        private void HandleUserSpaceMessage(ChanneldClient client, uint channelId, IMessage msg)
        {
            var usm = (UserSpaceMessage)msg;
            UserSpaceMessageHandleFunc(channelId, usm.SourceConnId, usm.Payload.ToByteArray());
        }

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

        public void Connect(string host, int port, Action onConnected = null)
        {
            RemoteAddress = host;
            RemotePort = port;
            // Calling TcpClient.Connect() causes the thread to halt, which is not a good practice in an application with UI.
            Task.Run(() =>
            {
                tcp.Connect(host, port);
                netStream = tcp.GetStream();
                receiveThread.Start();
                onConnected?.Invoke();
            });
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

        public void Disconnect()
        {
            if (!Connected)
                return;

            receiveThread.Abort();
            netStream.Close();
            tcp.Close();
        }

        private void Receive()
        {
            while (netStream.CanRead)
            {
                byte[] buffer = new byte[512];
                using (MemoryStream ms = new MemoryStream())
                {
                    do
                    {
                        int bytesRead = netStream.Read(buffer, 0, buffer.Length);
                        ms.Write(buffer, 0, bytesRead);
                    }
                    while (netStream.DataAvailable);

                    if (ms.Length <= 4)
                        continue;

                    var bytes = ms.ToArray();
                    if (bytes[0] != 67)
                        throw new IOException("invalid tag");

                    int size = bytes[3];
                    if (bytes[1] != 72)
                    {
                        size = size | bytes[1] << 16 | bytes[2] << 8;
                    }
                    else if (bytes[2] != 78)
                    {
                        size = size | bytes[2] << 8;
                    }

                    if (bytes.Length < size + 4)
                        throw new IOException("segment doesn't have a complete packet");

                    var p = Packet.Parser.ParseFrom(bytes, 4, size);

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
                                throw new Exception("No parser registered for msgType:" + mp.MsgType);
                            }
                        }

                        var msg = entry.parser.ParseFrom(mp.MsgBody);
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

        public void SendRaw(uint channelId, uint msgType, ByteString msgBody, BroadcastType broadcast = BroadcastType.No, MessageHandlerFunc callback = null, TaskCompletionSource<IMessage> tcs = null)
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

            if (msgType >= (uint)MessageType.UserSpaceStart && !ShowUserSpaceMessageLog)
                return;
            Log.Debug($"[channeld] Send message(channelId={channelId}, stubId={stubId}, type={msgType}, bodySize={msgBody.Length})");
        }

        public void Send(uint channelId, uint msgType, IMessage msg, BroadcastType broadcast = BroadcastType.No, MessageHandlerFunc callback = null, TaskCompletionSource<IMessage> tcs = null)
        {
            SendRaw(channelId, msgType, msg.ToByteString(), broadcast, callback, tcs);
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
            var tag = new byte[] { 67, 72, 78, 76 };
            tag[3] = (byte)(bytes.Length & 0xff);
            if (bytes.Length > 0xff) tag[2] = (byte)((bytes.Length >> 8) & 0xff);
            if (bytes.Length > 0xffff) tag[1] = (byte)((bytes.Length >> 16) & 0xff);
            netStream.Write(tag, 0, 4);
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
            }, BroadcastType.No, WrapMessageHandler(callback));
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

        public void CreateChannel(ChannelType channelType, string metadata, ChannelSubscriptionOptions subOptions = null, IMessage data = null, Action<CreateChannelResultMessage> callback = null)
        {
            Send(GlobalChannelId, (uint)MessageType.CreateChannel, new CreateChannelMessage()
            {
                ChannelType = channelType,
                Metadata = metadata,
                SubOptions = subOptions,
                Data = data == null ? null : Any.Pack(data),
            }, BroadcastType.No, WrapMessageHandler(callback));
        }

        public void RemoveChannel(uint channelId, Action<RemoveChannelMessage> callback = null)
        {
            Send(GlobalChannelId, (uint)MessageType.RemoveChannel, new RemoveChannelMessage()
            {
                ChannelId = channelId
            }, BroadcastType.No, WrapMessageHandler(callback));
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
            Send(GlobalChannelId, (uint)MessageType.ListChannel, listMsg, BroadcastType.No, WrapMessageHandler(callback));
        }

        public void SubToChannel(uint channelId, ChannelSubscriptionOptions options = null, Action<SubscribedToChannelResultMessage> callback = null)
        {
            Send(channelId, (uint)MessageType.SubToChannel, new SubscribedToChannelMessage()
            {
                ConnId = Id,
                SubOptions = options
            }, BroadcastType.No, WrapMessageHandler(callback));
        }

        public void UnsubFromChannel(uint channelId, Action<UnsubscribedFromChannelMessage> callback = null)
        {
            Send(channelId, (uint)MessageType.UnsubFromChannel, new UnsubscribedFromChannelMessage()
            {
                ConnId = Id
            }, BroadcastType.No, WrapMessageHandler(callback));
        }
    }
}
