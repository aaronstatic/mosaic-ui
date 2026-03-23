using System;
using UnityEngine;

namespace Mosaic.UI.Windows
{
    public struct WindowSavedState
    {
        public Vector2 position;
        public Vector2 size;
    }

    public interface IWindowPersistence
    {
        void Save(string key, WindowSavedState state);
        WindowSavedState? Load(string key);
        void Delete(string key);
        void Clear();
    }

    public class PlayerPrefsWindowPersistence : IWindowPersistence
    {
        private const string Prefix = "MosaicUI_Window_";

        public void Save(string key, WindowSavedState state)
        {
            var json = JsonUtility.ToJson(new SerializableState
            {
                px = state.position.x,
                py = state.position.y,
                sx = state.size.x,
                sy = state.size.y
            });
            PlayerPrefs.SetString(Prefix + key, json);
        }

        public WindowSavedState? Load(string key)
        {
            var prefKey = Prefix + key;
            if (!PlayerPrefs.HasKey(prefKey))
                return null;

            try
            {
                var data = JsonUtility.FromJson<SerializableState>(PlayerPrefs.GetString(prefKey));
                return new WindowSavedState
                {
                    position = new Vector2(data.px, data.py),
                    size = new Vector2(data.sx, data.sy)
                };
            }
            catch
            {
                return null;
            }
        }

        public void Delete(string key)
        {
            PlayerPrefs.DeleteKey(Prefix + key);
        }

        public void Clear()
        {
            // PlayerPrefs does not support prefix-based deletion.
            // Individual windows should be cleared via Delete(key).
        }

        [Serializable]
        private struct SerializableState
        {
            public float px, py, sx, sy;
        }
    }
}
