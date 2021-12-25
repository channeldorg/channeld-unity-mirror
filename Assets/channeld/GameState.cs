using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Mirror;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Channeld
{
    public abstract class GameState<T> : MonoBehaviour where T : class, IMessage<T>, new()
    {
        public ChannelType channelType;
        public uint ChannelId { get; private set; }
        public T ChannelData { get; private set; }

        private ChanneldClient client;
        private T bufferedUpdate;

        public static Action<uint, T> OnGameStateChanged;

        private static Dictionary<uint, GameState<T>> gameStatesInChannels = new Dictionary<uint, GameState<T>>();
        public static GameState<T> GetInstance(uint channelId)
        {
            GameState<T> instance;
            if (gameStatesInChannels.TryGetValue(channelId, out instance))
            {
                return instance;
            }
            return null;
        }

        private void Awake()
        {
            ChanneldClient.OnAuthenticated += OnChanneldAuthenticated;

            ChannelData = new T();
        }

        protected virtual void Merge(T dst, T src)
        {
            // Use Protobuf's default message merge.
            // However, the merge of two maps doesn't meet our requirements (having the same key causes exception).
            // In that case, we need to override this method and manually merge the map (use set indexer instead of Add).
            // TODO: change the source code of Google Protobuf 
            dst.MergeFrom(src);
        }

        private void OnChanneldAuthenticated(ChanneldClient client)
        {
            this.client = client;
            client.AddMessageHandler((uint)MessageType.CreateChannel, (c, channelId, msg) =>
            {
                var resultMsg = (CreateChannelResultMessage)msg;
                if (resultMsg.ChannelType == channelType)
                {
                    ChannelId = channelId;
                    gameStatesInChannels[channelId] = this;
                    Log.Info($"Added GameState '{this.GetType().Name}' for channel {channelId}");
                }
            });
            client.AddMessageHandler((uint)MessageType.RemoveChannel, (c, channelId, msg) =>
            {
                var removeMsg = (RemoveChannelMessage)msg;
                if (gameStatesInChannels.Remove(channelId))
                {
                    Log.Info($"Removed GameState '{this.GetType().Name}' for channel {channelId}");
                }
            });
            client.AddMessageHandler((uint)MessageType.SubToChannel, (c, channelId, msg) =>
            {
                var resultMsg = (SubscribedToChannelResultMessage)msg;
                if (resultMsg.ConnId == c.Id && resultMsg.ChannelType == channelType)
                {
                    ChannelId = channelId;
                    gameStatesInChannels[channelId] = this;
                    Log.Info($"Added GameState '{this.GetType().Name}' for channel {channelId}");
                }
            });
            client.AddMessageHandler((uint)MessageType.UnsubFromChannel, (c, channelId, msg) =>
            {
                var unsubMsg = (UnsubscribedFromChannelMessage)msg;
                if (unsubMsg.ConnId == c.Id)
                {
                    if (gameStatesInChannels.Remove(channelId))
                    {
                        Log.Info($"Removed GameState '{this.GetType().Name}' for channel {channelId}");
                    }
                }
            });
            client.AddMessageHandler((uint)MessageType.ChannelDataUpdate, (_, channelId, msg) =>
            {
                var updateData = (msg as ChannelDataUpdateMessage).Data.Unpack<T>();
                Log.Debug($"Receive {typeof(T).Name} update: {updateData.ToString()}");
                Merge(ChannelData, updateData);
                OnGameStateChanged?.Invoke(channelId, updateData);
            });
        }

        protected void SendUpdate(T update)
        {
            Log.Debug($"Send {typeof(T).Name} update: {update.ToString()}");

            if (bufferedUpdate == null)
                bufferedUpdate = update;
            else
                Merge(bufferedUpdate, update);
        }

        public static void SendUpdate(NetworkIdentity ni, T update)
        {
            var channelId = ChanneldTransport.GetOwningChannel(ni.netId);
            var instance = GetInstance(channelId);
            if (instance == null)
            {
                Log.Warning($"Cannot find GameState by channelId: {channelId} (netId={ni.netId})");
                return;
            }
            instance.SendUpdate(update);
        }

        // Construct a channel data message used to be merged and sent.
        protected abstract T ConstructTransformUpdate(NetworkIdentity ni, Vector3? position, Quaternion? rotation, Vector3? scale);

        public static void SendTransformUpdate(NetworkIdentity ni, Vector3? position, Quaternion? rotation, Vector3? scale)
        {
            var channelId = ChanneldTransport.GetOwningChannel(ni.netId);
            var instance = GetInstance(channelId);
            if (instance == null)
            {
                Log.Warning($"Cannot find GameState by channelId: {channelId} (netId={ni.netId})");
                return;
            }
            T update = instance.ConstructTransformUpdate(ni, position, rotation, scale);
            instance.SendUpdate(update);
        }

        // Make sure the message is sent after all the updates are buffered.
        private void LateUpdate()
        {
            if (client == null)
                return;

            if (bufferedUpdate == null)
                return;

            client.Send(ChannelId, (uint)MessageType.ChannelDataUpdate, new ChannelDataUpdateMessage()
            {
                Data = Any.Pack(bufferedUpdate)
            }, BroadcastType.No);

            bufferedUpdate = null;
        }
    }
}
