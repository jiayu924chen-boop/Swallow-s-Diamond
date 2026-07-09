using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public interface ICarpetMenuBackgroundAnimation
{
    void BuildAnimation(RectTransform layer, CarpetLevelMenu.MenuAnimationConfig config);
}

public sealed class CarpetLevelMenu : MonoBehaviour
{
    private const string ConfigPath = "Menu/menu_config.json";
    private const string SettingsButtonIconPath = "Menu/icon_settings_gear.png";
    private const string ProgressKeyPrefix = "carpet-menu-progress-";
    private const string ChapterStateKeyPrefix = "carpet-menu-chapter-state-";
    private const string ChapterTransitionKeyPrefix = "carpet-menu-chapter-transition-";
    private const string FirstChapterUnlockedKey = "carpet-menu-first-chapter-unlocked";
    private const string SoundKey = "carpet-setting-sound";
    private const string SfxKey = "carpet-setting-sfx";
    private const string VibrationKey = "carpet-setting-vibration";
    private const int ChapterButtonCount = 5;
    private const int ChapterStateLocked = 0;
    private const int ChapterStateUnlocked = 1;
    private const int ChapterStateFinished = 2;
    private const int ChapterTransitionNone = 0;
    private const int ChapterTransitionLockToUnlock = 1;
    private const int ChapterTransitionUnlockToFinish = 2;
    private static readonly int[] buttonProgress = new int[ChapterButtonCount];
    private static readonly int[] chapterUnlockStates = new int[ChapterButtonCount];
    private static CarpetLevelMenu instance;

    [Header("Background")]
    public Sprite backgroundSprite;
    public Color backgroundColor = new Color(0.92f, 0.88f, 0.78f, 1f);
    public MonoBehaviour[] backgroundAnimationHooks = Array.Empty<MonoBehaviour>();
    private Sprite settingsButtonSprite;

    [Header("Buttons")]
    [SerializeField] private Button[] chapterButtons = Array.Empty<Button>();
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button sfxButton;
    [SerializeField] private Button soundButton;
    [SerializeField] private Button vibrationButton;
    [SerializeField] private Button resetButton;

    public MenuButtonConfig[] buttonConfigs =
    {
        new MenuButtonConfig { label = "\u7ae0\u8282\u4e00", levels = new[] { 1, 2 } },
        new MenuButtonConfig { label = "\u7ae0\u8282\u4e8c", levels = new[] { 3, 4 } },
        new MenuButtonConfig { label = "\u7ae0\u8282\u4e09", levels = new[] { 5, 6 } },
        new MenuButtonConfig { label = "\u7ae0\u8282\u56db", levels = new[] { 7 } },
        new MenuButtonConfig { label = "\u7ae0\u8282\u4e94", levels = new[] { 10 } }
    };

    [Header("Existing Scene Bindings")]
    [SerializeField] private RectTransform root;
    [SerializeField] private RectTransform buttonLayer;
    [SerializeField] private RectTransform decorationLayer;
    [SerializeField] private RectTransform animationLayer;
    [SerializeField] private RectTransform settingsPanel;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image coverBackgroundImage;
    [SerializeField] private Image mainCharacterImage;
    [SerializeField] private Image workbenchImage;

    private Font uiFont;
    private MenuDecorationConfig[] decorationConfigs = Array.Empty<MenuDecorationConfig>();
    private MenuAnimationConfig[] animationConfigs = Array.Empty<MenuAnimationConfig>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneLoaded()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == CarpetLevelFlow.MenuSceneName)
        {
            EnsureMenuExists();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (SceneManager.GetActiveScene().name != CarpetLevelFlow.MenuSceneName)
        {
            return;
        }
        EnsureMenuExists();
    }

    private static void EnsureMenuExists()
    {
        if (FindObjectOfType<CarpetLevelMenu>() != null)
        {
            return;
        }
        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(eventSystem);
        }

        GameObject prefab = Resources.Load<GameObject>("Prefabs/CarpetLevelMenu");
        if (prefab == null)
        {
            prefab = Resources.Load<GameObject>("Prefabs/Carpet Level Menu");
        }
        if (prefab != null)
        {
            Instantiate(prefab);
            return;
        }

        Debug.LogWarning("CarpetLevelMenu scene object is missing. Add Resources/Prefabs/CarpetLevelMenu to the menu scene.");
    }

    public static void AdvanceButtonProgress(int buttonIndex)
    {
        if (buttonIndex >= 0 && buttonIndex < buttonProgress.Length)
        {
            buttonProgress[buttonIndex]++;
            SaveProgress();
            RefreshChapterUnlockStates(true);
        }
    }

    public static void ResetSavedProgress()
    {
        for (int i = 0; i < buttonProgress.Length; i++)
        {
            buttonProgress[i] = 0;
            PlayerPrefs.DeleteKey(ProgressKeyPrefix + i);
            chapterUnlockStates[i] = ChapterStateLocked;
            PlayerPrefs.SetInt(ChapterStateKeyPrefix + i, ChapterStateLocked);
            PlayerPrefs.DeleteKey(ChapterTransitionKeyPrefix + i);
        }
        PlayerPrefs.DeleteKey(FirstChapterUnlockedKey);
        PlayerPrefs.Save();
    }

    public static bool HasStartedGameProgress()
    {
        if (PlayerPrefs.GetInt(FirstChapterUnlockedKey, 0) != 0)
        {
            return true;
        }

        for (int i = 0; i < buttonProgress.Length; i++)
        {
            if (PlayerPrefs.GetInt(ProgressKeyPrefix + i, 0) > 0)
            {
                return true;
            }

            if (PlayerPrefs.GetInt(ChapterStateKeyPrefix + i, ChapterStateLocked) != ChapterStateLocked)
            {
                return true;
            }
        }

        return false;
    }

    public static void RefreshMenuState()
    {
        if (instance == null)
        {
            return;
        }

        LoadProgress();
        instance.BindChapterButtons();
    }

    private void Awake()
    {
        instance = this;
        CarpetBgmPlayer.EnsurePlaying();
        uiFont = LoadUiFont();
        ApplyJsonConfig();
        settingsButtonSprite = LoadSpriteResource(SettingsButtonIconPath);
        LoadProgress();
        ApplySoundSetting();
        BindExistingUi();
    }

    private void Start()
    {
        if (!CarpetLevelFlow.TryConsumePendingMenuGuide(out GuideTextType guideType))
        {
            return;
        }

        GuideLayerController guideLayer = GetComponentInChildren<GuideLayerController>(true);
        if (guideLayer == null)
        {
            Debug.LogWarning("CarpetLevelMenu guide layer is missing.");
            return;
        }

        guideLayer.GuideCompleted -= HandleGuideCompleted;
        guideLayer.GuideCompleted += HandleGuideCompleted;
        guideLayer.StartGuide(guideType);
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }

        GuideLayerController guideLayer = GetComponentInChildren<GuideLayerController>(true);
        if (guideLayer != null)
        {
            guideLayer.GuideCompleted -= HandleGuideCompleted;
        }
    }

    private void ApplyJsonConfig()
    {
        string path = Path.Combine(Application.streamingAssetsPath, ConfigPath);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            MenuConfig config = JsonUtility.FromJson<MenuConfig>(File.ReadAllText(path));
            if (config == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(config.backgroundColor) && ColorUtility.TryParseHtmlString(config.backgroundColor, out Color parsed))
            {
                backgroundColor = parsed;
            }
            if (!string.IsNullOrEmpty(config.backgroundImage))
            {
                LoadBackgroundSprite(config.backgroundImage);
            }
            if (config.buttons != null && config.buttons.Length > 0)
            {
                buttonConfigs = config.buttons.Take(ChapterButtonCount).ToArray();
            }
            decorationConfigs = config.decorations ?? Array.Empty<MenuDecorationConfig>();
            animationConfigs = config.animations ?? Array.Empty<MenuAnimationConfig>();
        }
        catch (Exception error)
        {
            Debug.LogWarning("Failed to read menu config: " + error.Message);
        }
    }

    private void LoadBackgroundSprite(string relativePath)
    {
        backgroundSprite = LoadSpriteResource(relativePath);
        if (backgroundSprite == null)
        {
            Debug.LogWarning("Menu background resource not found: " + Path.ChangeExtension(relativePath.Replace('\\', '/'), null));
        }
    }

    private static Sprite LoadSpriteResource(string relativePath)
    {
        string resourcePath = Path.ChangeExtension(relativePath.Replace('\\', '/'), null);
        Sprite sprite = Resources.Load<Sprite>(resourcePath);
        if (sprite != null)
        {
            return sprite;
        }

        Texture2D texture = Resources.Load<Texture2D>(resourcePath);
        if (texture != null)
        {
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }

        return null;
    }

    private void BindExistingUi()
    {
        EnsureEventSystemExists();

        root = root != null ? root : FindRect("Root");
        buttonLayer = buttonLayer != null ? buttonLayer : FindRect("ButtonLayer");
        decorationLayer = decorationLayer != null ? decorationLayer : FindRect("DecorationLayer");
        animationLayer = animationLayer != null ? animationLayer : FindRect("AnimationLayer");
        settingsPanel = settingsPanel != null ? settingsPanel : FindRect("SettingsPanel");
        backgroundImage = backgroundImage != null ? backgroundImage : FindImage("Root");
        coverBackgroundImage = coverBackgroundImage != null ? coverBackgroundImage : FindImage("MenuBackgroundImage");
        mainCharacterImage = mainCharacterImage != null ? mainCharacterImage : FindImage("MainCharacter");
        workbenchImage = workbenchImage != null ? workbenchImage : FindImage("workbench");

        ApplyExistingBackground();
        BindChapterButtons();
        BindSettingsControls();
        BindExistingDecorations();
        BindExistingAnimations();
    }

    private static void EnsureEventSystemExists()
    {
        if (FindObjectOfType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        DontDestroyOnLoad(eventSystem);
    }

    private void ApplyExistingBackground()
    {
        if (backgroundImage != null)
        {
            backgroundImage.color = backgroundColor;
            backgroundImage.raycastTarget = false;
        }
        if (coverBackgroundImage != null && backgroundSprite != null)
        {
            coverBackgroundImage.sprite = backgroundSprite;
            coverBackgroundImage.color = Color.white;
            coverBackgroundImage.preserveAspect = true;
            coverBackgroundImage.raycastTarget = false;
        }
    }

    private void BindChapterButtons()
    {
        Button[] resolvedButtons = ResolveChapterButtons();
        chapterButtons = resolvedButtons;

        for (int i = 0; i < ChapterButtonCount; i++)
        {
            if (i >= resolvedButtons.Length || resolvedButtons[i] == null)
            {
                Debug.LogWarning("CarpetLevelMenu chapter button is missing: " + (i + 1));
                continue;
            }

            int index = i;
            MenuButtonConfig config = GetButtonConfig(index);
            ChapterState chapterState = GetChapterState(index);
            Button button = resolvedButtons[index];
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => StartConfiguredLevel(index));
            button.interactable = chapterState == ChapterState.Open;
            ApplyChapterButtonState(button, index, chapterState);

            Text text = button.GetComponentInChildren<Text>(true);
            if (text != null)
            {
                text.font = uiFont;
                text.text = BuildChapterLabel(config, chapterState);
            }
        }
    }

    private void HandleGuideCompleted(GuideTextType guideType)
    {
        if (guideType != GuideTextType.StartGame)
        {
            return;
        }

        UnlockFirstChapter();
        BindChapterButtons();
    }

    private static void ApplyChapterButtonState(Button button, int index, ChapterState chapterState)
    {
        if (button == null)
        {
            return;
        }

        ChapterButtonStateController stateController = button.GetComponent<ChapterButtonStateController>();
        if (stateController == null)
        {
            return;
        }

        int transition = ConsumePendingChapterTransition(index);
        switch (chapterState)
        {
            case ChapterState.Open:
                if (transition == ChapterTransitionLockToUnlock)
                {
                    stateController.PlayLockToUnlock();
                }
                else
                {
                    stateController.SetUnlockState();
                }
                break;
            case ChapterState.Completed:
                if (transition == ChapterTransitionUnlockToFinish)
                {
                    stateController.PlayUnlockToFinish();
                }
                else
                {
                    stateController.SetFinishState();
                }
                break;
            default:
                stateController.SetLockState();
                break;
        }
    }

    private Button[] ResolveChapterButtons()
    {
        Button[] resolved = new Button[ChapterButtonCount];
        if (chapterButtons != null)
        {
            for (int i = 0; i < Mathf.Min(chapterButtons.Length, resolved.Length); i++)
            {
                resolved[i] = chapterButtons[i];
            }
        }

        Button[] candidates = (buttonLayer != null ? buttonLayer : transform).GetComponentsInChildren<Button>(true)
            .Where(button => button != null && IsChapterButtonName(button.name))
            .OrderByDescending(button => ((RectTransform)button.transform).anchoredPosition.y)
            .ToArray();

        for (int i = 0; i < resolved.Length && i < candidates.Length; i++)
        {
            if (resolved[i] == null)
            {
                resolved[i] = candidates[i];
            }
        }
        return resolved;
    }

    private static bool IsChapterButtonName(string value)
    {
        return !string.IsNullOrEmpty(value) && value.StartsWith("Button ", StringComparison.OrdinalIgnoreCase);
    }

    private void BindSettingsControls()
    {
        settingsButton = settingsButton != null ? settingsButton : FindButton("ImageButton Settings", "Settings");
        sfxButton = sfxButton != null ? sfxButton : FindButton("Toggle 音效");
        soundButton = soundButton != null ? soundButton : FindButton("Toggle 声音");
        vibrationButton = vibrationButton != null ? vibrationButton : FindButton("Toggle 震动");
        resetButton = resetButton != null ? resetButton : FindButton("Button 重置游戏");

        if (settingsButton != null)
        {
            if (settingsButtonSprite != null)
            {
                Image image = settingsButton.targetGraphic as Image ?? settingsButton.GetComponent<Image>();
                if (image != null)
                {
                    image.sprite = settingsButtonSprite;
                    image.preserveAspect = true;
                }
            }
            settingsButton.onClick.RemoveAllListeners();
            settingsButton.onClick.AddListener(ToggleSettingsPanel);
        }

        BindSettingButton(sfxButton, "音效", SfxKey, true, null);
        BindSettingButton(soundButton, "声音", SoundKey, true, ApplySoundSetting);
        BindSettingButton(vibrationButton, "震动", VibrationKey, true, null);

        if (resetButton != null)
        {
            resetButton.onClick.RemoveAllListeners();
            resetButton.onClick.AddListener(ResetAllProgress);
            Text text = resetButton.GetComponentInChildren<Text>(true);
            if (text != null)
            {
                text.font = uiFont;
                text.text = "重置游戏";
            }
        }

        if (settingsPanel != null)
        {
            settingsPanel.gameObject.SetActive(false);
        }
    }

    private void BindSettingButton(Button button, string label, string key, bool defaultValue, Action onChanged)
    {
        if (button == null)
        {
            Debug.LogWarning("CarpetLevelMenu setting button is missing: " + label);
            return;
        }

        Image image = button.targetGraphic as Image ?? button.GetComponent<Image>();
        Text text = button.GetComponentInChildren<Text>(true);
        if (text != null)
        {
            text.font = uiFont;
        }

        Action refresh = () =>
        {
            bool enabled = GetBoolSetting(key, defaultValue);
            if (image != null)
            {
                image.color = enabled ? new Color(0.18f, 0.48f, 0.42f, 0.96f) : new Color(0.38f, 0.38f, 0.38f, 0.96f);
            }
            if (text != null)
            {
                text.text = label + (enabled ? " 开" : " 关");
            }
        };

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() =>
        {
            SetBoolSetting(key, !GetBoolSetting(key, defaultValue));
            refresh();
            onChanged?.Invoke();
        });
        refresh();
    }

    private void BindExistingDecorations()
    {
        foreach (MenuDecorationConfig config in decorationConfigs)
        {
            if (config == null || string.IsNullOrEmpty(config.image))
            {
                continue;
            }

            Image image = ResolveDecorationImage(config);
            if (image == null)
            {
                Debug.LogWarning("CarpetLevelMenu decoration object is missing: " + config.id);
                continue;
            }

            Sprite sprite = LoadSpriteResource(config.image);
            if (sprite != null)
            {
                image.sprite = sprite;
            }
            image.color = ParseColor(config.color, Color.white);
            image.preserveAspect = true;
            image.raycastTarget = false;

            BindDecorationShadow(config, sprite);
            BuildDecorationFrameAnimation(image.rectTransform, image, config);
        }
    }

    private void BindDecorationShadow(MenuDecorationConfig config, Sprite sprite)
    {
        if (!config.shadow)
        {
            return;
        }

        Image shadowImage = FindImage("MenuDecorationShadow " + config.id);
        if (shadowImage == null)
        {
            return;
        }
        if (sprite != null)
        {
            shadowImage.sprite = sprite;
        }
        shadowImage.color = ParseColor(config.shadowColor, new Color(0.10f, 0.07f, 0.08f, 0.40f));
        shadowImage.raycastTarget = false;
    }

    private Image ResolveDecorationImage(MenuDecorationConfig config)
    {
        if (config.id.IndexOf("character", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return mainCharacterImage != null ? mainCharacterImage : FindImage("MainCharacter");
        }
        if (config.id.IndexOf("workbench", StringComparison.OrdinalIgnoreCase) >= 0 || config.id.IndexOf("bench", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return workbenchImage != null ? workbenchImage : FindImage("workbench");
        }

        return FindImage(config.id) ?? FindImage("MenuDecoration " + config.id);
    }

    private void BindExistingAnimations()
    {
        if (animationLayer == null)
        {
            return;
        }

        foreach (MonoBehaviour hook in backgroundAnimationHooks)
        {
            if (hook is ICarpetMenuBackgroundAnimation animationHook)
            {
                foreach (MenuAnimationConfig config in animationConfigs)
                {
                    animationHook.BuildAnimation(animationLayer, config);
                }
            }
        }
    }

    private RectTransform FindRect(params string[] names)
    {
        Transform found = FindChildRecursive(transform, names);
        return found as RectTransform;
    }

    private Image FindImage(params string[] names)
    {
        Transform found = FindChildRecursive(transform, names);
        return found != null ? found.GetComponent<Image>() : null;
    }

    private Button FindButton(params string[] names)
    {
        Transform found = FindChildRecursive(transform, names);
        return found != null ? found.GetComponent<Button>() : null;
    }

    private static Transform FindChildRecursive(Transform parent, params string[] names)
    {
        if (parent == null || names == null || names.Length == 0)
        {
            return null;
        }

        foreach (Transform child in parent)
        {
            foreach (string name in names)
            {
                if (string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }

            Transform found = FindChildRecursive(child, names);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    private void BuildUi()
    {
        GameObject canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform root = AddRect("Root", canvasObject.transform);
        Stretch(root);
        Image background = root.gameObject.AddComponent<Image>();
        background.color = backgroundColor;
        background.raycastTarget = false;
        AddCoverBackground(root);

        RectTransform decorationLayer = AddRect("DecorationLayer", root);
        Stretch(decorationLayer);
        BuildDecorations(decorationLayer);

        RectTransform animationLayer = AddRect("AnimationLayer", root);
        Stretch(animationLayer);
        BuildBackgroundAnimations(animationLayer);

        RectTransform buttonLayer = AddRect("ButtonLayer", root);
        Stretch(buttonLayer);

        for (int i = 0; i < ChapterButtonCount; i++)
        {
            int index = i;
            MenuButtonConfig config = GetButtonConfig(index);
            ChapterState chapterState = GetChapterState(index);
            Button button = AddButton(buttonLayer, BuildChapterLabel(config, chapterState), ChapterButtonColor(chapterState), () => StartConfiguredLevel(index));
            button.interactable = chapterState == ChapterState.Open;

            RectTransform buttonRect = button.transform as RectTransform;
            buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            Vector2 buttonSize = ResolveButtonSize(config);
            buttonRect.sizeDelta = buttonSize;
            buttonRect.anchoredPosition = ResolveButtonPosition(config, index, buttonSize);
            buttonRect.localEulerAngles = new Vector3(0f, 0f, config.rotation);
        }

        Button settingsButton = AddImageButton(root, "Settings", settingsButtonSprite, ToggleSettingsPanel);
        RectTransform settingsButtonRect = settingsButton.transform as RectTransform;
        settingsButtonRect.anchorMin = new Vector2(1f, 1f);
        settingsButtonRect.anchorMax = new Vector2(1f, 1f);
        settingsButtonRect.pivot = new Vector2(1f, 1f);
        settingsButtonRect.anchoredPosition = new Vector2(-36, -36);
        settingsButtonRect.sizeDelta = new Vector2(92, 92);

        BuildSettingsPanel(root);
    }

    private void AddCoverBackground(RectTransform root)
    {
        if (backgroundSprite == null)
        {
            return;
        }

        RectTransform backgroundRect = AddRect("MenuBackgroundImage", root);
        backgroundRect.anchorMin = new Vector2(0.5f, 0.5f);
        backgroundRect.anchorMax = new Vector2(0.5f, 0.5f);
        backgroundRect.pivot = new Vector2(0.5f, 0.5f);
        backgroundRect.anchoredPosition = Vector2.zero;

        float spriteAspect = backgroundSprite.rect.width / Mathf.Max(1f, backgroundSprite.rect.height);
        float screenAspect = 1080f / 1920f;
        float width = 1080f;
        float height = 1920f;
        if (spriteAspect > screenAspect)
        {
            width = height * spriteAspect;
        }
        else
        {
            height = width / Mathf.Max(0.01f, spriteAspect);
        }
        backgroundRect.sizeDelta = new Vector2(width, height);

        Image backgroundImage = backgroundRect.gameObject.AddComponent<Image>();
        backgroundImage.sprite = backgroundSprite;
        backgroundImage.color = Color.white;
        backgroundImage.preserveAspect = true;
        backgroundImage.raycastTarget = false;
    }

    private void BuildDecorations(RectTransform layer)
    {
        foreach (MenuDecorationConfig config in decorationConfigs)
        {
            if (config == null || string.IsNullOrEmpty(config.image))
            {
                continue;
            }

            Sprite sprite = LoadSpriteResource(config.image);
            if (sprite == null)
            {
                Debug.LogWarning("Menu decoration resource not found: " + Path.ChangeExtension(config.image.Replace('\\', '/'), null));
                continue;
            }

            Vector2 size = ResolveDecorationSize(config, sprite);
            if (config.shadow)
            {
                RectTransform shadow = AddRect("MenuDecorationShadow " + config.id, layer);
                ConfigureDecorationRect(shadow, config.position + config.shadowOffset, ScaleDecorationSize(size, config.shadowScale), config.rotation);

                Image shadowImage = shadow.gameObject.AddComponent<Image>();
                shadowImage.sprite = sprite;
                shadowImage.color = ParseColor(config.shadowColor, new Color(0.10f, 0.07f, 0.08f, 0.40f));
                shadowImage.preserveAspect = false;
                shadowImage.raycastTarget = false;
            }

            RectTransform item = AddRect("MenuDecoration " + config.id, layer);
            ConfigureDecorationRect(item, config.position, size, config.rotation);

            Image image = item.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = ParseColor(config.color, Color.white);
            image.preserveAspect = true;
            image.raycastTarget = false;

            BuildDecorationFrameAnimation(item, image, config);
        }
    }

    private void BuildDecorationFrameAnimation(RectTransform item, Image image, MenuDecorationConfig config)
    {
        if (config.triggerAnimationFolders != null && config.triggerAnimationFolders.Length > 0)
        {
            List<Sprite[]> sequences = new List<Sprite[]>();
            foreach (string folder in config.triggerAnimationFolders)
            {
                Sprite[] sequence = LoadSpriteSequenceFolder(folder);
                if (sequence.Length > 0)
                {
                    sequences.Add(sequence);
                }
                else
                {
                    Debug.LogWarning("Menu decoration trigger animation folder has no frames: " + folder);
                }
            }

            if (sequences.Count > 0)
            {
                MenuDecorationTriggeredFrameAnimator triggeredAnimator = item.GetComponent<MenuDecorationTriggeredFrameAnimator>();
                if (triggeredAnimator == null)
                {
                    triggeredAnimator = item.gameObject.AddComponent<MenuDecorationTriggeredFrameAnimator>();
                }
                triggeredAnimator.Init(
                    item,
                    image,
                    image.sprite,
                    sequences.ToArray(),
                    item.anchoredPosition,
                    config.triggerIntervalSeconds,
                    config.triggerFrameSeconds,
                    config.breathAmplitude,
                    config.breathScale,
                    config.breathSeconds,
                    config.frameFadeSeconds);
                return;
            }
        }

        if (config.animationFrames == null || config.animationFrames.Length == 0)
        {
            return;
        }

        List<Sprite> frames = new List<Sprite>();
        foreach (string framePath in config.animationFrames)
        {
            Sprite frame = LoadSpriteResource(framePath);
            if (frame != null)
            {
                frames.Add(frame);
            }
            else
            {
                Debug.LogWarning("Menu decoration animation frame not found: " + Path.ChangeExtension(framePath.Replace('\\', '/'), null));
            }
        }

        if (frames.Count == 0)
        {
            return;
        }

        MenuDecorationFrameAnimator frameAnimator = item.GetComponent<MenuDecorationFrameAnimator>();
        if (frameAnimator == null)
        {
            frameAnimator = item.gameObject.AddComponent<MenuDecorationFrameAnimator>();
        }
        frameAnimator.Init(
            item,
            image,
            frames.ToArray(),
            config.animationDurations,
            item.anchoredPosition,
            config.breathAmplitude,
            config.breathScale,
            config.breathSeconds,
            config.frameFadeSeconds);
    }

    private static Sprite[] LoadSpriteSequenceFolder(string relativeFolder)
    {
        if (string.IsNullOrEmpty(relativeFolder))
        {
            return Array.Empty<Sprite>();
        }

        string resourcePath = Path.ChangeExtension(relativeFolder.Replace('\\', '/'), null).TrimEnd('/');
        Sprite[] sprites = Resources.LoadAll<Sprite>(resourcePath);
        if (sprites != null && sprites.Length > 0)
        {
            return sprites
                .OrderBy(sprite => ParseNumericSortKey(sprite.name))
                .ThenBy(sprite => sprite.name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        Texture2D[] textures = Resources.LoadAll<Texture2D>(resourcePath);
        if (textures == null || textures.Length == 0)
        {
            return Array.Empty<Sprite>();
        }

        return textures
            .OrderBy(texture => ParseNumericSortKey(texture.name))
            .ThenBy(texture => texture.name, StringComparer.OrdinalIgnoreCase)
            .Select(texture => Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f)))
            .Where(sprite => sprite != null)
            .ToArray();
    }

    private static int ParseNumericSortKey(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return int.MaxValue;
        }

        int end = value.Length - 1;
        while (end >= 0 && !char.IsDigit(value[end]))
        {
            end--;
        }
        if (end < 0)
        {
            return int.MaxValue;
        }

        int start = end;
        while (start >= 0 && char.IsDigit(value[start]))
        {
            start--;
        }
        return int.TryParse(value.Substring(start + 1, end - start), out int number) ? number : int.MaxValue;
    }

    private static void ConfigureDecorationRect(RectTransform item, Vector2 position, Vector2 size, float rotation)
    {
        item.anchorMin = new Vector2(0.5f, 0.5f);
        item.anchorMax = new Vector2(0.5f, 0.5f);
        item.pivot = new Vector2(0.5f, 0.5f);
        item.anchoredPosition = position;
        item.sizeDelta = size;
        item.localEulerAngles = new Vector3(0f, 0f, rotation);
    }

    private static Vector2 ScaleDecorationSize(Vector2 size, Vector2 scale)
    {
        float x = scale.x > 0f ? scale.x : 1f;
        float y = scale.y > 0f ? scale.y : 1f;
        return new Vector2(size.x * x, size.y * y);
    }

    private static Vector2 ResolveDecorationSize(MenuDecorationConfig config, Sprite sprite)
    {
        float spriteWidth = Mathf.Max(1f, sprite.rect.width);
        float spriteHeight = Mathf.Max(1f, sprite.rect.height);
        float aspect = spriteHeight / spriteWidth;

        if (config.size.x > 0f)
        {
            return new Vector2(config.size.x, config.size.x * aspect);
        }
        if (config.size.y > 0f)
        {
            return new Vector2(config.size.y / aspect, config.size.y);
        }
        return new Vector2(spriteWidth, spriteHeight);
    }

    private void BuildBackgroundAnimations(RectTransform layer)
    {
        foreach (MenuAnimationConfig config in animationConfigs)
        {
            CreateBuiltInAnimation(layer, config);
        }

        foreach (MonoBehaviour hook in backgroundAnimationHooks)
        {
            if (hook is ICarpetMenuBackgroundAnimation animationHook)
            {
                foreach (MenuAnimationConfig config in animationConfigs)
                {
                    animationHook.BuildAnimation(layer, config);
                }
            }
        }
    }

    private void BuildSettingsPanel(RectTransform root)
    {
        settingsPanel = AddRect("SettingsPanel", root);
        settingsPanel.anchorMin = new Vector2(0f, 1f);
        settingsPanel.anchorMax = new Vector2(1f, 1f);
        settingsPanel.pivot = new Vector2(0.5f, 1f);
        settingsPanel.anchoredPosition = new Vector2(0f, -148f);
        settingsPanel.sizeDelta = new Vector2(-72f, 260f);

        Image panelImage = settingsPanel.gameObject.AddComponent<Image>();
        panelImage.color = new Color(0.14f, 0.13f, 0.12f, 0.90f);

        VerticalLayoutGroup panelLayout = settingsPanel.gameObject.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(18, 18, 18, 18);
        panelLayout.spacing = 16;
        panelLayout.childAlignment = TextAnchor.MiddleCenter;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        RectTransform toggleRow = AddRect("SettingToggles", settingsPanel);
        toggleRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;
        HorizontalLayoutGroup rowLayout = toggleRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 10;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;

        AddSettingToggle(toggleRow, "音效", SfxKey, true, null);
        AddSettingToggle(toggleRow, "声音", SoundKey, true, ApplySoundSetting);
        AddSettingToggle(toggleRow, "震动", VibrationKey, true, null);

        Button resetButton = AddButton(settingsPanel, "重置游戏", new Color(0.72f, 0.27f, 0.27f, 0.96f), ResetAllProgress);
        resetButton.gameObject.GetComponent<LayoutElement>().preferredHeight = 58;

        settingsPanel.gameObject.SetActive(false);
    }

    private void ToggleSettingsPanel()
    {
        if (settingsPanel != null)
        {
            settingsPanel.gameObject.SetActive(!settingsPanel.gameObject.activeSelf);
        }
    }

    private Button AddSettingToggle(RectTransform parent, string label, string key, bool defaultValue, Action onChanged)
    {
        RectTransform item = AddRect("Toggle " + label, parent);
        item.gameObject.AddComponent<LayoutElement>().preferredHeight = 46;

        Button button = item.gameObject.AddComponent<Button>();
        Image image = item.gameObject.AddComponent<Image>();
        button.targetGraphic = image;
        Text text = AddText(item, "", 18, FontStyle.Bold, Color.white);
        Fill(text.rectTransform, 6);

        Action refresh = () =>
        {
            bool enabled = GetBoolSetting(key, defaultValue);
            image.color = enabled ? new Color(0.18f, 0.48f, 0.42f, 0.96f) : new Color(0.38f, 0.38f, 0.38f, 0.96f);
            text.text = label + (enabled ? " \u5f00" : " \u5173");
        };

        button.onClick.AddListener(() =>
        {
            SetBoolSetting(key, !GetBoolSetting(key, defaultValue));
            refresh();
            onChanged?.Invoke();
        });
        refresh();
        return button;
    }

    private void CreateBuiltInAnimation(RectTransform layer, MenuAnimationConfig config)
    {
        if (config == null || string.Equals(config.type, "custom", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        RectTransform item = AddRect("MenuAnimation " + config.id, layer);
        item.anchorMin = new Vector2(0.5f, 0.5f);
        item.anchorMax = new Vector2(0.5f, 0.5f);
        item.pivot = new Vector2(0.5f, 0.5f);
        item.anchoredPosition = config.position;
        item.sizeDelta = config.size;

        Image image = item.gameObject.AddComponent<Image>();
        image.color = ParseColor(config.color, new Color(1f, 1f, 1f, 0.18f));
        item.gameObject.AddComponent<CarpetMenuBackgroundTween>().Init(config.position, config.speed);
    }

    private MenuButtonConfig GetButtonConfig(int index)
    {
        if (buttonConfigs != null && index >= 0 && index < buttonConfigs.Length && buttonConfigs[index] != null)
        {
            return buttonConfigs[index];
        }
        if (index >= 0 && index < ChapterButtonCount)
        {
            return CreateDefaultButtonConfig(index);
        }
        return new MenuButtonConfig { label = "按钮 " + (index + 1), levels = new[] { index + 1 } };
    }

    private void StartConfiguredLevel(int buttonIndex)
    {
        if (CarpetLevelFlow.IsTransitioning)
        {
            return;
        }
        if (GetChapterState(buttonIndex) != ChapterState.Open)
        {
            return;
        }

        MenuButtonConfig config = GetButtonConfig(buttonIndex);
        int[] levels = config.levels == null ? Array.Empty<int>() : config.levels.Where(level => level > 0).ToArray();
        if (levels.Length == 0)
        {
            Debug.LogWarning("Menu button has no configured levels: " + buttonIndex);
            return;
        }

        int progress = Mathf.Clamp(buttonProgress[buttonIndex], 0, levels.Length - 1);
        CarpetLevelFlow.StartLevel(buttonIndex, levels[progress]);
    }

    private ChapterState GetChapterState(int index)
    {
        if (index < 0 || index >= buttonProgress.Length)
        {
            return ChapterState.Locked;
        }

        switch (Mathf.Clamp(chapterUnlockStates[index], ChapterStateLocked, ChapterStateFinished))
        {
            case ChapterStateUnlocked:
                return ChapterState.Open;
            case ChapterStateFinished:
                return ChapterState.Completed;
            default:
                return ChapterState.Locked;
        }
    }

    private static string BuildChapterLabel(MenuButtonConfig config, ChapterState chapterState)
    {
        string label = string.IsNullOrEmpty(config.label) ? "\u7ae0\u8282" : config.label;
        if (chapterState == ChapterState.Completed)
        {
            return label + "\n\u5df2\u5b8c\u6210";
        }
        if (chapterState == ChapterState.Locked)
        {
            return label + "\n\u672a\u89e3\u9501";
        }
        return label;
    }

    private static Color ChapterButtonColor(ChapterState chapterState)
    {
        switch (chapterState)
        {
            case ChapterState.Open:
                return new Color(0.18f, 0.43f, 0.50f, 0.96f);
            case ChapterState.Completed:
                return new Color(0.52f, 0.56f, 0.57f, 0.72f);
            default:
                return new Color(0.24f, 0.24f, 0.24f, 0.60f);
        }
    }

    private static Vector2 ResolveButtonSize(MenuButtonConfig config)
    {
        if (config != null && config.size.x > 0f && config.size.y > 0f)
        {
            return config.size;
        }
        return new Vector2(560f, 108f);
    }

    private static Vector2 ResolveButtonPosition(MenuButtonConfig config, int index, Vector2 size)
    {
        if (config != null && config.size.x > 0f && config.size.y > 0f)
        {
            return config.position;
        }

        float spacing = 42f;
        float totalHeight = ChapterButtonCount * size.y + (ChapterButtonCount - 1) * spacing;
        float topY = totalHeight * 0.5f - size.y * 0.5f;
        return new Vector2(0f, topY - index * (size.y + spacing));
    }

    private static MenuButtonConfig CreateDefaultButtonConfig(int index)
    {
        switch (index)
        {
            case 0:
                return new MenuButtonConfig { label = "\u7ae0\u8282\u4e00", levels = new[] { 1, 2 } };
            case 1:
                return new MenuButtonConfig { label = "\u7ae0\u8282\u4e8c", levels = new[] { 3, 4 } };
            case 2:
                return new MenuButtonConfig { label = "\u7ae0\u8282\u4e09", levels = new[] { 5, 6 } };
            case 3:
                return new MenuButtonConfig { label = "\u7ae0\u8282\u56db", levels = new[] { 7 } };
            case 4:
                return new MenuButtonConfig { label = "\u7ae0\u8282\u4e94", levels = new[] { 10 } };
            default:
                return new MenuButtonConfig { label = "\u6309\u94ae " + (index + 1), levels = new[] { index + 1 } };
        }
    }

    private static void LoadProgress()
    {
        for (int i = 0; i < buttonProgress.Length; i++)
        {
            buttonProgress[i] = Mathf.Max(0, PlayerPrefs.GetInt(ProgressKeyPrefix + i, 0));
            int savedState = PlayerPrefs.GetInt(ChapterStateKeyPrefix + i, -1);
            chapterUnlockStates[i] = savedState >= 0
                ? Mathf.Clamp(savedState, ChapterStateLocked, ChapterStateFinished)
                : DeriveChapterUnlockState(i);
            PlayerPrefs.SetInt(ChapterStateKeyPrefix + i, chapterUnlockStates[i]);
        }
        RefreshChapterUnlockStates(false);
        PlayerPrefs.Save();
    }

    private static void SaveProgress()
    {
        for (int i = 0; i < buttonProgress.Length; i++)
        {
            PlayerPrefs.SetInt(ProgressKeyPrefix + i, Mathf.Max(0, buttonProgress[i]));
        }
        PlayerPrefs.Save();
    }

    private static int DeriveChapterUnlockState(int index)
    {
        if (index < 0 || index >= buttonProgress.Length)
        {
            return ChapterStateLocked;
        }

        int levelCount = GetConfiguredLevelCount(index);
        if (levelCount <= 0 || buttonProgress[index] >= levelCount)
        {
            return ChapterStateFinished;
        }
        if (index == 0)
        {
            bool firstChapterUnlocked = PlayerPrefs.GetInt(FirstChapterUnlockedKey, 0) != 0 || chapterUnlockStates[index] >= ChapterStateUnlocked;
            return firstChapterUnlocked ? ChapterStateUnlocked : ChapterStateLocked;
        }

        int previousLevelCount = GetConfiguredLevelCount(index - 1);
        return previousLevelCount > 0 && buttonProgress[index - 1] >= previousLevelCount ? ChapterStateUnlocked : ChapterStateLocked;
    }

    private static int GetConfiguredLevelCount(int index)
    {
        CarpetLevelMenu menu = instance != null ? instance : FindObjectOfType<CarpetLevelMenu>();
        MenuButtonConfig config = menu != null ? menu.GetButtonConfig(index) : CreateDefaultButtonConfig(index);
        return config.levels == null ? 0 : config.levels.Count(level => level > 0);
    }

    private static void RefreshChapterUnlockStates(bool markTransitions)
    {
        for (int i = 0; i < chapterUnlockStates.Length; i++)
        {
            SetChapterUnlockState(i, DeriveChapterUnlockState(i), markTransitions);
        }
        PlayerPrefs.Save();
    }

    private static void SetChapterUnlockState(int index, int state, bool markTransition)
    {
        if (index < 0 || index >= chapterUnlockStates.Length)
        {
            return;
        }

        int nextState = Mathf.Clamp(state, ChapterStateLocked, ChapterStateFinished);
        int previousState = Mathf.Clamp(chapterUnlockStates[index], ChapterStateLocked, ChapterStateFinished);
        if (previousState == nextState)
        {
            return;
        }

        chapterUnlockStates[index] = nextState;
        PlayerPrefs.SetInt(ChapterStateKeyPrefix + index, nextState);

        if (!markTransition)
        {
            return;
        }

        if (previousState == ChapterStateLocked && nextState == ChapterStateUnlocked)
        {
            PlayerPrefs.SetInt(ChapterTransitionKeyPrefix + index, ChapterTransitionLockToUnlock);
        }
        else if (previousState == ChapterStateUnlocked && nextState == ChapterStateFinished)
        {
            PlayerPrefs.SetInt(ChapterTransitionKeyPrefix + index, ChapterTransitionUnlockToFinish);
        }
    }

    private static int ConsumePendingChapterTransition(int index)
    {
        if (index < 0 || index >= ChapterButtonCount)
        {
            return ChapterTransitionNone;
        }

        string key = ChapterTransitionKeyPrefix + index;
        int transition = PlayerPrefs.GetInt(key, ChapterTransitionNone);
        if (transition != ChapterTransitionNone)
        {
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }
        return transition;
    }

    private static void UnlockFirstChapter()
    {
        PlayerPrefs.SetInt(FirstChapterUnlockedKey, 1);
        SetChapterUnlockState(0, ChapterStateUnlocked, true);
        PlayerPrefs.Save();
    }

    private void ResetAllProgress()
    {
        CarpetLevelFlow.ResetGameAndReturnToIntro();
    }

    private static bool GetBoolSetting(string key, bool defaultValue)
    {
        return PlayerPrefs.GetInt(key, defaultValue ? 1 : 0) != 0;
    }

    private static void SetBoolSetting(string key, bool value)
    {
        PlayerPrefs.SetInt(key, value ? 1 : 0);
        PlayerPrefs.Save();
    }

    private static void ApplySoundSetting()
    {
        PlayerPrefs.SetInt(SoundKey, GetBoolSetting(SoundKey, true) ? 1 : 0);
        PlayerPrefs.Save();
        CarpetBgmPlayer.ApplySavedSetting();
    }

    private Button AddButton(RectTransform parent, string label, Color color, Action onClick)
    {
        RectTransform rect = AddRect("Button " + label, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => onClick());
        Text text = AddText(rect, label, 26, FontStyle.Bold, Color.white);
        Fill(text.rectTransform, 8);
        rect.gameObject.AddComponent<LayoutElement>().preferredHeight = 72;
        return button;
    }

    private Button AddImageButton(RectTransform parent, string name, Sprite sprite, Action onClick)
    {
        if (sprite == null)
        {
            return AddButton(parent, name, new Color(0.30f, 0.30f, 0.30f, 0.92f), onClick);
        }

        RectTransform rect = AddRect("ImageButton " + name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = Color.white;
        image.preserveAspect = true;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => onClick());
        rect.gameObject.AddComponent<LayoutElement>().preferredHeight = 118;
        return button;
    }

    private Text AddText(RectTransform parent, string value, int size, FontStyle style, Color color)
    {
        RectTransform rect = AddRect("Text", parent);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = uiFont;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        Stretch(rect);
        return text;
    }

    private static RectTransform AddRect(string name, Transform parent)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void Fill(RectTransform rect, float inset = 0)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static Font LoadUiFont()
    {
        string[] candidates =
        {
            "Microsoft YaHei UI",
            "Microsoft YaHei",
            "SimHei",
            "Noto Sans CJK SC",
            "Arial"
        };

        Font font = Font.CreateDynamicFontFromOSFont(candidates, 16);
        return font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private static Color ParseColor(string value, Color fallback)
    {
        if (!string.IsNullOrEmpty(value) && ColorUtility.TryParseHtmlString(value, out Color parsed))
        {
            return parsed;
        }
        return fallback;
    }

    [Serializable]
    public sealed class MenuConfig
    {
        public string backgroundColor = "";
        public string backgroundImage = "";
        public MenuButtonConfig[] buttons = Array.Empty<MenuButtonConfig>();
        public MenuDecorationConfig[] decorations = Array.Empty<MenuDecorationConfig>();
        public MenuAnimationConfig[] animations = Array.Empty<MenuAnimationConfig>();
    }

    [Serializable]
    public sealed class MenuButtonConfig
    {
        public string label = "";
        public int[] levels = Array.Empty<int>();
        public Vector2 position;
        public Vector2 size;
        public float rotation;
    }

    [Serializable]
    public sealed class MenuDecorationConfig
    {
        public string id = "";
        public string image = "";
        public Vector2 position;
        public Vector2 size = Vector2.zero;
        public float rotation;
        public string color = "#ffffff";
        public bool shadow;
        public Vector2 shadowOffset = Vector2.zero;
        public Vector2 shadowScale = Vector2.one;
        public string shadowColor = "#1a121466";
        public string[] animationFrames = Array.Empty<string>();
        public float[] animationDurations = Array.Empty<float>();
        public string[] triggerAnimationFolders = Array.Empty<string>();
        public float triggerIntervalSeconds = 5f;
        public float triggerFrameSeconds = 0.05f;
        public float breathAmplitude = 6f;
        public float breathScale = 0.018f;
        public float breathSeconds = 2.8f;
        public float frameFadeSeconds = 0.08f;
    }

    [Serializable]
    public sealed class MenuAnimationConfig
    {
        public string id = "";
        public string type = "";
        public string color = "#ffffff";
        public Vector2 position;
        public Vector2 size = new Vector2(120, 120);
        public float speed = 1f;
        public string payload = "";
    }

    private enum ChapterState
    {
        Locked,
        Open,
        Completed
    }
}

public sealed class CarpetMenuBackgroundTween : MonoBehaviour
{
    private RectTransform rect;
    private Graphic graphic;
    private Vector2 origin;
    private float speed = 1f;
    private float phase;

    public void Init(Vector2 start, float animationSpeed)
    {
        origin = start;
        speed = Mathf.Max(0.01f, animationSpeed);
        phase = UnityEngine.Random.Range(0f, 6.28f);
    }

    private void Awake()
    {
        rect = GetComponent<RectTransform>();
        graphic = GetComponent<Graphic>();
    }

    private void Update()
    {
        if (rect == null)
        {
            return;
        }

        float t = Time.unscaledTime * speed + phase;
        rect.anchoredPosition = origin + new Vector2(Mathf.Sin(t) * 18f, Mathf.Cos(t * 0.7f) * 12f);
        if (graphic != null)
        {
            Color color = graphic.color;
            color.a = Mathf.Lerp(0.08f, 0.22f, (Mathf.Sin(t * 0.9f) + 1f) * 0.5f);
            graphic.color = color;
        }
    }
}

public sealed class MenuDecorationFrameAnimator : MonoBehaviour
{
    private RectTransform rect;
    private Image image;
    private Image overlayImage;
    private Sprite[] frames = Array.Empty<Sprite>();
    private float[] durations = Array.Empty<float>();
    private Vector2 origin;
    private Vector3 baseScale = Vector3.one;
    private float elapsed;
    private float fadeElapsed = 99f;
    private float totalDuration = 1f;
    private float breathAmplitude = 6f;
    private float breathScale = 0.018f;
    private float breathSeconds = 2.8f;
    private float frameFadeSeconds = 0.08f;
    private int currentFrame = -1;

    public void Init(RectTransform target, Image targetImage, Sprite[] sprites, float[] frameDurations, Vector2 basePosition, float amplitude, float scale, float seconds, float fadeSeconds)
    {
        rect = target;
        image = targetImage;
        frames = sprites ?? Array.Empty<Sprite>();
        durations = NormalizeDurations(frames.Length, frameDurations);
        origin = basePosition;
        baseScale = target != null ? target.localScale : Vector3.one;
        breathAmplitude = Mathf.Max(0f, amplitude);
        breathScale = Mathf.Max(0f, scale);
        breathSeconds = Mathf.Max(0.1f, seconds);
        frameFadeSeconds = Mathf.Max(0f, fadeSeconds);
        totalDuration = Mathf.Max(0.01f, durations.Sum());
        EnsureOverlayImage();
        SetFrame(0);
    }

    private void Awake()
    {
        if (rect == null)
        {
            rect = transform as RectTransform;
        }
        if (image == null)
        {
            image = GetComponent<Image>();
        }
    }

    private void Update()
    {
        if (rect == null || image == null || frames.Length == 0)
        {
            return;
        }

        elapsed = (elapsed + Time.unscaledDeltaTime) % Mathf.Max(0.01f, totalDuration);
        SetFrame(FrameIndexAt(elapsed));
        UpdateFrameFade();

        float breath = Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / breathSeconds);
        rect.anchoredPosition = origin + new Vector2(0f, breath * breathAmplitude);
        float yScale = 1f + breath * breathScale;
        float xScale = 1f - breath * breathScale * 0.25f;
        rect.localScale = new Vector3(baseScale.x * xScale, baseScale.y * yScale, baseScale.z);
    }

    private void SetFrame(int index)
    {
        if (index == currentFrame || index < 0 || index >= frames.Length)
        {
            return;
        }
        if (currentFrame >= 0 && overlayImage != null && image != null && image.sprite != null && frameFadeSeconds > 0f)
        {
            overlayImage.sprite = image.sprite;
            overlayImage.color = image.color;
            fadeElapsed = 0f;
        }
        currentFrame = index;
        image.sprite = frames[index];
    }

    private void EnsureOverlayImage()
    {
        if (image == null || overlayImage != null || frameFadeSeconds <= 0f)
        {
            return;
        }

        GameObject overlayObject = new GameObject("FrameCrossfade", typeof(RectTransform), typeof(Image));
        overlayObject.transform.SetParent(image.transform, false);
        RectTransform overlayRect = overlayObject.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        overlayImage = overlayObject.GetComponent<Image>();
        overlayImage.preserveAspect = image.preserveAspect;
        overlayImage.raycastTarget = false;
        Color color = image.color;
        color.a = 0f;
        overlayImage.color = color;
    }

    private void UpdateFrameFade()
    {
        if (overlayImage == null || frameFadeSeconds <= 0f)
        {
            return;
        }

        fadeElapsed += Time.unscaledDeltaTime;
        float alpha = 1f - Mathf.Clamp01(fadeElapsed / frameFadeSeconds);
        Color color = image != null ? image.color : Color.white;
        color.a *= alpha;
        overlayImage.color = color;
    }

    private int FrameIndexAt(float time)
    {
        float cursor = 0f;
        for (int i = 0; i < durations.Length; i++)
        {
            cursor += durations[i];
            if (time <= cursor)
            {
                return i;
            }
        }
        return durations.Length - 1;
    }

    private static float[] NormalizeDurations(int count, float[] input)
    {
        if (count <= 0)
        {
            return Array.Empty<float>();
        }

        float[] result = new float[count];
        for (int i = 0; i < count; i++)
        {
            float value = input != null && i < input.Length ? input[i] : 0.8f;
            result[i] = Mathf.Max(0.05f, value);
        }
        return result;
    }
}

public sealed class MenuDecorationTriggeredFrameAnimator : MonoBehaviour
{
    private RectTransform rect;
    private Image image;
    private Image overlayImage;
    private Sprite idleFrame;
    private Sprite[][] sequences = Array.Empty<Sprite[]>();
    private Vector2 origin;
    private Vector3 baseScale = Vector3.one;
    private float triggerIntervalSeconds = 5f;
    private float triggerFrameSeconds = 0.05f;
    private float breathAmplitude = 6f;
    private float breathScale = 0.018f;
    private float breathSeconds = 2.8f;
    private float frameFadeSeconds = 0.08f;
    private float idleElapsed;
    private float animationElapsed;
    private float fadeElapsed = 99f;
    private int nextSequenceIndex;
    private int activeSequenceIndex = -1;
    private int currentFrame = -1;

    public void Init(RectTransform target, Image targetImage, Sprite idleSprite, Sprite[][] animationSequences, Vector2 basePosition, float intervalSeconds, float frameSeconds, float amplitude, float scale, float seconds, float fadeSeconds)
    {
        rect = target;
        image = targetImage;
        idleFrame = idleSprite;
        sequences = (animationSequences ?? Array.Empty<Sprite[]>())
            .Where(sequence => sequence != null && sequence.Length > 0)
            .ToArray();
        origin = basePosition;
        baseScale = target != null ? target.localScale : Vector3.one;
        triggerIntervalSeconds = Mathf.Max(0.1f, intervalSeconds);
        triggerFrameSeconds = Mathf.Max(0.01f, frameSeconds);
        breathAmplitude = Mathf.Max(0f, amplitude);
        breathScale = Mathf.Max(0f, scale);
        breathSeconds = Mathf.Max(0.1f, seconds);
        frameFadeSeconds = Mathf.Max(0f, fadeSeconds);
        EnsureOverlayImage();
        SetSprite(idleFrame);
    }

    private void Awake()
    {
        if (rect == null)
        {
            rect = transform as RectTransform;
        }
        if (image == null)
        {
            image = GetComponent<Image>();
        }
    }

    private void Update()
    {
        if (rect == null || image == null || sequences.Length == 0)
        {
            return;
        }

        if (activeSequenceIndex >= 0)
        {
            animationElapsed += Time.unscaledDeltaTime;
            UpdateActiveSequence();
        }
        else
        {
            idleElapsed += Time.unscaledDeltaTime;
            if (idleElapsed >= triggerIntervalSeconds)
            {
                StartNextSequence();
            }
        }

        UpdateFrameFade();
        ApplyBreath();
    }

    private void StartNextSequence()
    {
        for (int attempt = 0; attempt < sequences.Length; attempt++)
        {
            int index = nextSequenceIndex % sequences.Length;
            nextSequenceIndex = (nextSequenceIndex + 1) % sequences.Length;
            if (sequences[index] == null || sequences[index].Length == 0)
            {
                continue;
            }

            activeSequenceIndex = index;
            animationElapsed = 0f;
            currentFrame = -1;
            SetSequenceFrame(0);
            return;
        }
    }

    private void UpdateActiveSequence()
    {
        if (activeSequenceIndex < 0 || activeSequenceIndex >= sequences.Length)
        {
            FinishSequence();
            return;
        }

        Sprite[] sequence = sequences[activeSequenceIndex];
        if (sequence == null || sequence.Length == 0)
        {
            FinishSequence();
            return;
        }

        int frameIndex = Mathf.Clamp(Mathf.FloorToInt(animationElapsed / triggerFrameSeconds), 0, sequence.Length - 1);
        SetSequenceFrame(frameIndex);
        if (animationElapsed >= triggerFrameSeconds * sequence.Length)
        {
            FinishSequence();
        }
    }

    private void SetSequenceFrame(int index)
    {
        if (activeSequenceIndex < 0 || activeSequenceIndex >= sequences.Length)
        {
            return;
        }
        if (index == currentFrame)
        {
            return;
        }

        Sprite[] sequence = sequences[activeSequenceIndex];
        if (sequence == null || index < 0 || index >= sequence.Length)
        {
            return;
        }

        currentFrame = index;
        SetSprite(sequence[index]);
    }

    private void FinishSequence()
    {
        activeSequenceIndex = -1;
        currentFrame = -1;
        animationElapsed = 0f;
        idleElapsed = 0f;
        SetSprite(idleFrame);
    }

    private void SetSprite(Sprite sprite)
    {
        if (image == null || image.sprite == sprite)
        {
            return;
        }

        if (overlayImage != null && image.sprite != null && frameFadeSeconds > 0f)
        {
            overlayImage.sprite = image.sprite;
            overlayImage.color = image.color;
            fadeElapsed = 0f;
        }
        image.sprite = sprite;
    }

    private void ApplyBreath()
    {
        float breath = Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / breathSeconds);
        rect.anchoredPosition = origin + new Vector2(0f, breath * breathAmplitude);
        float yScale = 1f + breath * breathScale;
        float xScale = 1f - breath * breathScale * 0.25f;
        rect.localScale = new Vector3(baseScale.x * xScale, baseScale.y * yScale, baseScale.z);
    }

    private void EnsureOverlayImage()
    {
        if (image == null || overlayImage != null || frameFadeSeconds <= 0f)
        {
            return;
        }

        GameObject overlayObject = new GameObject("TriggeredFrameCrossfade", typeof(RectTransform), typeof(Image));
        overlayObject.transform.SetParent(image.transform, false);
        RectTransform overlayRect = overlayObject.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        overlayImage = overlayObject.GetComponent<Image>();
        overlayImage.preserveAspect = image.preserveAspect;
        overlayImage.raycastTarget = false;
        Color color = image.color;
        color.a = 0f;
        overlayImage.color = color;
    }

    private void UpdateFrameFade()
    {
        if (overlayImage == null || frameFadeSeconds <= 0f)
        {
            return;
        }

        fadeElapsed += Time.unscaledDeltaTime;
        float alpha = 1f - Mathf.Clamp01(fadeElapsed / frameFadeSeconds);
        Color color = image != null ? image.color : Color.white;
        color.a *= alpha;
        overlayImage.color = color;
    }
}
