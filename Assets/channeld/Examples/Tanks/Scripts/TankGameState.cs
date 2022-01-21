using UnityEngine;
using Mirror;
using Google.Protobuf;

namespace Channeld.Examples.Tanks
{
    public class TankGameState : GameState<TankGameChannelData>
    {
        protected override IMessage GetChannelDataUpdateFromTransform(NetworkIdentity ni, bool removed, Vector3? position, Quaternion? rotation, Vector3? scale)
        {
            var updateData = new TankGameChannelData();
            var transformState = new TransformState() { Removed = removed };
            if (!removed)
            {
                if (position.HasValue)
                    transformState.Position = new Vector3f() { X = position.Value.x, Y = position.Value.y, Z = position.Value.z };
                if (rotation.HasValue)
                    transformState.Rotation = new Vector4f() { X = rotation.Value.x, Y = rotation.Value.y, Z = rotation.Value.z, W = rotation.Value.w };
                if (scale.HasValue)
                    transformState.Scale = new Vector3f() { X = scale.Value.x, Y = scale.Value.y, Z = scale.Value.z };
            }
            updateData.TransformStates[ni.netId] = transformState;
            return updateData;
        }

        public override TransformUpdateData GetTransformUpdateFromChannelData(IMessage channelUpdateData, NetworkIdentity ni)
        {
            var updateData = (TankGameChannelData)channelUpdateData;
            TransformState transformState;
            if (updateData.TransformStates.TryGetValue(ni.netId, out transformState))
            {
                if (transformState.Removed)
                    return null;

                return new TransformUpdateData()
                {
                    Position = transformState.GetUnityPosition(),
                    Rotation = transformState.GetUnityRotation(),
                    Scale = transformState.GetUnityScale()
                };
            }
            return null;
        }

        protected override void Merge(IMessage dst, IMessage src)
        {
            var dstState = (TankGameChannelData)dst;
            var srcState = (TankGameChannelData)src;

            foreach (var kv in srcState.TransformStates)
            {
                if (kv.Value.Removed)
                {
                    dstState.TransformStates.Remove(kv.Key);
                    continue;
                }

                TransformState transformState;
                if (dstState.TransformStates.TryGetValue(kv.Key, out transformState))
                {
                    // The default merge causes null position/rotation/scale overwriting the non-null value
                    //transformState.MergeFrom(kv.Value);
                    if (kv.Value.Position != null)
                        transformState.Position = kv.Value.Position;
                    if (kv.Value.Rotation != null)
                        transformState.Rotation = kv.Value.Rotation;
                    if (kv.Value.Position != null)
                        transformState.Scale = kv.Value.Scale;
                }
                else
                {
                    dstState.TransformStates[kv.Key] = kv.Value;
                }
            }

            foreach (var kv in srcState.TankStates)
            {
                if (kv.Value.Removed)
                {
                    dstState.TankStates.Remove(kv.Key);
                    continue;
                }

                TankState tankState;
                if (dstState.TankStates.TryGetValue(kv.Key, out tankState))
                {
                    tankState.MergeFrom(kv.Value);
                }
                else
                {
                    dstState.TankStates[kv.Key] = kv.Value;
                }
            }
        }

        public override ChannelDataMergeOptions SetUpMergeOptions()
        {
            return new ChannelDataMergeOptions() { ShouldCheckRemovableMapField = true };
        }
    }

    public static class TransformStateExtension
    {
        public static Vector3? GetUnityPosition(this TransformState transformState)
        {
            return transformState.Position == null ? null : new Vector3?(new Vector3(transformState.Position.X, transformState.Position.Y, transformState.Position.Z));
        }

        public static Quaternion? GetUnityRotation(this TransformState transformState)
        {
            return transformState.Rotation == null ? null : new Quaternion?(new Quaternion(transformState.Rotation.X, transformState.Rotation.Y, transformState.Rotation.Z, transformState.Rotation.W));
        }

        public static Vector3? GetUnityScale(this TransformState transformState)
        {
            return transformState.Scale == null ? null : new Vector3?(new Vector3(transformState.Scale.X, transformState.Scale.Y, transformState.Scale.Z));
        }
    }
}