using System;
using UnityEngine;

[CreateAssetMenu(fileName = "AudioConfig", menuName = "Swallow Diamond/Audio Config")]
public sealed class AudioConfig : ScriptableObject
{
    public AudioEntry[] entries = Array.Empty<AudioEntry>();

    public bool TryGetClip(string soundType, out AudioClip clip)
    {
        clip = null;
        if (string.IsNullOrWhiteSpace(soundType) || entries == null)
        {
            return false;
        }

        foreach (AudioEntry entry in entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.soundType))
            {
                continue;
            }

            if (string.Equals(entry.soundType, soundType, StringComparison.Ordinal))
            {
                clip = entry.clip;
                return clip != null;
            }
        }

        return false;
    }
}

[Serializable]
public sealed class AudioEntry
{
    public string soundType;
    public AudioClip clip;
}
