#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FreelancePhotographer
{
    internal static class PhotographySaveService
    {
        private static object? cachedSave;
        private static PhotographySaveState? cachedState;

        internal static PhotographySaveState? Load()
        {
            var save = SaveGameManager.Current;
            if (save == null)
            {
                ResetCache();
                return null;
            }

            if (ReferenceEquals(cachedSave, save) && cachedState != null)
                return cachedState;

            var state = new PhotographySaveState();
            if (save.modData != null &&
                save.modData.TryGetValue(FreelancePhotographerIds.SaveKey, out var serialized) &&
                !string.IsNullOrWhiteSpace(serialized))
            {
                try
                {
                    state = JsonUtility.FromJson<PhotographySaveState>(serialized) ?? state;
                }
                catch (Exception)
                {
                    state = new PhotographySaveState();
                }
            }

            state.Normalize();
            cachedSave = save;
            cachedState = state;
            return state;
        }

        internal static void Save(PhotographySaveState state)
        {
            var save = SaveGameManager.Current;
            if (save == null || state == null)
                return;

            state.Normalize();
            save.modData ??= new Dictionary<string, string>();
            save.modData[FreelancePhotographerIds.SaveKey] = JsonUtility.ToJson(state);
            save.hasEverUsedMods = true;
            cachedSave = save;
            cachedState = state;
            SaveGameManager.MarkChange();
        }

        internal static double CurrentGameHours()
        {
            var save = SaveGameManager.Current;
            return save == null ? 0d : save.Day * 24d + save.Hour + save.Minute / 60d;
        }

        internal static void ResetCache()
        {
            cachedSave = null;
            cachedState = null;
        }
    }
}
