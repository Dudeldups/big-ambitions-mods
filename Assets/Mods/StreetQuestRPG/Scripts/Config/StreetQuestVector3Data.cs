using System;
using System.Runtime.Serialization;
using UnityEngine;

namespace StreetQuestRPG
{
    [Serializable, DataContract]
    internal sealed class StreetQuestVector3Data
    {
        [DataMember] public float x;
        [DataMember] public float y;
        [DataMember] public float z;

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
