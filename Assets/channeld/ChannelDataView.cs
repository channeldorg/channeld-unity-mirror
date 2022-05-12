
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
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

        // Unfortunately, MessageParser.CreateTemplate() is not public...
        public static void RegisterChannelDataParser(ChannelType channelType, IMessage channelDataTemplate, MessageParser parser)
        {
            channelDataParsers[Any.Pack(channelDataTemplate).TypeUrl] = parser;

            
            //channelDataTemplates[channelType] = channelDataTemplate;
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
            Connection = conn;
            Connection.AddMessageHandler((uint)MessageType.ChannelDataUpdate, HandleChannelDataUpdate);
            InitChannels();
        }

        public virtual void Unintialize()
        {
            Connection.RemoveMessageHandler((uint)MessageType.ChannelDataUpdate, HandleChannelDataUpdate);
            UninitChannels();
        }

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

            foreach (var kv in Connection.SubscribedChannels)
            {
                if (kv.Value.ChannelType == channelType)
                {
                    AddChannelDataProvider(kv.Key, provider);
                    return;
                }
            }

            Log.Warning($"Failed to AddChannelDataProviderToDefaultChannel: no default channel found of type '{channelType}', data type: {channelDataType}.");
        }


        public void AddChannelDataProvider(uint channelId, IChannelDataProvider provider)
        {
            HashSet<IChannelDataProvider> providers;
            if (!channelDataProviders.TryGetValue(channelId, out providers))
            {
                providers = new HashSet<IChannelDataProvider>();
                channelDataProviders[channelId] = providers;
                Log.Info($"Added channel data provider {provider} to channel {channelId}");
            }
            providers.Add(provider);
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

            Log.Warning($"Failed to RemoveChannelDataProviderFromAllChannels: no default channel found of type '{channelType}', data type: {channelDataType}.");
        }

        public void RemoveChannelDataProvider(uint channelId, IChannelDataProvider provider)
        {
            HashSet<IChannelDataProvider> providers;
            if (channelDataProviders.TryGetValue(channelId, out providers))
            {
                providers.Remove(provider);
                Log.Info($"Removed channel data provider {provider} from channel {channelId}");
            }
        }

        protected virtual void HandleChannelDataUpdate(ChanneldConnection conn, uint channelId, IMessage msg)
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
                Log.Error($"No provider registered for channel {channelId}, typeUrl: {updateMsg.Data.TypeUrl}");
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
                    // FIXME: dynamic 
                    foreach (var provider in providers)
                    {
                       if (provider.UpdateChannelData(newState))
                            updateCount++;
                    }

                    if (updateCount > 0)
                    {
                        Connection.Send(channelId, (uint)MessageType.ChannelDataUpdate, new ChannelDataUpdateMessage()
                        {
                            Data = Any.Pack(newState)
                        }, BroadcastType.NoBroadcast);
                    }
                }
            }
        }
    }
}
