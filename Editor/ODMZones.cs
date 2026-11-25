using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ODM.Editor
{
    [Serializable]
    internal sealed class ZonePreset
    {
        public string zoneName;
        public string encryptedQr;
    }

    [Serializable]
    internal sealed class ZonePresetCollection
    {
        public List<ZonePreset> zones = new List<ZonePreset>();
    }

    /// <summary>Saved zone QR texts. Stored in this Unity user's Editor preferences, never in the project.</summary>
    internal sealed class ZoneStore
    {
        private const string PrefsKey = "ODM.ZonePresets.v1";

        public readonly List<ZonePreset> Items = new List<ZonePreset>();

        /// <summary>Loads saved zones. Returns an error message, or null when everything loaded.</summary>
        public string Load()
        {
            Items.Clear();
            string json = EditorPrefs.GetString(PrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                ZonePresetCollection collection = JsonUtility.FromJson<ZonePresetCollection>(json);
                if (collection != null && collection.zones != null)
                    Items.AddRange(collection.zones.FindAll(zone => zone != null));
                return null;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return $"Could not load saved zones: {exception.Message}";
            }
        }

        public void Save()
        {
            EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(new ZonePresetCollection { zones = Items }));
        }

        public void Add(string zoneName, string encryptedQr)
        {
            Items.Add(new ZonePreset { zoneName = zoneName.Trim(), encryptedQr = encryptedQr.Trim() });
            Save();
        }

        /// <summary>Replaces the saved zones with the ones in the file. Returns how many were imported.</summary>
        public int Import(string path)
        {
            ZonePresetCollection imported = JsonUtility.FromJson<ZonePresetCollection>(File.ReadAllText(path));
            if (imported == null || imported.zones == null)
                throw new InvalidDataException("The selected file does not contain zone presets.");

            Items.Clear();
            Items.AddRange(imported.zones.FindAll(zone => zone != null));
            Save();
            return Items.Count;
        }

        public void Export(string path)
        {
            File.WriteAllText(path, JsonUtility.ToJson(new ZonePresetCollection { zones = new List<ZonePreset>(Items) }, true));
        }
    }

    /// <summary>Feeds a saved QR text into the app's own EditorQRSimulator.</summary>
    internal static class QrSimulator
    {
        public static StatusKind Scan(ZonePreset zone, out string message)
        {
            if (string.IsNullOrWhiteSpace(zone.encryptedQr))
            {
                message = "This zone has no encrypted QR text.";
                return StatusKind.Warn;
            }

            MonoBehaviour simulator = Reflect.FindSceneComponent(AppTypes.QrSimulator);
            if (simulator == null)
            {
                message = "EditorQRSimulator is not present in the loaded scene.";
                return StatusKind.Warn;
            }

            if (!Reflect.SetText(Reflect.Read(simulator, "qrInputField"), zone.encryptedQr))
            {
                message = "Could not find the QR text input on EditorQRSimulator.";
                return StatusKind.Error;
            }

            try
            {
                Reflect.Call(simulator, "SimulateScan");
                message = $"Scanned '{zone.zoneName}' through the app's normal QR flow.";
                return StatusKind.Ok;
            }
            catch (Exception exception)
            {
                message = $"QR simulation failed: {exception.GetBaseException().Message}";
                Debug.LogException(exception);
                return StatusKind.Error;
            }
        }
    }
}
