
using UnityEngine;

namespace Channeldpb
{
    public static class ChanneldpbExt
    {
        public static Vector3 ToVector3(this SpatialInfo info)
        {
            return new Vector3((float)info.X, (float)info.Y, (float)info.Z);
        }

        public static Bounds ToBounds(this SpatialRegion region)
        {
            var bounds = new Bounds();
            bounds.min = region.Min.ToVector3();
            bounds.max = region.Max.ToVector3();
            return bounds;
        }
    }
}
