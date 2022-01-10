using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Mirror;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Channeld
{
    public class TransformUpdateData
    {
        public Vector3? Position;
        public Quaternion? Rotation;
        public Vector3? Scale;
    }

    // Accessor of all static methods.
    // DO NOT inherit from this class. Use GameState<T> instead.
    public abstract class GameState : MonoBehaviour
    {
        public ChannelType channelType;
        public uint ChannelId { get; protected set; }

        protected ChanneldClient client;

        private IMessage bufferedUpdate;

        protected static Dictionary<ChannelType, GameState> statesByChannelType = new Dictionary<ChannelType, GameState>();
        public static GameState GetByChannelType(ChannelType channelType)
        {
            GameState instance;
            if (statesByChannelType.TryGetValue(channelType, out instance))
            {
                return instance;
            }
            return null;
        }

        protected static Dictionary<uint, GameState> statesInChannels = new Dictionary<uint, GameState>();
        public static GameState GetByChannelId(uint channelId)
        {
            GameState instance;
            if (statesInChannels.TryGetValue(channelId, out instance))
            {
                return instance;
            }
            return null;
        }

        public static Action<uint, GameState, IMessage> OnDataChanged;

        protected virtual void Awake()
        {
            if (statesByChannelType.ContainsKey(channelType))
            {
                Log.Error($"GameState with ChannelType '{channelType}' alreadys exists. There can only be one GameState per ChannelType. Object name: {gameObject.name}");
                return;
            }
            statesByChannelType[channelType] = this;

            ChanneldClient.OnAuthenticated += OnChanneldAuthenticated;
        }

        protected virtual void OnChanneldAuthenticated(ChanneldClient client)
        {
            this.client = client;
            client.AddMessageHandler((uint)MessageType.CreateChannel, (c, channelId, msg) =>
            {
                var resultMsg = (CreateChannelResultMessage)msg;
                if (resultMsg.ChannelType == channelType)
                {
                    ChannelId = channelId;
                    statesInChannels[channelId] = this;
                    Log.Info($"Added GameState '{this.GetType().Name}' for channel {channelId}");
                }
            });
            client.AddMessageHandler((uint)MessageType.RemoveChannel, (c, channelId, msg) =>
            {
                var removeMsg = (RemoveChannelMessage)msg;
                if (statesInChannels.Remove(channelId))
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
                    statesInChannels[channelId] = this;
                    Log.Info($"Added GameState '{this.GetType().Name}' for channel {channelId}");
                }
            });
            client.AddMessageHandler((uint)MessageType.UnsubFromChannel, (c, channelId, msg) =>
            {
                var unsubMsg = (UnsubscribedFromChannelMessage)msg;
                if (unsubMsg.ConnId == c.Id)
                {
                    if (statesInChannels.Remove(channelId))
                    {
                        Log.Info($"Removed GameState '{this.GetType().Name}' for channel {channelId}");
                    }
                }
            });
        }

        // The channel data used by server to create the channel. Could be null.
        public virtual IMessage SetUpInitData() => null;

        // The data merge options used by server to create the channel. Could be null.
        public virtual ChannelDataMergeOptions SetUpMergeOptions() => null;

        // Construct the TransformUpdateData used to update the local transform.
        public abstract TransformUpdateData GetTransformUpdateFromChannelData(IMessage channelUpdateData, NetworkIdentity ni);

        // Construct a channel data message used to be merged and sent.
        protected abstract IMessage GetChannelDataUpdateFromTransform(NetworkIdentity ni, bool removed, Vector3? position, Quaternion? rotation, Vector3? scale);

        protected abstract void Merge(IMessage dst, IMessage src);

        protected void SendUpdate(IMessage update)
        {
            Log.Debug($"Send {update.GetType().Name} update: {update.ToString()}");

            if (bufferedUpdate == null)
                bufferedUpdate = update;
            else
                Merge(bufferedUpdate, update);
        }

        public static void SendUpdate(NetworkIdentity ni, IMessage update)
        {
            var channelId = ChanneldTransport.GetOwningChannel(ni.netId);
            var instance = GetByChannelId(channelId);
            if (instance == null)
            {
                Log.Warning($"Cannot find GameState by channelId: {channelId} (netId={ni.netId})");
                return;
            }
            instance.SendUpdate(update);
        }

        public static void SendTransformUpdate(NetworkIdentity ni, bool removed, Vector3? position, Quaternion? rotation, Vector3? scale)
        {
            var channelId = ChanneldTransport.GetOwningChannel(ni.netId);
            var instance = GetByChannelId(channelId);
            if (instance == null)
            {
                Log.Warning($"Cannot find GameState by channelId: {channelId} (netId={ni.netId})");
                return;
            }
            IMessage update = instance.GetChannelDataUpdateFromTransform(ni, removed, position, rotation, scale);
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
            }, BroadcastType.NoBroadcast);

            bufferedUpdate = null;
        }
    }

    // GameState base class with generic type which is used to help to merge data and unpack from Any message type.
    // All concrete GameState class should inherit from this class.
    public abstract class GameState<T> : GameState where T : class, IMessage<T>, new()
    {
        public T ChannelData { get; private set; }

        public static Action<uint, T> OnGenericDataChanged;

        protected override void Awake()
        {
            base.Awake();

            ChannelData = (T)SetUpInitData();
        }

        public override IMessage SetUpInitData()
        {
            return new T();
        }

        protected override void Merge(IMessage dst, IMessage src)
        {
            // Use Protobuf's default message merge.
            // However, the merge of two maps doesn't meet our requirements (having the same key causes exception).
            // In that case, we need to override this method and manually merge the map (use set indexer instead of Add).
            // TODO: change the source code of Google Protobuf 
            ((T)dst).MergeFrom((T)src);
        }

        protected override void OnChanneldAuthenticated(ChanneldClient client)
        {
            base.OnChanneldAuthenticated(client);
            client.AddMessageHandler((uint)MessageType.ChannelDataUpdate, (_, channelId, msg) =>
            {
                var updateData = (msg as ChannelDataUpdateMessage).Data.Unpack<T>();
                Log.Debug($"Receive {typeof(T).Name} update: {updateData.ToString()}");
                Merge(ChannelData, updateData);
                OnDataChanged?.Invoke(channelId, this, updateData);
                OnGenericDataChanged?.Invoke(channelId, updateData);
            });
        }
    }
}
