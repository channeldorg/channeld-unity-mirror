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
            dst.MergeFrom(src);
        }

        private void OnChanneldAuthenticated(ChanneldClient client)
        {
            this.client = client;
            client.AddMessageHandler((uint)MessageType.ChannelDataUpdate, (_, channelId, msg) =>
            {
                if (!gameStatesInChannels.ContainsKey(channelId))
                {
                    gameStatesInChannels[channelId] = this;
                }

                var updateData = (msg as ChannelDataUpdateMessage).Data.Unpack<T>();
                Debug.Log($"Receive {typeof(T).Name} update: {updateData.ToString()}");
                Merge(ChannelData, updateData);
                OnGameStateChanged?.Invoke(channelId, updateData);
            });
        }

        protected void SendUpdate(T update)
        {
            Debug.Log($"Send {typeof(T).Name} update: {update.ToString()}");

            if (bufferedUpdate == null)
                bufferedUpdate = update;
            else
                Merge(bufferedUpdate, update);
        }

        public static void SendUpdate(NetworkIdentity ni, T update)
        {
            var channelId = ChanneldTransport.GetOwningChannel(ni.netId);
            var instance = GetInstance(channelId);
            if (instance != null)
                instance.SendUpdate(update);
        }

        // Construct a channel data message used to be merged and sent.
        protected abstract T ConstructTransformUpdate(NetworkIdentity ni, Vector3? position, Quaternion? rotation, Vector3? scale);

        public static void SendTransformUpdate(NetworkIdentity ni, Vector3? position, Quaternion? rotation, Vector3? scale)
        {
            var channelId = ChanneldTransport.GetOwningChannel(ni.netId);
            var instance = GetInstance(channelId);
            if (instance != null)
            {
                T update = instance.ConstructTransformUpdate(ni, position, rotation, scale);
                if (update != null)
                    instance.SendUpdate(update);
            }
        }

        // Make sure the message is sent after all the updates are buffered.
        private void LateUpdate()
        {
            if (client == null)
                return;

            if (bufferedUpdate == null)
                return;

            var transport = Transport.activeTransport as ChanneldTransport;
            client.Send(transport.TargetChannelId ?? 0, (uint)MessageType.ChannelDataUpdate, new ChannelDataUpdateMessage()
            {
                Data = Any.Pack(bufferedUpdate)
            }, BroadcastType.No);

            bufferedUpdate = null;
        }
    }
}
