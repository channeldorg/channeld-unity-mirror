using UnityEngine;
using Mirror;
using Channeld;
using Channeld.Examples.Tanks;

namespace Mirror.Examples.Tanks
{
    public class TankGameState : GameState<TankGameChannelData>
    {
        protected override TankGameChannelData ConstructTransformUpdate(NetworkIdentity ni, Vector3? position, Quaternion? rotation, Vector3? scale)
        {
            var updateData = new TankGameChannelData();
            var transformState = new TransformState();
            if (position.HasValue)
                transformState.Position = new Vector3f() { X = position.Value.x, Y = position.Value.y, Z = position.Value.z };
            if (rotation.HasValue)
                transformState.Rotation = new Vector4f() { X = rotation.Value.x, Y = rotation.Value.y, Z = rotation.Value.z, W = rotation.Value.w };
            if (scale.HasValue)
                transformState.Scale = new Vector3f() { X = scale.Value.x, Y = scale.Value.y, Z = scale.Value.z };
            updateData.TransformStates[ni.netId] = transformState;
            return updateData;
        }

        protected override void Merge(TankGameChannelData dst, TankGameChannelData src)
        {
            foreach (var kv in src.TransformStates)
            {
                TransformState transformState;
                if (dst.TransformStates.TryGetValue(kv.Key, out transformState))
                {
                    transformState.MergeFrom(kv.Value);
                }
                else
                {
                    dst.TransformStates[kv.Key] = kv.Value;
                }
            }

            foreach (var kv in src.TankStates)
            {
                TankState tankState;
                if (dst.TankStates.TryGetValue(kv.Key, out tankState))
                {
                    tankState.MergeFrom(kv.Value);
                }
                else
                {
                    dst.TankStates[kv.Key] = kv.Value;
                }
            }
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