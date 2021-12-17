using Google.Protobuf;
using Mirror;
using System;
using System.IO;

namespace Channeld
{
    public static class MirrorUtils
    {
        private static NetworkReader msgTypeReader = new NetworkReader(new byte[0]);

        public static uint GetChanneldMsgType(ArraySegment<byte> segment)
        {
            ushort msgType;
            msgTypeReader.SetBuffer(segment);
            if (MessagePacking.Unpack(msgTypeReader, out msgType))
            {
                msgType += (ushort)MessageType.UserSpaceStart;
            }
            else
            {
                msgType = (ushort)MessageType.UserSpaceStart;
            }
            return msgType;
        }

    }
}
