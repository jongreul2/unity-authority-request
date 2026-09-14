using Jongreul.AuthorityRequest.Grab;
using UnityEngine;

namespace Jongreul.AuthorityRequest
{
    /// <summary>엔진 비의존 <see cref="PoseData"/> ↔ Unity 타입 변환.</summary>
    public static class PoseConversions
    {
        public static PoseData ToPoseData(Vector3 position, Quaternion rotation) =>
            new PoseData(position.x, position.y, position.z, rotation.x, rotation.y, rotation.z, rotation.w);

        public static PoseData ToPoseData(this Transform transform) =>
            ToPoseData(transform.position, transform.rotation);

        public static Vector3 Position(this PoseData pose) => new Vector3(pose.Px, pose.Py, pose.Pz);

        public static Quaternion Rotation(this PoseData pose) => new Quaternion(pose.Rx, pose.Ry, pose.Rz, pose.Rw);

        public static void ApplyTo(this PoseData pose, Transform transform) =>
            transform.SetPositionAndRotation(pose.Position(), pose.Rotation());
    }
}
