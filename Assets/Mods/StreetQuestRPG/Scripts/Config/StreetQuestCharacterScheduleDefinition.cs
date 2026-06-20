using System;
using System.Runtime.Serialization;

namespace StreetQuestRPG
{
#pragma warning disable CS0649
    [Serializable, DataContract]
    internal sealed class StreetQuestCharacterScheduleDefinition
    {
        [DataMember] public string mode;
        [DataMember] public int startHour;
        [DataMember] public int endHour;
        [DataMember] public string address;
        [DataMember] public float nearestBuildingMaxDistance = 40f;

        public StreetQuestCharacterScheduleMode Mode =>
            Enum.TryParse(mode, true, out StreetQuestCharacterScheduleMode parsedMode)
                ? parsedMode
                : StreetQuestCharacterScheduleMode.Always;
    }
#pragma warning restore CS0649
}
