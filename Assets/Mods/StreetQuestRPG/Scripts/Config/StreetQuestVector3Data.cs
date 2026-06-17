using System;
using UnityEngine;

namespace StreetQuestRPG
{
    [Serializable]
    internal sealed class StreetQuestVector3Data
    {
        public float x;
        public float y;
        public float z;

        public StreetQuestVector3Data()
        {
        }

        public StreetQuestVector3Data(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public Vector3 ToVector3() => new(x, y, z);

        public static StreetQuestVector3Data From(Vector3 value) => new(value.x, value.y, value.z);
    }
}
