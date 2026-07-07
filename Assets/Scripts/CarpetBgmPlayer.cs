using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CarpetBgmPlayer : MonoBehaviour
{
    private const string ResourcePath = "Audio/perfect_beauty_bgm";
    private const string SoundKey = "carpet-setting-sound";
    private const float DefaultVolume = 0.55f;

    private static CarpetBgmPlayer instance;

    private AudioSource audioSource;
    private AudioListener ownedListener;
    private bool loggedLoadedClip;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        EnsureExists();
    }

    public static void ApplySavedSetting()
    {
        CarpetBgmPlayer player = EnsureExists();
        player.ApplyVolumeSetting();
    }

    public static void EnsurePlaying()
    {
        CarpetBgmPlayer player = EnsureExists();
        player.EnsureAudioSource();
    }

    public static void RestartFromBeginning()
    {
        CarpetBgmPlayer player = EnsureExists();
        player.EnsureAudioSource();
        if (player.audioSource == null || player.audioSource.clip == null)
        {
            return;
        }

        player.audioSource.Stop();
        player.audioSource.time = 0f;
        player.audioSource.Play();
    }

    private static CarpetBgmPlayer EnsureExists()
    {
        if (instance != null)
        {
            return instance;
        }

        CarpetBgmPlayer existing = FindObjectOfType<CarpetBgmPlayer>();
        if (existing != null)
        {
            instance = existing;
            DontDestroyOnLoad(existing.gameObject);
            existing.EnsureAudioSource();
            return existing;
        }

        GameObject host = new GameObject("Carpet BGM Player");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<CarpetBgmPlayer>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        EnsureAudioSource();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            instance = null;
        }
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (instance != null)
        {
            instance.EnsureAudioSource();
            instance.SyncAudioListener();
        }
    }

    private void EnsureAudioSource()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        if (audioSource.clip == null)
        {
            audioSource.clip = Resources.Load<AudioClip>(ResourcePath);
        }

        audioSource.playOnAwake = false;
        audioSource.loop = true;
        audioSource.spatialBlend = 0f;
        audioSource.priority = 128;
        SyncAudioListener();
        ApplyVolumeSetting();

        if (audioSource.clip != null && !audioSource.isPlaying)
        {
            audioSource.Play();
        }

        if (audioSource.clip != null && !loggedLoadedClip)
        {
            loggedLoadedClip = true;
            Debug.Log("BGM loaded and looping: " + ResourcePath);
        }
        if (audioSource.clip == null)
        {
            Debug.LogWarning("BGM resource not found: " + ResourcePath);
        }
    }

    private void SyncAudioListener()
    {
        AudioListener[] listeners = FindObjectsOfType<AudioListener>();
        bool hasExternalListener = false;
        foreach (AudioListener listener in listeners)
        {
            if (listener != null && listener != ownedListener && listener.enabled)
            {
                hasExternalListener = true;
                break;
            }
        }

        if (ownedListener == null)
        {
            ownedListener = GetComponent<AudioListener>();
            if (ownedListener == null)
            {
                ownedListener = gameObject.AddComponent<AudioListener>();
            }
        }

        ownedListener.enabled = !hasExternalListener;
    }

    private void ApplyVolumeSetting()
    {
        if (audioSource == null)
        {
            return;
        }

        bool soundEnabled = PlayerPrefs.GetInt(SoundKey, 1) != 0;
        audioSource.volume = soundEnabled ? DefaultVolume : 0f;
        audioSource.mute = !soundEnabled;
    }
}
