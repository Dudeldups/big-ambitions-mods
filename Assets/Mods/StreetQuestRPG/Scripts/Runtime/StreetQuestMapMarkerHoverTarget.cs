using UnityEngine;
using UnityEngine.EventSystems;

namespace StreetQuestRPG
{
    internal sealed class StreetQuestMapMarkerHoverTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        internal StreetQuestMapMarkerWatcher Owner;
        internal string CharacterId;

        public void OnPointerEnter(PointerEventData eventData)
        {
            Owner?.HandleMarkerPointerEnter(CharacterId);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Owner?.HandleMarkerPointerExit(CharacterId);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Owner?.HandleMarkerPointerClick(CharacterId);
        }
    }
}
