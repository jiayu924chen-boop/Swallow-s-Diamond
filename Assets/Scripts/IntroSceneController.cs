using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

public sealed class IntroSceneController : MonoBehaviour
{
    private const string ConfigPath = "Intro/intro_story_config.json";
    private const string DefaultVideoPath = "Intro/intro_video.mp4";
    private const string DefaultLogoResourcePath = "Intro/swallows_diamond_logo";
    private const string DefaultStartButtonResourcePath = "Intro/start_button";

    private IntroConfig config;
    private Font uiFont;
    private VideoPlayer videoPlayer;
    private RenderTexture videoTexture;
    private RawImage videoImage;
    private RectTransform videoRect;
    private RectTransform titleLayer;
    private RectTransform overlayLayer;
    private RectTransform storyList;
    private Text storyHint;
    private Image overlayImage;
    private RectTransform transitionLayer;
    private Image transitionImage;
    private RawImage transitionAssetImage;
    private Text transitionText;
    private Coroutine revealRoutine;

    private int pageIndex;
    private int lineIndex;
    private Text activeLine;
    private string activeLineValue = "";
    private IntroState state = IntroState.Landing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneLoaded()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == CarpetLevelFlow.IntroSceneName)
        {
            EnsureIntroExists();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (SceneManager.GetActiveScene().name == CarpetLevelFlow.IntroSceneName)
        {
            EnsureIntroExists();
        }
    }

    private static void EnsureIntroExists()
    {
        if (FindObjectOfType<IntroSceneController>() != null)
        {
            return;
        }

        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(eventSystem);
        }

        GameObject host = new GameObject("Intro Scene Controller");
        host.AddComponent<IntroSceneController>();
    }

    private void Awake()
    {
        CarpetBgmPlayer.EnsurePlaying();
        uiFont = LoadUiFont();
        config = LoadConfig();
        BuildUi();
        BuildVideoPlayer();
    }

    private void OnDestroy()
    {
        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= HandleVideoPrepared;
            videoPlayer.loopPointReached -= HandleVideoEnded;
        }
        if (videoTexture != null)
        {
            videoTexture.Release();
            Destroy(videoTexture);
        }
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
        Image fallback = root.gameObject.AddComponent<Image>();
        fallback.color = ParseColor(config.backgroundColor, Color.black);
        fallback.raycastTarget = false;

        videoRect = AddRect("IntroVideoFrame", root);
        Center(videoRect);
        videoRect.sizeDelta = new Vector2(1080, 1920);
        videoImage = videoRect.gameObject.AddComponent<RawImage>();
        videoImage.color = Color.white;
        videoImage.raycastTarget = false;

        titleLayer = AddRect("TitleLayer", root);
        Stretch(titleLayer);
        BuildTitleLayer(titleLayer);

        overlayLayer = AddRect("StoryOverlay", root);
        Stretch(overlayLayer);
        overlayImage = overlayLayer.gameObject.AddComponent<Image>();
        overlayImage.color = new Color(0f, 0f, 0f, Mathf.Clamp01(config.storyOverlayAlpha));

        Button storyButton = overlayLayer.gameObject.AddComponent<Button>();
        storyButton.targetGraphic = overlayImage;
        storyButton.transition = Selectable.Transition.None;
        storyButton.onClick.AddListener(HandleStoryClick);

        BuildStoryContent(overlayLayer);
        overlayLayer.gameObject.SetActive(false);

        transitionLayer = AddRect("TransitionLayer", root);
        Stretch(transitionLayer);
        transitionImage = transitionLayer.gameObject.AddComponent<Image>();
        transitionImage.color = new Color(0f, 0f, 0f, 0f);
        transitionImage.raycastTarget = false;
        Texture2D transitionTexture = string.IsNullOrEmpty(config.transitionImageResource)
            ? null
            : Resources.Load<Texture2D>(SafeResourcePath(config.transitionImageResource, ""));
        if (transitionTexture != null)
        {
            RectTransform transitionAssetRect = AddRect("TransitionAsset", transitionLayer);
            Center(transitionAssetRect);
            transitionAssetRect.sizeDelta = new Vector2(520f, 520f);
            transitionAssetImage = transitionAssetRect.gameObject.AddComponent<RawImage>();
            transitionAssetImage.texture = transitionTexture;
            transitionAssetImage.color = new Color(1f, 1f, 1f, 0f);
            transitionAssetImage.raycastTarget = false;
        }
        transitionText = AddText(transitionLayer, config.transitionLabel, 36, FontStyle.Bold, Color.white);
        transitionText.alignment = TextAnchor.MiddleCenter;
        transitionText.color = new Color(1f, 1f, 1f, 0f);
        Fill(transitionText.rectTransform, 32f);
        transitionLayer.gameObject.SetActive(false);
    }

    private void BuildTitleLayer(RectTransform root)
    {
        RectTransform logoRect = AddRect("Logo", root);
        logoRect.anchorMin = new Vector2(0.5f, 1f);
        logoRect.anchorMax = new Vector2(0.5f, 1f);
        logoRect.pivot = new Vector2(0.5f, 1f);
        logoRect.anchoredPosition = new Vector2(0f, -110f);
        logoRect.sizeDelta = new Vector2(900f, 520f);

        Texture2D logoTexture = Resources.Load<Texture2D>(SafeResourcePath(config.logoResource, DefaultLogoResourcePath));
        if (logoTexture != null)
        {
            logoRect.sizeDelta = FitInside(new Vector2(900f, 520f), logoTexture.width, logoTexture.height);
            RawImage logo = logoRect.gameObject.AddComponent<RawImage>();
            logo.texture = logoTexture;
            logo.color = Color.white;
            logo.raycastTarget = false;
        }
        else
        {
            Text logoText = AddText(logoRect, "SWALLOW'S\nDIAMOND", 72, FontStyle.Bold, Color.black);
            logoText.alignment = TextAnchor.MiddleCenter;
            Fit(logoText.rectTransform, 0f);
        }

        RectTransform buttonRect = AddRect("StartButton", root);
        buttonRect.anchorMin = new Vector2(0.5f, 0f);
        buttonRect.anchorMax = new Vector2(0.5f, 0f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = new Vector2(0f, 640f);
        buttonRect.sizeDelta = new Vector2(570f, 188f);

        Graphic buttonGraphic;
        Texture2D startButtonTexture = Resources.Load<Texture2D>(SafeResourcePath(config.startButtonResource, DefaultStartButtonResourcePath));
        if (startButtonTexture != null)
        {
            buttonRect.sizeDelta = FitInside(new Vector2(570f, 188f), startButtonTexture.width, startButtonTexture.height);
            RawImage buttonImage = buttonRect.gameObject.AddComponent<RawImage>();
            buttonImage.texture = startButtonTexture;
            buttonImage.color = Color.white;
            buttonGraphic = buttonImage;
        }
        else
        {
            Image buttonImage = buttonRect.gameObject.AddComponent<Image>();
            buttonImage.color = ParseColor(config.startButtonColor, new Color(0.08f, 0.08f, 0.08f, 0.88f));
            Text buttonText = AddText(buttonRect, config.startButtonText, 36, FontStyle.Bold, Color.white);
            Fill(buttonText.rectTransform, 18f);
            buttonGraphic = buttonImage;
        }

        Button button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = buttonGraphic;
        button.onClick.AddListener(StartStory);
    }

    private void BuildStoryContent(RectTransform root)
    {
        RectTransform panel = AddRect("StoryPanel", root);
        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(900f, 1500f);

        storyList = panel;
        storyHint = AddText(root, config.storyHint, 24, FontStyle.Normal, new Color(1f, 1f, 1f, 0.58f));
        storyHint.alignment = TextAnchor.LowerCenter;
        storyHint.rectTransform.anchorMin = new Vector2(0f, 0f);
        storyHint.rectTransform.anchorMax = new Vector2(1f, 0f);
        storyHint.rectTransform.pivot = new Vector2(0.5f, 0f);
        storyHint.rectTransform.anchoredPosition = new Vector2(0f, 78f);
        storyHint.rectTransform.sizeDelta = new Vector2(-120f, 58f);
    }

    private void BuildVideoPlayer()
    {
        GameObject host = new GameObject("IntroVideoPlayer", typeof(VideoPlayer));
        host.transform.SetParent(transform, false);
        videoPlayer = host.GetComponent<VideoPlayer>();
        videoPlayer.playOnAwake = false;
        videoPlayer.waitForFirstFrame = true;
        videoPlayer.isLooping = false;
        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = Path.Combine(Application.streamingAssetsPath, string.IsNullOrEmpty(config.videoPath) ? DefaultVideoPath : config.videoPath).Replace("\\", "/");
        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        videoPlayer.prepareCompleted += HandleVideoPrepared;
        videoPlayer.loopPointReached += HandleVideoEnded;

        CreateVideoTexture(1080, 1920);
        videoPlayer.Prepare();
    }

    private void HandleVideoPrepared(VideoPlayer player)
    {
        int width = player.width > 0 ? (int)player.width : 1080;
        int height = player.height > 0 ? (int)player.height : 1920;
        CreateVideoTexture(width, height);
        ApplyCoverSize(width, height);
        player.frame = 0;
        StartCoroutine(ShowPreparedFirstFrame(player));
    }

    private IEnumerator ShowPreparedFirstFrame(VideoPlayer player)
    {
        player.Play();
        yield return null;
        if (state != IntroState.Video && player != null)
        {
            player.Pause();
            player.frame = 0;
        }
    }

    private void StartStory()
    {
        if (state != IntroState.Landing)
        {
            return;
        }

        titleLayer.gameObject.SetActive(false);
        overlayLayer.gameObject.SetActive(true);
        pageIndex = 0;
        lineIndex = 0;
        state = IntroState.Story;
        StartCurrentPage();
    }

    private void StartCurrentPage()
    {
        ClearChildren(storyList);
        lineIndex = 0;
        activeLine = null;
        activeLineValue = "";
        RevealNextLine();
    }

    private void HandleStoryClick()
    {
        if (state == IntroState.Story)
        {
            if (revealRoutine != null)
            {
                StopCoroutine(revealRoutine);
                revealRoutine = null;
                if (activeLine != null)
                {
                    activeLine.text = activeLineValue;
                }
                return;
            }

            if (pageIndex + 1 < StoryPageCount())
            {
                pageIndex++;
                StartCurrentPage();
                return;
            }

            state = IntroState.AwaitingVideoClick;
            storyHint.text = config.finalStoryHint;
            return;
        }

        if (state == IntroState.AwaitingVideoClick)
        {
            StartVideo();
        }
    }

    private void RevealNextLine()
    {
        IntroStoryPage page = GetCurrentPage();
        if (page == null || page.lines == null || page.lines.Length == 0)
        {
            return;
        }

        string value = string.Join("\n\n", page.lines);
        lineIndex = page.lines.Length;

        activeLineValue = value;
        activeLine = AddText(storyList, "", Mathf.Max(16, config.storyFontSize), FontStyle.Normal, Color.white);
        activeLine.alignment = TextAnchor.MiddleLeft;
        activeLine.horizontalOverflow = HorizontalWrapMode.Wrap;
        activeLine.verticalOverflow = VerticalWrapMode.Overflow;
        activeLine.lineSpacing = 1.18f;
        revealRoutine = StartCoroutine(RevealLineRoutine(activeLine, value));
    }

    private IEnumerator RevealLineRoutine(Text label, string value)
    {
        float charDelay = Mathf.Max(0.001f, config.lineRevealSeconds / Mathf.Max(1, value.Length));
        for (int i = 0; i <= value.Length; i++)
        {
            label.text = value.Substring(0, i);
            yield return new WaitForSecondsRealtime(charDelay);
        }

        revealRoutine = null;
    }

    private void StartVideo()
    {
        state = IntroState.Video;
        overlayLayer.gameObject.SetActive(false);

        if (videoPlayer == null)
        {
            StartCoroutine(PlayTransitionThenMenu());
            return;
        }

        if (videoPlayer.isPrepared)
        {
            videoPlayer.frame = 0;
            videoPlayer.Play();
        }
        else
        {
            StartCoroutine(PlayWhenPrepared());
        }
    }

    private IEnumerator PlayWhenPrepared()
    {
        float deadline = Time.realtimeSinceStartup + 8f;
        while (videoPlayer != null && !videoPlayer.isPrepared && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        if (videoPlayer != null && videoPlayer.isPrepared)
        {
            videoPlayer.frame = 0;
            videoPlayer.Play();
        }
        else
        {
            StartCoroutine(PlayTransitionThenMenu());
        }
    }

    private void HandleVideoEnded(VideoPlayer player)
    {
        if (state != IntroState.Video)
        {
            return;
        }

        player.Pause();
        StartCoroutine(PlayTransitionThenMenu());
    }

    private IEnumerator PlayTransitionThenMenu()
    {
        state = IntroState.Transition;
        transitionLayer.gameObject.SetActive(true);
        float seconds = Mathf.Max(0.1f, config.transitionSeconds);
        float elapsed = 0f;

        while (elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / seconds);
            float eased = t * t * (3f - 2f * t);
            transitionImage.color = new Color(0f, 0f, 0f, eased);
            transitionText.color = new Color(1f, 1f, 1f, Mathf.Clamp01((eased - 0.25f) / 0.75f));
            transitionText.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.96f, 1.04f, Mathf.Sin(t * Mathf.PI));
            if (transitionAssetImage != null)
            {
                transitionAssetImage.color = new Color(1f, 1f, 1f, Mathf.Clamp01((eased - 0.15f) / 0.85f));
                transitionAssetImage.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.86f, 1.08f, eased);
            }
            yield return null;
        }

        CarpetLevelFlow.RequestMenuGuide(GuideTextType.StartGame);
        SceneManager.LoadScene(CarpetLevelFlow.MenuSceneName, LoadSceneMode.Single);
    }

    private IntroStoryPage GetCurrentPage()
    {
        if (config.storyPages == null || pageIndex < 0 || pageIndex >= config.storyPages.Length)
        {
            return null;
        }
        return config.storyPages[pageIndex];
    }

    private int StoryPageCount()
    {
        return config.storyPages == null ? 0 : config.storyPages.Length;
    }

    private void CreateVideoTexture(int width, int height)
    {
        width = Mathf.Clamp(width, 16, 4096);
        height = Mathf.Clamp(height, 16, 4096);

        if (videoTexture != null && videoTexture.width == width && videoTexture.height == height)
        {
            return;
        }

        if (videoTexture != null)
        {
            videoTexture.Release();
            Destroy(videoTexture);
        }

        videoTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        videoTexture.Create();
        videoImage.texture = videoTexture;
        if (videoPlayer != null)
        {
            videoPlayer.targetTexture = videoTexture;
        }
    }

    private void ApplyCoverSize(int width, int height)
    {
        float contentAspect = width / Mathf.Max(1f, (float)height);
        float referenceAspect = 1080f / 1920f;
        float coverWidth = 1080f;
        float coverHeight = 1920f;

        if (contentAspect > referenceAspect)
        {
            coverWidth = coverHeight * contentAspect;
        }
        else
        {
            coverHeight = coverWidth / Mathf.Max(0.01f, contentAspect);
        }

        videoRect.sizeDelta = new Vector2(coverWidth, coverHeight);
    }

    private IntroConfig LoadConfig()
    {
        string path = Path.Combine(Application.streamingAssetsPath, ConfigPath);
        if (File.Exists(path))
        {
            try
            {
                IntroConfig loaded = JsonUtility.FromJson<IntroConfig>(File.ReadAllText(path));
                if (loaded != null)
                {
                    return loaded.WithDefaults();
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning("Failed to read intro config: " + error.Message);
            }
        }

        return IntroConfig.Default();
    }

    private static string SafeResourcePath(string value, string fallback)
    {
        string path = string.IsNullOrEmpty(value) ? fallback : value;
        return Path.ChangeExtension(path.Replace("\\", "/"), null);
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

    private static void Center(RectTransform rect)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
    }

    private static void Fill(RectTransform rect, float inset = 0)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static Vector2 FitInside(Vector2 maxSize, float width, float height)
    {
        float aspect = Mathf.Max(0.01f, width) / Mathf.Max(0.01f, height);
        float fittedWidth = maxSize.x;
        float fittedHeight = fittedWidth / aspect;
        if (fittedHeight > maxSize.y)
        {
            fittedHeight = maxSize.y;
            fittedWidth = fittedHeight * aspect;
        }
        return new Vector2(fittedWidth, fittedHeight);
    }

    private static void Fit(RectTransform rect, float inset = 0)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Destroy(parent.GetChild(i).gameObject);
        }
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
    private sealed class IntroConfig
    {
        public string videoPath = DefaultVideoPath;
        public string logoResource = DefaultLogoResourcePath;
        public string backgroundColor = "#000000";
        public string startButtonResource = DefaultStartButtonResourcePath;
        public string startButtonText = "\u5f00\u59cb\u6e38\u620f";
        public string startButtonColor = "#111111dd";
        public float storyOverlayAlpha = 0.78f;
        public int storyFontSize = 34;
        public float storyLineHeight = 92f;
        public float lineRevealSeconds = 1.15f;
        public string storyHint = "\u70b9\u51fb\u7ee7\u7eed";
        public string finalStoryHint = "\u518d\u6b21\u70b9\u51fb\u5f00\u59cb";
        public string transitionLabel = "LOADING";
        public string transitionImageResource = "";
        public float transitionSeconds = 1.4f;
        public IntroStoryPage[] storyPages = Array.Empty<IntroStoryPage>();

        public static IntroConfig Default()
        {
            return new IntroConfig
            {
                storyPages = CreateDefaultStoryPages()
            }.WithDefaults();
        }

        private static IntroStoryPage[] CreateDefaultStoryPages()
        {
            return new[]
            {
                new IntroStoryPage
                {
                    lines = new[]
                    {
                        "AX7-19 QV0L 7M2T",
                        "K4N-88 LUMEN / NODE / TRACE",
                        "RZQ-314 00FF AWAKE",
                        "THIS PAGE IS A PLACEHOLDER FOR STORY COPY."
                    }
                }
            };
        }

        public IntroConfig WithDefaults()
        {
            if (string.IsNullOrEmpty(videoPath))
            {
                videoPath = DefaultVideoPath;
            }
            if (string.IsNullOrEmpty(logoResource))
            {
                logoResource = DefaultLogoResourcePath;
            }
            if (string.IsNullOrEmpty(startButtonResource))
            {
                startButtonResource = DefaultStartButtonResourcePath;
            }
            if (string.IsNullOrEmpty(startButtonText))
            {
                startButtonText = "\u5f00\u59cb\u6e38\u620f";
            }
            if (string.IsNullOrEmpty(storyHint))
            {
                storyHint = "\u70b9\u51fb\u7ee7\u7eed";
            }
            if (string.IsNullOrEmpty(finalStoryHint))
            {
                finalStoryHint = "\u518d\u6b21\u70b9\u51fb\u5f00\u59cb";
            }
            if (string.IsNullOrEmpty(transitionLabel))
            {
                transitionLabel = "LOADING";
            }
            if (storyPages == null || storyPages.Length == 0)
            {
                storyPages = CreateDefaultStoryPages();
            }
            storyOverlayAlpha = Mathf.Clamp01(storyOverlayAlpha <= 0f ? 0.78f : storyOverlayAlpha);
            storyFontSize = storyFontSize <= 0 ? 34 : storyFontSize;
            storyLineHeight = storyLineHeight <= 0f ? 92f : storyLineHeight;
            lineRevealSeconds = lineRevealSeconds <= 0f ? 1.15f : lineRevealSeconds;
            transitionSeconds = transitionSeconds <= 0f ? 1.4f : transitionSeconds;
            return this;
        }
    }

    [Serializable]
    private sealed class IntroStoryPage
    {
        public string[] lines = Array.Empty<string>();
    }

    private enum IntroState
    {
        Landing,
        Story,
        AwaitingVideoClick,
        Video,
        Transition
    }
}
