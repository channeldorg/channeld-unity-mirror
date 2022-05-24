using Channeldpb;
using Google.Protobuf;
using Mirror;
using System;
using System.IO;
using UnityEngine;

namespace Channeld
{
    public static class MirrorUtils
    {
        private static NetworkReader emptyReader = new NetworkReader(new byte[0]);

        public static uint GetChanneldMsgType(ArraySegment<byte> segment)
        {
            ushort msgType;
            emptyReader.SetBuffer(segment);
            if (MessagePacking.Unpack(emptyReader, out msgType))
            {
                msgType += (ushort)MessageType.UserSpaceStart;
            }
            else
            {
                msgType = (ushort)MessageType.UserSpaceStart;
            }
            return msgType;
        }

        public static bool IsMessage<T>(uint channeldMsgType) where T : struct, NetworkMessage
        {
            return (uint)MessagePacking.GetId<T>() == channeldMsgType - (uint)MessageType.UserSpaceStart;
        }

        public static bool IsMessage<T>(ArraySegment<byte> segment, NetworkReader reader = null) where T : struct, NetworkMessage
        {
            if (reader == null)
                reader = emptyReader;
            reader.SetBuffer(segment);
            ushort msgType;
            return MessagePacking.Unpack(reader, out msgType) && msgType == MessagePacking.GetId<T>();
        }

        #region Extension methods

        /*
        public static uint GetOwningChannel(this NetworkBehaviour netBehaviour)
        {
            return ChanneldTransport.GetOwningChannel(netBehaviour.netId);
        }
        */

        #endregion
    }
}
