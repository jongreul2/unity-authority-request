using System;

namespace Jongreul.AuthorityRequest.Grab
{
    /// <summary>엔진 비의존 위치·회전. Unity 계층에서 Vector3·Quaternion으로 바꾼다.</summary>
    public readonly struct PoseData : IEquatable<PoseData>
    {
        public readonly float Px, Py, Pz;
        public readonly float Rx, Ry, Rz, Rw;

        public PoseData(float px, float py, float pz, float rx = 0, float ry = 0, float rz = 0, float rw = 1)
        {
            Px = px;
            Py = py;
            Pz = pz;
            Rx = rx;
            Ry = ry;
            Rz = rz;
            Rw = rw;
        }

        public static PoseData Identity => new PoseData(0, 0, 0);

        public static PoseData At(float x, float y, float z) => new PoseData(x, y, z);

        /// <summary>위치는 선형, 회전은 짧은 쪽 경로로 정규화 선형 보간.</summary>
        public static PoseData Lerp(PoseData a, PoseData b, float t)
        {
            t = t < 0 ? 0 : t > 1 ? 1 : t;

            float dot = a.Rx * b.Rx + a.Ry * b.Ry + a.Rz * b.Rz + a.Rw * b.Rw;
            float sign = dot < 0 ? -1f : 1f;
            float rx = a.Rx + (b.Rx * sign - a.Rx) * t;
            float ry = a.Ry + (b.Ry * sign - a.Ry) * t;
            float rz = a.Rz + (b.Rz * sign - a.Rz) * t;
            float rw = a.Rw + (b.Rw * sign - a.Rw) * t;
            float length = (float)Math.Sqrt(rx * rx + ry * ry + rz * rz + rw * rw);
            if (length > 1e-6f)
            {
                rx /= length;
                ry /= length;
                rz /= length;
                rw /= length;
            }
            else
            {
                rx = ry = rz = 0;
                rw = 1;
            }

            return new PoseData(
                a.Px + (b.Px - a.Px) * t,
                a.Py + (b.Py - a.Py) * t,
                a.Pz + (b.Pz - a.Pz) * t,
                rx, ry, rz, rw);
        }

        public float DistanceTo(PoseData other)
        {
            float dx = Px - other.Px;
            float dy = Py - other.Py;
            float dz = Pz - other.Pz;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public bool Equals(PoseData other) =>
            Px == other.Px && Py == other.Py && Pz == other.Pz &&
            Rx == other.Rx && Ry == other.Ry && Rz == other.Rz && Rw == other.Rw;

        public override bool Equals(object obj) => obj is PoseData other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Px, Py, Pz, Rx, Ry, Rz, Rw);
        public static bool operator ==(PoseData a, PoseData b) => a.Equals(b);
        public static bool operator !=(PoseData a, PoseData b) => !a.Equals(b);
        public override string ToString() => $"({Px:0.###}, {Py:0.###}, {Pz:0.###})";
    }
}
