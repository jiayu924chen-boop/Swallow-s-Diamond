using UnityEngine;
using UnityEngine.SceneManagement;

public static class AudioSfx
{
    public const string Start = "start";
    public const string UI = "UI";
    public const string Move = "move";
    public const string Target = "target";
    public const string Win = "win";
    public const string Chapter = "chapter";
}

public sealed class AudioManager : MonoBehaviour
{
    private const string DefaultConfigPath = "Config/AudioConfig";
    private const string DefaultBgmType = "bgm";
    private const string SoundKey = "carpet-setting-sound";
    private const float DefaultBgmVolume = 0.55f;
    private const float DefaultSfxVolume = 1f;

    private static AudioManager instance;

    [SerializeField] private AudioConfig config;
    [SerializeField] private AudioSource bgmAudio;
    [SerializeField] private AudioSource sfxAudio;

    private AudioListener ownedListener;

    public static AudioManager Instance => EnsureExists();

    public static AudioManager EnsureExists()
    {
        if (instance != null)
        {
            return instance;
        }

        AudioManager existing = FindObjectOfType<AudioManager>();
        if (existing != null)
        {
            instance = existing;
            DontDestroyOnLoad(existing.gameObject);
            existing.EnsureSetup();
            existing.PlayDefaultBgmIfIntroScene();
            return existing;
        }

        GameObject host = new GameObject("Audio Manager");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<AudioManager>();
        instance.EnsureSetup();
        instance.PlayDefaultBgmIfIntroScene();
        return instance;
    }

    public static void PlayBgm(string soundType)
    {
        EnsureExists().Play(soundType, true, true);
    }

    public static void PlaySfx(string soundType)
    {
        EnsureExists().Play(soundType, false, false);
    }

    public static void StopBgm()
    {
        AudioManager manager = EnsureExists();
        if (manager.bgmAudio != null)
        {
            manager.bgmAudio.Stop();
        }
    }

    public static void StopSfx()
    {
        AudioManager manager = EnsureExists();
        if (manager.sfxAudio != null)
        {
            manager.sfxAudio.Stop();
        }
    }

    public static void ApplySavedSetting()
    {
        EnsureExists().ApplyVolumeSetting();
    }

    public static void RestartDefaultBgm()
    {
        AudioManager manager = EnsureExists();
        manager.Play(DefaultBgmType, true, true);
        if (manager.bgmAudio != null && manager.bgmAudio.clip != null)
        {
            manager.bgmAudio.time = 0f;
        }
    }

    public static void EnsureDefaultBgmPlaying()
    {
        AudioManager manager = EnsureExists();
        if (manager.bgmAudio == null || manager.bgmAudio.isPlaying)
        {
            return;
        }

        manager.Play(DefaultBgmType, true, true);
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
        EnsureSetup();
        PlayDefaultBgmIfIntroScene();
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
            instance.EnsureSetup();
            instance.PlayDefaultBgmIfIntroScene();
        }
    }

    private void Play(string soundType, bool isBgm, bool loop)
    {
        EnsureSetup();
        if (!TryGetClip(soundType, out AudioClip clip))
        {
            Debug.LogWarning("Audio clip not found for type: " + soundType);
            return;
        }

        AudioSource target = isBgm ? bgmAudio : sfxAudio;
        if (target == null)
        {
            return;
        }

        if (!isBgm)
        {
            target.loop = false;
            target.PlayOneShot(clip);
            return;
        }

        target.Stop();
        target.clip = clip;
        target.loop = loop;
        target.time = 0f;
        target.Play();
    }

    private bool TryGetClip(string soundType, out AudioClip clip)
    {
        if (config != null && config.TryGetClip(soundType, out clip))
        {
            return true;
        }

        clip = string.IsNullOrWhiteSpace(soundType) ? null : Resources.Load<AudioClip>("Audio/" + soundType);
        return clip != null;
    }

    private void EnsureSetup()
    {
        if (config == null)
        {
            config = Resources.Load<AudioConfig>(DefaultConfigPath);
        }

        AudioSource[] sources = GetComponents<AudioSource>();
        if (bgmAudio == null)
        {
            bgmAudio = sources.Length > 0 ? sources[0] : gameObject.AddComponent<AudioSource>();
        }

        sources = GetComponents<AudioSource>();
        if (sfxAudio == null)
        {
            sfxAudio = sources.Length > 1 ? sources[1] : gameObject.AddComponent<AudioSource>();
        }

        ConfigureSource(bgmAudio, true);
        ConfigureSource(sfxAudio, false);
        SyncAudioListener();
        ApplyVolumeSetting();
    }

    private static void ConfigureSource(AudioSource source, bool loop)
    {
        if (source == null)
        {
            return;
        }

        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;
        source.priority = 128;
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
        bool soundEnabled = PlayerPrefs.GetInt(SoundKey, 1) != 0;

        if (bgmAudio != null)
        {
            bgmAudio.volume = soundEnabled ? DefaultBgmVolume : 0f;
            bgmAudio.mute = !soundEnabled;
        }

        if (sfxAudio != null)
        {
            sfxAudio.volume = soundEnabled ? DefaultSfxVolume : 0f;
            sfxAudio.mute = !soundEnabled;
        }
    }

    private void PlayDefaultBgmIfIntroScene()
    {
        if (SceneManager.GetActiveScene().name == CarpetLevelFlow.IntroSceneName)
        {
            EnsureDefaultBgmPlaying();
        }
    }
}
