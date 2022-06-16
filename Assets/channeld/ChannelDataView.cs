
using Channeldpb;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Mirror;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;

namespace Channeld
{
    public abstract class ChannelDataView : ScriptableObject
    {
        // Key: the type URL of the channel data message
        private static Dictionary<string, MessageParser> channelDataParsers = new Dictionary<string, MessageParser>();
        //private static Dictionary<ChannelType, IMessage> channelDataTemplates = new Dictionary<ChannelType, IMessage>();
        private static Dictionary<ChannelType, System.Func<IMessage>> channelDataCreators = new Dictionary<ChannelType, System.Func<IMessage>>();
        private static Dictionary<System.Type, ChannelType> channelDataTypes = new Dictionary<System.Type, ChannelType>();
        // The spawned object's netId mapping to the id of the channel that owns the object.
        protected static Dictionary<uint, uint> netIdOwningChannels = new Dictionary<uint, uint>();
        public static uint GetOwningChannel(uint netId)
        {
            uint channelId = 0;
            if (!netIdOwningChannels.TryGetValue(netId, out channelId))
                Log.Warning($"No owning channel found for netId {netId}, fall back to GLOBAL channel.");
            return channelId;
        }

        public static void RegisterChannelDataParser(ChannelType channelType, IMessage channelDataTemplate, MessageParser parser)
        {
            channelDataParsers[Any.Pack(channelDataTemplate).TypeUrl] = parser;

            //channelDataTemplates[channelType] = channelDataTemplate;
            // Unfortunately, MessageParser.CreateTemplate() is not public...
            // We have to instantiate the ChannelData message using reflection, as it cannot be cast to IDeepClone<IMessage>
            // Use DynamicMethod for the sake of performance: https://andrewlock.net/benchmarking-4-reflection-methods-for-calling-a-constructor-in-dotnet/
            var channelDataType = channelDataTemplate.GetType();
            channelDataTypes[channelDataType] = channelType;
            var channelDataCtor = new DynamicMethod($"ChannelDataConstructor_{channelType}", channelDataType, null);
            var constructorInfo = channelDataType.GetConstructor(System.Type.EmptyTypes);
            var il = channelDataCtor.GetILGenerator();
            il.Emit(OpCodes.Newobj, constructorInfo);
            il.Emit(OpCodes.Ret);
            channelDataCreators[channelType] = (System.Func<IMessage>)channelDataCtor.CreateDelegate(typeof(System.Func<IMessage>));
        }

        // Key: channelId
        private Dictionary<uint, HashSet<IChannelDataProvider>> channelDataProviders = new Dictionary<uint, HashSet<IChannelDataProvider>>();

        public ChanneldConnection Connection {get; private set;}

        public virtual void Initialize(ChanneldConnection conn)
        {
            if (Connection == null)
            { 
                Connection = conn;
            
                LoadCmdLineArgs();
            
                Connection.AddMessageHandler((uint)MessageType.ChannelDataUpdate, HandleChannelDataUpdate);
                Connection.AddMessageHandler((uint)MessageType.UnsubFromChannel, HandleUnsub);
                if (Connection.ConnectionType == ConnectionType.Client)
                {
                    Action<uint, uint, byte[]> handler = (channelId, clientConnId, payload) =>
                    {
                        // The payload may contains multiple Mirror messages, making it hard to recognize the SpawnMessage inside.
                        // We have to use this awkward way to make sure when handling SpawnMessage, the NetworkClient has the right channelId context.
                        // FIXME: how to reduce memory allocation?
                        Action<SpawnMessage> onSpawn = (msg) =>
                        {
                            netIdOwningChannels[msg.netId] = channelId;
                            Log.Info($"Client set up mapping of netId: {msg.netId} -> channelId: {channelId}");
                            NetworkClientExposed.OnSpawn(msg);
                        };
                        NetworkClient.ReplaceHandler<SpawnMessage>(onSpawn, false);
                    };
                    // Make sure NetworkClient.ReplaceHandler() is called before NetworkClient.OnTransportData()
                    handler += Connection.UserSpaceMessageHandleFunc;
                    Connection.UserSpaceMessageHandleFunc = handler;
                }
            }

            InitChannels();
            
            Log.Info($"{GetType()} initialized channels.");
        }

        public virtual void Unintialize()
        {
            if (Connection != null)
            {
                Connection.RemoveMessageHandler((uint)MessageType.ChannelDataUpdate, HandleChannelDataUpdate);
            }

            UninitChannels();
            Log.Info($"{GetType()} uninitialized channels.");
        }

        protected virtual void LoadCmdLineArgs() { }
        protected abstract void InitChannels();
        protected abstract void UninitChannels();

        /*
        public void AddChannelDataProviderToDefaultChannel<T>(IChannelDataProvider<T> provider) where T : IMessage<T>
        {
            AddChannelDataProviderToDefaultChannel(provider, typeof(T));
        }
        */

        public void AddChannelDataProviderToDefaultChannel(IChannelDataProvider provider)
        {
            if (Connection == null)
            {
                Log.Error("Unable to call AddChannelDataProviderToDefaultChannel. The connection to channeld hasn't been set up yet and there's no subscription to any channel.");
                return;
            }

            var channelDataType = provider.GetChannelDataType();
            ChannelType channelType;
            if (!channelDataTypes.TryGetValue(channelDataType, out channelType))
            {
                Log.Error($"Unregistered channel data type: {channelDataType}.");
                return;
            }

            if (Connection.ConnectionType == ConnectionType.Server)
            {
                // Server: add the provider to ALL the matching channels that the server owns.
                foreach (var kv in Connection.OwnedChannels)
                {
                    if (kv.Value.ChannelType == channelType)
                    {
                        AddChannelDataProvider(kv.Key, provider);
                    }
                }
            }
            else
            {
                // Client: add the provider to the channel that spawns the netId.
                if (provider is NetworkBehaviour networkBehaviour)
                {
                    uint channelId = 0;
                    if (netIdOwningChannels.TryGetValue(networkBehaviour.netId, out channelId))
                    {
                        AddChannelDataProvider(channelId, provider);
                        return;
                    }
                    else
                    {
                        Log.Error($"No channelId mapping found for netId {networkBehaviour.netId}");
                    }
                }
                /*
                foreach (var kv in Connection.SubscribedChannels)
                {
                    if (kv.Value.ChannelType == channelType)
                    {
                        AddChannelDataProvider(kv.Key, provider);
                        return;
                    }
                }

                Log.Warning($"Failed to AddChannelDataProviderToDefaultChannel: no default channel found of type '{channelType}', data type: {channelDataType}.");
                */

            }
        }


        public void AddChannelDataProvider(uint channelId, IChannelDataProvider provider)
        {
            HashSet<IChannelDataProvider> providers;
            if (!channelDataProviders.TryGetValue(channelId, out providers))
            {
                providers = new HashSet<IChannelDataProvider>();
                channelDataProviders[channelId] = providers;
            }

            if (providers.Add(provider))
                Log.Info($"Added channel data provider {provider} to channel {channelId}");
        }

        /*
        public void RemoveChannelDataProviderFromAllChannels<T>(IChannelDataProvider<T> provider) where T : IMessage<T>
        {
            RemoveChannelDataProviderFromAllChannels(provider, typeof(T));
        }
        */

        public void RemoveChannelDataProviderFromAllChannels(IChannelDataProvider provider)
        {
            if (Connection == null)
            {
                Log.Error("Unable to call RemoveChannelDataProviderFromAllChannels. The connection to channeld hasn't been set up yet and there's no subscription to any channel.");
                return;
            }

            var channelDataType = provider.GetChannelDataType();
            ChannelType channelType;
            if (!channelDataTypes.TryGetValue(channelDataType, out channelType))
            {
                Log.Error($"Unregistered channel data type: {channelDataType}.");
                return;
            }

            foreach (var kv in Connection.SubscribedChannels)
            {
                if (kv.Value.ChannelType == channelType)
                {
                    RemoveChannelDataProvider(kv.Key, provider);
                    return;
                }
            }

            //Log.Warning($"Failed to RemoveChannelDataProviderFromAllChannels: no default channel found of type '{channelType}', data type: {channelDataType}.");
        }

        public void RemoveChannelDataProvider(uint channelId, IChannelDataProvider provider)
        {
            HashSet<IChannelDataProvider> providers;
            if (channelDataProviders.TryGetValue(channelId, out providers))
            {
                // Post the remove to the next SendAllChannelUpdates, so the Removed=true will be set in the provider's UpdateChannelData().
                //providers.Remove(provider);
                Log.Info($"Removing channel data provider {provider} from channel {channelId}");
                provider.IsRemoved = true;
            }
        }

        public void OnDisconnect()
        {
            foreach (var kv in channelDataProviders)
            {
                foreach (var provider in kv.Value)
                {
                    provider.IsRemoved = true;
                }
            }
            // Force to send the channel update data with the removed states to channeld
            SendAllChannelUpdates();
        }

        private void HandleUnsub(ChanneldConnection conn, uint channelId, IMessage msg)
        {
            var unsubMsg = (UnsubscribedFromChannelResultMessage)msg;
            if (unsubMsg.ConnId == Connection.Id)
            {
                HashSet<IChannelDataProvider> providers;
                if (channelDataProviders.TryGetValue(channelId, out providers))
                {
                    channelDataProviders.Remove(channelId);
                    Log.Info($"Received Unsub message. Removed all data providers({providers.Count}) from channel {channelId}");
                    OnUnsubFromChannel(channelId, providers);
                }
            }
        }

        protected virtual void OnUnsubFromChannel(uint channelId, IEnumerable<IChannelDataProvider> removedProviders){ }

        private void HandleChannelDataUpdate(ChanneldConnection conn, uint channelId, IMessage msg)
        {
            var updateMsg = msg as ChannelDataUpdateMessage;
            MessageParser channelDataParser;
            if (!channelDataParsers.TryGetValue(updateMsg.Data.TypeUrl, out channelDataParser))
            {
                Log.Error($"Unable to find channel data parser by TypeURL: {updateMsg.Data.TypeUrl}");
                return;
            }
            var updateData = channelDataParser.ParseFrom(updateMsg.Data.Value);
            Log.Debug($"Receive {updateData.GetType().Name} update: {updateData.ToString()}");
            //Merge(ChannelData, updateData);
            //OnDataChanged?.Invoke(channelId, this, updateData);
            //OnGenericDataChanged?.Invoke(channelId, updateData);

            HashSet<IChannelDataProvider> providers;
            if (!channelDataProviders.TryGetValue(channelId, out providers))
            {
                Log.Warning($"No provider registered for channel {channelId}, typeUrl: {updateMsg.Data.TypeUrl}");
                return;
            }
            foreach (var provider in providers)
            {
                provider.OnChannelDataUpdated(updateData);
            }

        }

        public void SendAllChannelUpdates()
        {
            if (Connection == null)
                return;

            foreach (var kv in Connection.SubscribedChannels)
            {
                if (kv.Value.SubOptions.CanUpdateData)
                {
                    uint channelId = kv.Key;
                    HashSet<IChannelDataProvider> providers;
                    if (!channelDataProviders.TryGetValue(channelId, out providers))
                    {
                        continue;
                    }

                    System.Func<IMessage> messageCreator;
                    if (!channelDataCreators.TryGetValue(kv.Value.ChannelType, out messageCreator))
                    {
                        continue;
                    }
                    
                    // Protobuf-generated classes in C# don't have Clear/Reset method,
                    // so we don't store any ChannelData as the member variable,
                    // as the ChannelData has to be created for every update.
                    //IMessage newState = ((IDeepCloneable<IMessage>)template).Clone();
                    IMessage newState = messageCreator();

                    int updateCount = 0;
                    foreach (var provider in providers)
                    {
                       if (provider.UpdateChannelData(newState))
                            updateCount++;
                    }
                    // Actually remove the provider from the set.
                    int removeCount = providers.RemoveWhere(p => p.IsRemoved);
                    if (removeCount > 0)
                        Log.Info($"Removed {removeCount} channel data provider(s) from channel {channelId}");

                    if (updateCount > 0)
                    {
                        Connection.Send(channelId, (uint)MessageType.ChannelDataUpdate, new ChannelDataUpdateMessage()
                        {
                            Data = Any.Pack(newState),
                            /* FIXME: in authoratative server, send the connId of the client that causes the data update
                            ContextConnId = Connection.Id,
                            */
                        }, BroadcastType.NoBroadcast);
                    }
                }
            }
        }
    }
}
