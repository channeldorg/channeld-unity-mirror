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
        //private static NetworkReader emptyReader = new NetworkReader(new byte[0]);

        public static uint GetChanneldMsgType(ArraySegment<byte> segment)
        {
            return (uint)MessageType.UserSpaceStart;
/*  Mirror's Transport.Send() receives batched messages which consists of a double time header and multiple NetworkMessages.
 *  So it's impossible to check only one type of message in the batch.
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
*/
        }


        #region Extension methods

        public static void BroadcastNetworkMessage<T>(this ChanneldConnection conn, uint channelId, T message, BroadcastType broadcast, uint clientConnId = 0) where T : struct, NetworkMessage
        {
            using (PooledNetworkWriter packetWriter = NetworkWriterPool.GetWriter())
            {
                // A packet consists of a timestamp and a series of NetworkMessage.
                packetWriter.WriteDouble(NetworkTime.localTime);
                MessagePacking.Pack(message, packetWriter);
                var segment = packetWriter.ToArraySegment();
                
                conn.Send(channelId, MirrorUtils.GetChanneldMsgType(segment), new ServerForwardMessage()
                {
                    ClientConnId = clientConnId,
                    Payload = ByteString.CopyFrom(segment.Array, segment.Offset, segment.Count),
                }, broadcast);

            }
        }

        public static void SendNetworkMessage<T>(this ChanneldConnection conn, uint channelId, T message, uint clientConnId) where T : struct, NetworkMessage
        {
            conn.BroadcastNetworkMessage(channelId, message, BroadcastType.SingleConnection, clientConnId);
        }

        #endregion
    }
}
