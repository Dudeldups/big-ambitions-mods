using UnityEngine;
using UnityEngine.EventSystems;

namespace StreetQuestRPG
{
    internal sealed class StreetQuestMapMarkerHoverTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
    {
        private StreetQuestMapMarkerWatcher _watcher;
        private string _characterId;
        private RectTransform _markerRoot;

        internal void Configure(StreetQuestMapMarkerWatcher watcher, string characterId, RectTransform markerRoot)
        {
            _watcher = watcher;
            _characterId = characterId;
            _markerRoot = markerRoot;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _watcher?.ShowMarkerNameplate(_characterId, _markerRoot);
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            _watcher?.ShowMarkerNameplate(_characterId, _markerRoot);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _watcher?.HideMarkerNameplate(_characterId, _markerRoot);
        }

        private void OnDisable()
        {
            _watcher?.HideMarkerNameplate(_characterId, _markerRoot);
        }
    }
}
