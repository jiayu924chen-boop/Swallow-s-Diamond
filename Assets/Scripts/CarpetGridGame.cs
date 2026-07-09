using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class CarpetGridGame : MonoBehaviour
{
    private const int LevelCount = 99;
    private const string SaveKey = "carpet-grid-unity-levels-v1";
    private const string LevelFolderName = "levels";
    private const string GameArtConfigPath = "Art/game_art_config.json";
    private const string BoardVisualConfigResourcePath = "Config/BoardVisualConfig";
    private const string GuildPopupResourceFolder = "Prefabs/Guild";
    private const string GuildPopupSaveKeyPrefix = "swallow-diamond-guild-popup-";
    private const float DragStartThresholdPixels = 10f;
    private const float DragRetryInterval = 0.08f;
    private const float MoveAnimationDuration = 0.18f;
    private const float UndoMoveAnimationDuration = 0.09f;
    private const float DownAnimationDuration = 0.55f;
    private const float TargetAnimationDuration = 2f;
    private const float ActiveCarpetScale = 1.08f;
    private const int ProceduralBoardCellSize = 64;
    private const float DiamondInsetScale = 0.82f;
    private const string DiamondPrefabResourcePath = "DiamondPrefab/DiamondPrefab";
    private const string LevelTitleLabel = "level";
    private const float LevelTitleArtHeight = 126f;

    private readonly string[] palette =
    {
        "#e85d64", "#2a9d8f", "#457b9d", "#f4a261", "#8b6fd8",
        "#d66ba0", "#00a6a6", "#b56576", "#118ab2", "#6a994e"
    };

    private readonly string[] paletteNames =
    {
        "红", "绿", "蓝", "橙", "紫", "粉", "翠", "砖", "青", "橄榄"
    };

    private Font uiFont;
    private Font pixelUiFont;
    private RectTransform leftListContent;
    private RectTransform boardContent;
    private RectTransform levelListContent;
    private Text modeTitle;
    private Text activeHint;
    private Text boardInfo;
    private Text paintedText;
    private Text carpetText;
    private Text toastText;
    private Text currentLevelText;
    private Image rootBackgroundImage;
    private RectTransform levelTitleDigits;
    private InputField colsInput;
    private InputField rowsInput;
    private InputField carpetCountInput;
    private InputField lengthInput;
    private InputField levelInput;
    private Button carpetToolButton;
    private Button targetToolButton;
    private Button editModeButton;
    private Button playModeButton;
    private Sprite levelWordSprite;
    private readonly Sprite[] levelDigitSprites = new Sprite[10];
    private int renderedLevelTitle = int.MinValue;

    [Header("Art Resources")]
    public Sprite sceneBackgroundSprite;
    public Sprite boardBackgroundSprite;
    public Sprite boardCellSprite;
    public Sprite carpetSprite;
    public Sprite targetSprite;
    private Sprite targetCornerTrianglesSprite;
    private Sprite diamondHighlightSprite;
    private Sprite backIconSprite;
    private Sprite restartIconSprite;
    private GameObject diamondPrefab;
    private Vector2 playBoardFrameSize = Vector2.zero;
    public Color sceneBackgroundColor = new Color(0.96f, 0.94f, 0.90f, 1f);
    public Color boardBackgroundColor = new Color(0.93f, 0.90f, 0.86f, 1f);
    public Color emptyCellColor = new Color(1f, 0.99f, 0.97f, 1f);

    private readonly GameState state = new GameState();
    private readonly Dictionary<int, LevelData> savedLevels = new Dictionary<int, LevelData>();
    private readonly Dictionary<int, CarpetMotion> pendingMotions = new Dictionary<int, CarpetMotion>();
    private readonly Dictionary<int, DiamondAnimationTrigger> pendingDiamondAnimations = new Dictionary<int, DiamondAnimationTrigger>();
    private readonly Dictionary<string, float> pendingPathReveals = new Dictionary<string, float>();
    private readonly Dictionary<string, float> activePathDiamondAnimations = new Dictionary<string, float>();
    private readonly Dictionary<string, Sprite> proceduralBoardCellSprites = new Dictionary<string, Sprite>();
    private bool renderQueued;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneLoaded()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == CarpetLevelFlow.GameSceneName)
        {
            SceneManager.SetActiveScene(scene);
            EnsureGameExists();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (SceneManager.GetActiveScene().name != CarpetLevelFlow.GameSceneName)
        {
            return;
        }
        EnsureGameExists();
    }

    private static void EnsureGameExists()
    {
        if (FindObjectOfType<CarpetGridGame>() != null)
        {
            return;
        }

        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(eventSystem);
        }

        GameObject host = new GameObject("Carpet Grid Unity Demo");
        host.AddComponent<CarpetGridGame>();
    }

    private void Awake()
    {
        CarpetBgmPlayer.EnsurePlaying();
        int requestedLevel = CarpetLevelFlow.ConsumeRequestedLevel();
        if (requestedLevel <= 0)
        {
            CarpetLevelFlow.ReturnToMenu();
            Destroy(gameObject);
            return;
        }

        uiFont = LoadUiFont();
        pixelUiFont = LoadPixelUiFont();
        state.mode = GameMode.Play;
        ApplyGameArtConfig(requestedLevel);
        LoadLevelDisplaySprites();
        LoadSavedLevels();
        BuildUi();
        if (savedLevels.ContainsKey(requestedLevel))
        {
            if (LoadLevel(requestedLevel))
            {
                TryShowGuildPopupForLevel(requestedLevel);
            }
        }
        else
        {
            ClearLevelView("Level JSON not found: " + requestedLevel);
        }
    }

    private void Update()
    {
        UpdatePathRevealTimers();

        if (state.pointerDown && !Input.GetMouseButton(0))
        {
            bool won = state.victory;
            ResetDrag();
            if (!won)
            {
                RequestRender();
            }
            return;
        }

        if (state.pointerDown && state.dragging)
        {
            TryMoveTowardHover();
        }
    }

    private void LateUpdate()
    {
        if (!renderQueued || boardContent == null)
        {
            return;
        }

        renderQueued = false;
        Render();
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
        rootBackgroundImage = root.gameObject.AddComponent<Image>();
        ApplyRootBackground();
        if (Application.isPlaying)
        {
            BuildMinimalGameUi(root);
            return;
        }
        VerticalLayoutGroup rootLayout = root.gameObject.AddComponent<VerticalLayoutGroup>();
        rootLayout.padding = new RectOffset(18, 18, 18, 18);
        rootLayout.spacing = 12;
        rootLayout.childControlWidth = true;
        rootLayout.childControlHeight = true;
        rootLayout.childForceExpandWidth = true;
        rootLayout.childForceExpandHeight = false;

        RectTransform center = CreatePanel(root, "棋盘区", -1, -1);
        EnsureLayoutElement(center.gameObject).flexibleHeight = 1;
        EnsureLayoutElement(center.gameObject).preferredHeight = 1240;
        BuildCenterPanel(center);

        RectTransform bottomRow = AddRect("BottomPanels", root);
        HorizontalLayoutGroup bottomLayout = bottomRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        bottomLayout.spacing = 12;
        bottomLayout.childControlWidth = true;
        bottomLayout.childControlHeight = true;
        bottomLayout.childForceExpandWidth = true;
        bottomLayout.childForceExpandHeight = true;
        LayoutElement bottomLayoutElement = bottomRow.gameObject.AddComponent<LayoutElement>();
        bottomLayoutElement.preferredHeight = 600;
        bottomLayoutElement.flexibleHeight = 0;

        RectTransform left = CreatePanel(bottomRow, "编辑栏", -1, -1);
        EnsureLayoutElement(left.gameObject).flexibleWidth = 1;
        BuildLevelInfoPanel(left);

        RectTransform right = CreatePanel(bottomRow, "模式栏", -1, -1);
        EnsureLayoutElement(right.gameObject).flexibleWidth = 1;
        BuildLevelListPanel(right);
    }

    private void BuildMinimalGameUi(RectTransform root)
    {
        RectTransform levelTitle = AddRect("LevelTitle", root);
        levelTitle.anchorMin = new Vector2(0.5f, 1f);
        levelTitle.anchorMax = new Vector2(0.5f, 1f);
        levelTitle.pivot = new Vector2(0.5f, 1f);
        levelTitle.anchoredPosition = new Vector2(0, -28);
        levelTitle.sizeDelta = new Vector2(700, 136);
        if (levelWordSprite != null && levelDigitSprites.Any(sprite => sprite != null))
        {
            BuildLevelTitleImages(levelTitle);
        }
        else
        {
            currentLevelText = AddText(levelTitle, LevelTitleLabel, 54, FontStyle.Bold, Hex("#2b2b2b"));
            ApplyPixelLevelTitleStyle(currentLevelText);
            Fill(currentLevelText.rectTransform);
        }

        Button backButton = AddIconOnlyButton(root, "Back", Hex("#6a994e"), CarpetLevelFlow.ReturnToMenu, 0);
        RectTransform backRect = backButton.transform as RectTransform;
        backRect.anchorMin = new Vector2(0f, 1f);
        backRect.anchorMax = new Vector2(0f, 1f);
        backRect.pivot = new Vector2(0f, 1f);
        backRect.anchoredPosition = new Vector2(26, -34);
        backRect.sizeDelta = new Vector2(186, 116);

        Button restartButton = AddIconOnlyButton(root, "Restart", Hex("#b56576"), RestartCurrentLevel, 1);
        RectTransform restartRect = restartButton.transform as RectTransform;
        restartRect.anchorMin = new Vector2(1f, 1f);
        restartRect.anchorMax = new Vector2(1f, 1f);
        restartRect.pivot = new Vector2(1f, 1f);
        restartRect.anchoredPosition = new Vector2(-34, -20);
        restartRect.sizeDelta = new Vector2(138, 154);

        RectTransform boardFrame = AddRect("BoardFrame", root);
        boardFrame.anchorMin = new Vector2(0.5f, 0.5f);
        boardFrame.anchorMax = new Vector2(0.5f, 0.5f);
        boardFrame.pivot = new Vector2(0.5f, 0.5f);
        boardFrame.anchoredPosition = new Vector2(0, -28);
        boardFrame.sizeDelta = new Vector2(1040, 1680);
        playBoardFrameSize = boardFrame.sizeDelta;

        RectTransform scroll = CreateBoardScroll(boardFrame);
        Stretch(scroll);

        backRect.SetAsLastSibling();
        restartRect.SetAsLastSibling();
    }

    private void BuildLevelTitleImages(RectTransform parent)
    {
        HorizontalLayoutGroup layout = parent.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.spacing = 10f;

        RectTransform wordRect = AddRect("LevelWord", parent);
        Image wordImage = wordRect.gameObject.AddComponent<Image>();
        wordImage.sprite = levelWordSprite;
        wordImage.color = Color.white;
        wordImage.preserveAspect = true;
        wordImage.raycastTarget = false;
        LayoutElement wordLayout = wordRect.gameObject.AddComponent<LayoutElement>();
        wordLayout.preferredWidth = SpriteWidthForHeight(levelWordSprite, LevelTitleArtHeight, 348);
        wordLayout.preferredHeight = LevelTitleArtHeight;

        levelTitleDigits = AddRect("LevelDigits", parent);
        HorizontalLayoutGroup digitLayout = levelTitleDigits.gameObject.AddComponent<HorizontalLayoutGroup>();
        digitLayout.childAlignment = TextAnchor.MiddleCenter;
        digitLayout.childControlWidth = true;
        digitLayout.childControlHeight = true;
        digitLayout.childForceExpandWidth = false;
        digitLayout.childForceExpandHeight = false;
        digitLayout.spacing = -6f;
        LayoutElement digitsLayout = levelTitleDigits.gameObject.AddComponent<LayoutElement>();
        digitsLayout.preferredHeight = LevelTitleArtHeight;
    }

    private void BuildLevelInfoPanel(RectTransform parent)
    {
        VerticalLayoutGroup layout = parent.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 8;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        AddLabel(parent, "Level Mode", 24, FontStyle.Bold, Hex("#2b2b2b"), 32);
        AddLabel(parent, "Select a numeric JSON level from the list, then drag carpets across adjacent cells.", 14, FontStyle.Normal, Hex("#6b675f"), 48);

        RectTransform listScroll = CreateScroll(parent, "CarpetListScroll", out leftListContent);
        EnsureLayoutElement(listScroll.gameObject).flexibleHeight = 1;
    }

    private void BuildLeftPanel(RectTransform parent)
    {
        VerticalLayoutGroup layout = parent.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 8;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        AddLabel(parent, "地毯编辑", 24, FontStyle.Bold, Hex("#2b2b2b"), 32);
        AddLabel(parent, "选颜色与长度后，在棋盘点格放置。棋盘内拖动为游玩移动。", 14, FontStyle.Normal, Hex("#6b675f"), 42);

        RectTransform gridControls = AddRect("BoardControls", parent);
        GridLayoutGroup fields = gridControls.gameObject.AddComponent<GridLayoutGroup>();
        fields.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        fields.constraintCount = 2;
        fields.cellSize = new Vector2(145, 54);
        fields.spacing = new Vector2(8, 8);
        gridControls.gameObject.AddComponent<LayoutElement>().preferredHeight = 118;
        colsInput = AddLabeledInput(gridControls, "列", "8");
        rowsInput = AddLabeledInput(gridControls, "行", "8");
        carpetCountInput = AddLabeledInput(gridControls, "地毯数", "3");
        lengthInput = AddLabeledInput(gridControls, "长度", "4");

        RectTransform actionRow = AddRect("ActionRow", parent);
        HorizontalLayoutGroup actionLayout = actionRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        actionLayout.spacing = 8;
        actionLayout.childControlWidth = true;
        actionLayout.childControlHeight = true;
        actionLayout.childForceExpandWidth = true;
        actionLayout.childForceExpandHeight = false;
        actionRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 38;
        AddButton(actionRow, "生成棋盘", Hex("#2a9d8f"), CreateBoard);
        AddButton(actionRow, "重置染色", Hex("#8b6f47"), () => ResetPaintToCarpetPositions("已恢复为当前地毯所在格。"));

        AddLabel(parent, "颜色", 15, FontStyle.Bold, Hex("#4e4a43"), 24);
        RectTransform paletteRow = AddRect("Palette", parent);
        GridLayoutGroup paletteLayout = paletteRow.gameObject.AddComponent<GridLayoutGroup>();
        paletteLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        paletteLayout.constraintCount = 5;
        paletteLayout.cellSize = new Vector2(54, 32);
        paletteLayout.spacing = new Vector2(8, 8);
        paletteRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 72;
        for (int i = 0; i < palette.Length; i++)
        {
            int index = i;
            Button swatch = AddIconButton(paletteRow, "", Hex(palette[i]), () =>
            {
                state.editColorIndex = index;
                Render();
            });
            Text label = AddText(swatch.transform as RectTransform, paletteNames[i], 13, FontStyle.Bold, Color.white);
            Fill(label.rectTransform, 2);
        }

        RectTransform toolRow = AddRect("ToolRow", parent);
        HorizontalLayoutGroup toolLayout = toolRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        toolLayout.spacing = 8;
        toolLayout.childControlWidth = true;
        toolLayout.childControlHeight = true;
        toolLayout.childForceExpandWidth = true;
        toolLayout.childForceExpandHeight = false;
        toolRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 42;
        carpetToolButton = AddButton(toolRow, "放置地毯", Hex("#4f7cac"), () =>
        {
            state.editTool = EditTool.Carpet;
            RequestRender();
        });
        targetToolButton = AddButton(toolRow, "放置目标", Hex("#b56576"), () =>
        {
            state.editTool = EditTool.Target;
            Render();
        });

        RectTransform listScroll = CreateScroll(parent, "CarpetListScroll", out leftListContent);
        EnsureLayoutElement(listScroll.gameObject).flexibleHeight = 1;
    }

    private void BuildCenterPanel(RectTransform parent)
    {
        VerticalLayoutGroup layout = parent.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 10;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        RectTransform header = AddRect("Status", parent);
        HorizontalLayoutGroup headerLayout = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 16;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandHeight = false;
        header.gameObject.AddComponent<LayoutElement>().preferredHeight = 62;

        RectTransform titleBox = AddRect("TitleBox", header);
        titleBox.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        VerticalLayoutGroup titleLayout = titleBox.gameObject.AddComponent<VerticalLayoutGroup>();
        titleLayout.childControlHeight = true;
        titleLayout.childForceExpandHeight = false;
        modeTitle = AddLabel(titleBox, "编辑模式", 26, FontStyle.Bold, Hex("#2b2b2b"), 32);
        activeHint = AddLabel(titleBox, "未进入关卡", 14, FontStyle.Normal, Hex("#716b62"), 24);

        RectTransform stats = AddRect("Stats", header);
        stats.gameObject.AddComponent<LayoutElement>().preferredWidth = 230;
        VerticalLayoutGroup statsLayout = stats.gameObject.AddComponent<VerticalLayoutGroup>();
        statsLayout.childControlHeight = true;
        statsLayout.childForceExpandHeight = false;
        paintedText = AddLabel(stats, "染色 0", 15, FontStyle.Bold, Hex("#4e4a43"), 24);
        carpetText = AddLabel(stats, "地毯 0", 15, FontStyle.Bold, Hex("#4e4a43"), 24);

        boardInfo = AddLabel(parent, "棋盘未载入", 15, FontStyle.Normal, Hex("#6b675f"), 28);

        RectTransform scroll = CreateBoardScroll(parent);
        EnsureLayoutElement(scroll.gameObject).flexibleHeight = 1;

        toastText = AddLabel(parent, "", 16, FontStyle.Bold, Hex("#6a4c93"), 28);
    }

    private void BuildLevelListPanel(RectTransform parent)
    {
        VerticalLayoutGroup layout = parent.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 8;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        AddLabel(parent, "Game", 22, FontStyle.Bold, Hex("#2b2b2b"), 30);
        levelInput = AddLabeledInput(parent, "Level", "1");

        RectTransform actions = AddRect("LevelActions", parent);
        HorizontalLayoutGroup actionLayout = actions.gameObject.AddComponent<HorizontalLayoutGroup>();
        actionLayout.spacing = 8;
        actionLayout.childControlWidth = true;
        actionLayout.childControlHeight = true;
        actionLayout.childForceExpandWidth = true;
        actionLayout.childForceExpandHeight = false;
        actions.gameObject.AddComponent<LayoutElement>().preferredHeight = 38;
        AddButton(actions, "Menu", Hex("#6a994e"), CarpetLevelFlow.ReturnToMenu);
        AddButton(actions, "Refresh", Hex("#457b9d"), RefreshJsonLevels);

        EnsureLayoutElement(AddButton(parent, "Reset Level", Hex("#8b6f47"), ResetCurrentLevel).gameObject).preferredHeight = 38;
        EnsureLayoutElement(AddButton(parent, "Restart", Hex("#b56576"), RestartCurrentLevel).gameObject).preferredHeight = 38;

        currentLevelText = AddLabel(parent, "Current: none", 15, FontStyle.Bold, Hex("#4e4a43"), 28);
        AddLabel(parent, "关卡由菜单按钮配置进入。", 14, FontStyle.Normal, Hex("#6b675f"), 48);
        levelListContent = null;
    }

    private void BuildRightPanel(RectTransform parent)
    {
        VerticalLayoutGroup layout = parent.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 8;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        AddLabel(parent, "模式与关卡", 22, FontStyle.Bold, Hex("#2b2b2b"), 30);
        RectTransform modeRow = AddRect("ModeRow", parent);
        HorizontalLayoutGroup modeLayout = modeRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        modeLayout.spacing = 8;
        modeLayout.childControlWidth = true;
        modeLayout.childControlHeight = true;
        modeLayout.childForceExpandWidth = true;
        modeLayout.childForceExpandHeight = false;
        modeRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
        editModeButton = AddButton(modeRow, "编辑", Hex("#2a9d8f"), () => SetMode(GameMode.Edit));
        playModeButton = AddButton(modeRow, "游玩", Hex("#457b9d"), () => SetMode(GameMode.Play));

        levelInput = AddLabeledInput(parent, "关卡号", "1");
        RectTransform editActions = AddRect("LevelActions", parent);
        HorizontalLayoutGroup actionLayout = editActions.gameObject.AddComponent<HorizontalLayoutGroup>();
        actionLayout.spacing = 8;
        actionLayout.childControlWidth = true;
        actionLayout.childControlHeight = true;
        actionLayout.childForceExpandWidth = true;
        actionLayout.childForceExpandHeight = false;
        editActions.gameObject.AddComponent<LayoutElement>().preferredHeight = 38;
        AddButton(editActions, "载入", Hex("#6a994e"), () => LoadLevel(ReadInt(levelInput, 1)));
        AddButton(editActions, "保存", Hex("#2a9d8f"), SaveCurrentLevel);

        EnsureLayoutElement(AddButton(parent, "重置当前关", Hex("#8b6f47"), ResetCurrentLevel).gameObject).preferredHeight = 38;
        EnsureLayoutElement(AddButton(parent, "重开/恢复初始", Hex("#b56576"), RestartCurrentLevel).gameObject).preferredHeight = 38;

        currentLevelText = AddLabel(parent, "当前：未进入", 15, FontStyle.Bold, Hex("#4e4a43"), 28);
        AddLabel(parent, "游玩关卡", 15, FontStyle.Bold, Hex("#4e4a43"), 24);
        RectTransform levelScroll = CreateScroll(parent, "LevelListScroll", out levelListContent, false);
        EnsureLayoutElement(levelScroll.gameObject).flexibleHeight = 1;
    }

    private RectTransform CreateBoardScroll(RectTransform parent)
    {
        RectTransform scroll = AddRect("BoardScroll", parent);
        Image bg = scroll.gameObject.AddComponent<Image>();
        bg.color = Color.clear;
        bg.sprite = null;
        bg.raycastTarget = false;
        scroll.gameObject.AddComponent<RectMask2D>();
        ScrollRect scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        RectTransform viewport = AddRect("Viewport", scroll);
        Stretch(viewport);
        Image viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();

        boardContent = AddRect("BoardContent", viewport);
        boardContent.anchorMin = new Vector2(0.5f, 0.5f);
        boardContent.anchorMax = new Vector2(0.5f, 0.5f);
        boardContent.pivot = new Vector2(0.5f, 0.5f);
        boardContent.anchoredPosition = Vector2.zero;

        scrollRect.viewport = viewport;
        scrollRect.content = boardContent;
        return scroll;
    }

    private void CreateBoard()
    {
        int cols = Mathf.Clamp(ReadInt(colsInput, 8), 1, 99);
        int rows = Mathf.Clamp(ReadInt(rowsInput, 8), 1, 99);
        int count = Mathf.Clamp(ReadInt(carpetCountInput, 3), 1, Mathf.Min(99, rows * cols));
        int length = Mathf.Clamp(ReadInt(lengthInput, 4), 0, 99);

        state.cols = cols;
        state.rows = rows;
        if (colsInput != null) colsInput.text = cols.ToString();
        if (rowsInput != null) rowsInput.text = rows.ToString();
        if (carpetCountInput != null) carpetCountInput.text = count.ToString();
        if (lengthInput != null) lengthInput.text = length.ToString();
        state.cells = MakeCells(rows, cols);
        state.carpets.Clear();
        state.editLastCarpetByColor.Clear();

        HashSet<string> used = new HashSet<string>();
        for (int i = 0; i < count; i++)
        {
            Vector2Int start = FindUnusedStart(used, rows, cols, i);
            Vector2Int target = CreateTargetFromWalk(start, length, rows, cols);
            Carpet carpet = new Carpet
            {
                id = GetNextCarpetId(),
                row = start.x,
                col = start.y,
                targetRow = target.x,
                targetCol = target.y,
                length = length,
                color = palette[i % palette.Length],
                groupId = "",
                passColor = "",
                alive = true
            };
            state.carpets.Add(carpet);
        }

        ResetRuntimeFlags();
        PaintCarpetStarts();
        SetToast("");
        RequestRender();
    }

    private Vector2Int FindUnusedStart(HashSet<string> used, int rows, int cols, int index)
    {
        for (int attempt = 0; attempt < rows * cols * 2; attempt++)
        {
            int row = UnityEngine.Random.Range(0, rows);
            int col = UnityEngine.Random.Range(0, cols);
            string key = CellKey(row, col);
            if (!used.Contains(key))
            {
                used.Add(key);
                return new Vector2Int(row, col);
            }
        }

        int fallback = index % (rows * cols);
        return new Vector2Int(fallback / cols, fallback % cols);
    }

    private Vector2Int CreateTargetFromWalk(Vector2Int start, int length, int rows, int cols)
    {
        Vector2Int[] directions =
        {
            Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
        };

        Vector2Int best = start;
        for (int attempt = 0; attempt < 80; attempt++)
        {
            Vector2Int current = start;
            HashSet<string> visited = new HashSet<string> { CellKey(current.x, current.y) };
            bool complete = true;
            for (int step = 0; step < length; step++)
            {
                List<Vector2Int> candidates = directions
                    .Select(d => current + d)
                    .Where(p => p.x >= 0 && p.y >= 0 && p.x < rows && p.y < cols && !visited.Contains(CellKey(p.x, p.y)))
                    .ToList();
                if (candidates.Count == 0)
                {
                    complete = false;
                    break;
                }
                current = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                visited.Add(CellKey(current.x, current.y));
            }

            if (complete)
            {
                return current;
            }
            best = current;
        }
        return best;
    }

    private void ClearLevelView(string message)
    {
        state.currentLevel = 0;
        state.cells.Clear();
        state.carpets.Clear();
        state.editLastCarpetByColor.Clear();
        ResetRuntimeFlags();
        SetToast(message);
        Render();
    }

    private void SetMode(GameMode mode)
    {
        state.mode = GameMode.Play;
        ClearLevelView("");
    }

    private void SaveCurrentLevel()
    {
        if (state.cells.Count == 0)
        {
            SetToast("请先生成或载入一个关卡。");
            return;
        }

        int level = Mathf.Clamp(ReadInt(levelInput, 1), 1, LevelCount);
        state.currentLevel = level;
        savedLevels[level] = SerializeLevel();
        PersistSavedLevels();
        SetToast("关卡 " + level + " 已保存。");
        Render();
    }

    private bool LoadLevel(int level)
    {
        int targetLevel = Mathf.Max(1, level);
        if (levelInput != null) levelInput.text = targetLevel.ToString();
        if (!savedLevels.ContainsKey(targetLevel))
        {
            ClearLevelView("关卡 " + targetLevel + " 没有关卡数据。");
            return false;
        }

        state.currentLevel = targetLevel;
        ApplyLevelData(savedLevels[targetLevel]);
        SetToast("");
        return true;
    }

    private void ResetCurrentLevel()
    {
        int level = state.currentLevel > 0 ? state.currentLevel : Mathf.Max(1, ReadInt(levelInput, 1));
        if (LoadLevel(level))
        {
            SetToast("关卡 " + level + " 已重置。");
        }
    }

    private void RestartCurrentLevel()
    {
        ResetCurrentLevel();
    }

    private void TryShowGuildPopupForLevel(int level)
    {
        int popupIndex = GuildPopupIndexForLevel(level);
        if (popupIndex <= 0)
        {
            return;
        }

        string saveKey = GuildPopupSaveKeyPrefix + popupIndex.ToString("00");
        if (GuildPopup.HasConfirmed(saveKey))
        {
            return;
        }

        string prefabName = "Guild" + popupIndex.ToString("00");
        GameObject prefab = Resources.Load<GameObject>(GuildPopupResourceFolder + "/" + prefabName);
        if (prefab == null)
        {
            Debug.LogWarning("Guild popup prefab is missing: " + prefabName);
            return;
        }

        Canvas canvas = GetComponentInChildren<Canvas>(true);
        Transform parent = canvas != null ? canvas.transform : transform;
        GameObject instance = Instantiate(prefab, parent, false);
        instance.name = prefabName;
        instance.transform.SetAsLastSibling();

        RectTransform rect = instance.transform as RectTransform;
        if (rect != null)
        {
            Stretch(rect);
        }

        GuildPopup popup = instance.GetComponent<GuildPopup>();
        if (popup != null)
        {
            popup.SetSaveKey(saveKey);
        }
    }

    private static int GuildPopupIndexForLevel(int level)
    {
        switch (level)
        {
            case 1:
                return 1;
            case 4:
                return 2;
            case 7:
                return 3;
            case 10:
                return 4;
            default:
                return 0;
        }
    }

    private LevelData SerializeLevel()
    {
        LevelData data = new LevelData();
        data.rows = state.rows;
        data.cols = state.cols;
        for (int i = 0; i < state.carpets.Count; i++)
        {
            Carpet carpet = state.carpets[i];
            if (!carpet.alive)
            {
                continue;
            }
            data.carpets.Add(new CarpetSave
            {
                id = i + 1,
                row = carpet.row,
                col = carpet.col,
                targetRow = carpet.targetRow,
                targetCol = carpet.targetCol,
                length = Mathf.Clamp(carpet.length, 0, 99),
                color = string.IsNullOrEmpty(carpet.color) ? palette[i % palette.Length] : carpet.color,
                groupId = carpet.groupId ?? "",
                passColor = carpet.passColor ?? ""
            });
        }
        return data;
    }

    private void ApplyLevelData(LevelData data)
    {
        state.rows = Mathf.Clamp(data.rows, 1, 99);
        state.cols = Mathf.Clamp(data.cols, 1, 99);
        if (rowsInput != null) rowsInput.text = state.rows.ToString();
        if (colsInput != null) colsInput.text = state.cols.ToString();
        state.cells = MakeCells(state.rows, state.cols);
        state.carpets.Clear();

        int index = 0;
        foreach (CarpetSave item in data.carpets.Take(Mathf.Min(99, state.rows * state.cols)))
        {
            Carpet carpet = new Carpet
            {
                id = index + 1,
                row = Mathf.Clamp(item.row, 0, state.rows - 1),
                col = Mathf.Clamp(item.col, 0, state.cols - 1),
                targetRow = Mathf.Clamp(item.targetRow, 0, state.rows - 1),
                targetCol = Mathf.Clamp(item.targetCol, 0, state.cols - 1),
                length = Mathf.Clamp(item.length, 0, 99),
                color = string.IsNullOrEmpty(item.color) ? palette[index % palette.Length] : item.color,
                groupId = item.groupId ?? "",
                passColor = item.passColor ?? "",
                alive = true
            };
            state.carpets.Add(carpet);
            index++;
        }

        if (carpetCountInput != null) carpetCountInput.text = Mathf.Max(1, state.carpets.Count).ToString();
        if (lengthInput != null) lengthInput.text = state.carpets.Count > 0 ? state.carpets[0].length.ToString() : "1";
        RebuildEditCarpetMemory();
        ResetRuntimeFlags();
        PaintCarpetStarts();
        RefreshVictory(false);
        Render();
    }

    private void LoadSavedLevels()
    {
        savedLevels.Clear();
        foreach (string path in GetLevelJsonPaths())
        {
            int levelNumber;
            if (!TryGetNumericLevelNumber(path, out levelNumber))
            {
                continue;
            }

            LevelData data;
            string error;
            if (TryLoadLevelJson(path, out data, out error))
            {
                savedLevels[levelNumber] = data;
            }
            else
            {
                Debug.LogWarning("Failed to load level JSON " + path + ": " + error);
            }
        }
    }

    private void RefreshJsonLevels()
    {
        int current = state.currentLevel;
        LoadSavedLevels();
        if (current > 0 && savedLevels.ContainsKey(current))
        {
            LoadLevel(current);
            SetToast("Reloaded JSON levels.");
        }
        else
        {
            int firstLevel = savedLevels.Keys.OrderBy(level => level).FirstOrDefault();
            if (firstLevel > 0)
            {
                LoadLevel(firstLevel);
                SetToast("Reloaded JSON levels.");
            }
            else
            {
                ClearLevelView("No level JSON found in Assets/levels.");
            }
        }
    }

    private IEnumerable<string> GetLevelJsonPaths()
    {
        string assetLevelFolder = Path.Combine(Application.dataPath, LevelFolderName);
        if (Directory.Exists(assetLevelFolder))
        {
            foreach (string path in Directory.GetFiles(assetLevelFolder, "*.json", SearchOption.TopDirectoryOnly))
            {
                yield return path;
            }
            yield break;
        }

        string streamingLevelFolder = Path.Combine(Application.streamingAssetsPath, "Levels");
        if (Directory.Exists(streamingLevelFolder))
        {
            foreach (string path in Directory.GetFiles(streamingLevelFolder, "*.json", SearchOption.TopDirectoryOnly))
            {
                yield return path;
            }
        }
    }

    private static bool TryGetNumericLevelNumber(string path, out int levelNumber)
    {
        levelNumber = 0;
        string name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (name.All(char.IsDigit))
        {
            return int.TryParse(name, out levelNumber) && levelNumber > 0;
        }

        int end = name.Length - 1;
        while (end >= 0 && !char.IsDigit(name[end]))
        {
            end--;
        }
        if (end < 0)
        {
            return false;
        }

        int start = end;
        while (start >= 0 && char.IsDigit(name[start]))
        {
            start--;
        }

        string digits = name.Substring(start + 1, end - start);
        return int.TryParse(digits, out levelNumber) && levelNumber > 0;
    }

    private bool TryLoadLevelJson(string path, out LevelData data, out string error)
    {
        data = null;
        error = "";
        try
        {
            string json = File.ReadAllText(path);
            LevelData direct = JsonUtility.FromJson<LevelData>(json);
            if (IsUsableLevelData(direct))
            {
                data = direct;
                return true;
            }

            LevelFile wrapped = JsonUtility.FromJson<LevelFile>(json);
            if (wrapped != null && IsUsableLevelData(wrapped.level))
            {
                data = wrapped.level;
                return true;
            }

            error = "Expected LevelData JSON with rows, cols and carpets.";
            return false;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private void ApplyGameArtConfig(int requestedLevel)
    {
        string path = Path.Combine(Application.streamingAssetsPath, GameArtConfigPath);
        if (!File.Exists(path))
        {
            ApplyBoardVisualConfig();
            return;
        }

        try
        {
            GameArtConfig config = JsonUtility.FromJson<GameArtConfig>(File.ReadAllText(path));
            if (config == null)
            {
                return;
            }

            ApplyBaseArtConfig(config);
            GameArtChapterConfig chapterConfig = ResolveChapterArtConfig(config, requestedLevel);
            if (chapterConfig != null)
            {
                ApplyChapterArtConfig(chapterConfig);
            }
            ApplyBoardVisualConfig();
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Failed to read game art config: " + exception.Message);
            ApplyBoardVisualConfig();
        }
    }

    private void ApplyBaseArtConfig(GameArtConfig config)
    {
        sceneBackgroundSprite = LoadSpriteResource(config.sceneBackground);
        boardBackgroundSprite = LoadSpriteResource(config.boardBackground);
        boardCellSprite = LoadSpriteResource(config.boardCell);
        carpetSprite = LoadSpriteResource(config.carpet);
        targetSprite = LoadSpriteResource(config.target);
        backIconSprite = LoadSpriteResource(config.backIcon);
        restartIconSprite = LoadSpriteResource(config.restartIcon);
        sceneBackgroundColor = ParseColor(config.sceneBackgroundColor, sceneBackgroundColor);
        boardBackgroundColor = ParseColor(config.boardBackgroundColor, boardBackgroundColor);
        emptyCellColor = ParseColor(config.emptyCellColor, emptyCellColor);
    }

    private void ApplyChapterArtConfig(GameArtChapterConfig config)
    {
        Sprite sprite = LoadSpriteResource(config.sceneBackground);
        if (sprite != null) sceneBackgroundSprite = sprite;
        sprite = LoadSpriteResource(config.boardBackground);
        if (sprite != null) boardBackgroundSprite = sprite;
        sprite = LoadSpriteResource(config.boardCell);
        if (sprite != null) boardCellSprite = sprite;
        sprite = LoadSpriteResource(config.carpet);
        if (sprite != null) carpetSprite = sprite;
        sprite = LoadSpriteResource(config.target);
        if (sprite != null) targetSprite = sprite;
        sceneBackgroundColor = ParseColor(config.sceneBackgroundColor, sceneBackgroundColor);
        boardBackgroundColor = ParseColor(config.boardBackgroundColor, boardBackgroundColor);
        emptyCellColor = ParseColor(config.emptyCellColor, emptyCellColor);
        proceduralBoardCellSprites.Clear();
    }

    private void ApplyBoardVisualConfig()
    {
        BoardVisualConfig config = Resources.Load<BoardVisualConfig>(BoardVisualConfigResourcePath);
        if (config == null)
        {
            return;
        }

        if (config.overrideBackgroundColor)
        {
            sceneBackgroundColor = config.backgroundColor;
            boardBackgroundColor = config.backgroundColor;
        }
        if (config.backgroundSprite != null)
        {
            sceneBackgroundSprite = config.backgroundSprite;
            boardBackgroundSprite = config.backgroundSprite;
        }
        if (config.boardCellSprite != null)
        {
            boardCellSprite = config.boardCellSprite;
            proceduralBoardCellSprites.Clear();
        }
        if (config.overrideCellTint)
        {
            emptyCellColor = config.cellTint;
        }
    }

    private void LoadLevelDisplaySprites()
    {
        levelWordSprite = LoadSpriteResource("Art/LevelDisplay/level_word");
        ApplyPointFilter(levelWordSprite);
        for (int i = 0; i < levelDigitSprites.Length; i++)
        {
            levelDigitSprites[i] = LoadSpriteResource("Art/LevelDisplay/digit_" + i);
            ApplyPointFilter(levelDigitSprites[i]);
        }
        renderedLevelTitle = int.MinValue;
    }

    private static void ApplyPointFilter(Sprite sprite)
    {
        if (sprite == null || sprite.texture == null)
        {
            return;
        }

        sprite.texture.filterMode = FilterMode.Point;
        sprite.texture.wrapMode = TextureWrapMode.Clamp;
    }

    private static GameArtChapterConfig ResolveChapterArtConfig(GameArtConfig config, int requestedLevel)
    {
        if (config.chapters == null || config.chapters.Length == 0)
        {
            return null;
        }

        int requestedChapter = CarpetLevelFlow.RequestedButtonIndex;
        if (requestedChapter >= 0)
        {
            GameArtChapterConfig oneBased = config.chapters.FirstOrDefault(c => c != null && c.chapterIndex == requestedChapter + 1);
            if (oneBased != null)
            {
                return oneBased;
            }

            GameArtChapterConfig zeroBased = config.chapters.FirstOrDefault(c => c != null && c.chapterIndex == requestedChapter);
            if (zeroBased != null)
            {
                return zeroBased;
            }
        }

        if (requestedLevel > 0)
        {
            return config.chapters.FirstOrDefault(c => c != null && c.levels != null && c.levels.Contains(requestedLevel));
        }
        return null;
    }

    private static Sprite LoadSpriteResource(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        string resourcePath = Path.ChangeExtension(path.Replace('\\', '/'), null);
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

        Debug.LogWarning("Game art resource not found: " + resourcePath);
        return null;
    }

    private static Color ParseColor(string value, Color fallback)
    {
        if (!string.IsNullOrEmpty(value) && ColorUtility.TryParseHtmlString(value, out Color parsed))
        {
            return parsed;
        }
        return fallback;
    }

    private static void ApplySprite(Image image, Sprite sprite)
    {
        if (image == null || sprite == null)
        {
            return;
        }
        image.sprite = sprite;
        image.type = Image.Type.Simple;
    }

    private Sprite GetBoardCellSprite(int row, int col)
    {
        if (boardCellSprite != null)
        {
            return boardCellSprite;
        }

        string key = row + "," + col;
        if (proceduralBoardCellSprites.TryGetValue(key, out Sprite cached) && cached != null)
        {
            return cached;
        }

        Texture2D texture = new Texture2D(ProceduralBoardCellSize, ProceduralBoardCellSize, TextureFormat.RGBA32, false);
        texture.name = "GlobalWhiteMarbleCell " + key;
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;

        for (int y = 0; y < ProceduralBoardCellSize; y++)
        {
            for (int x = 0; x < ProceduralBoardCellSize; x++)
            {
                texture.SetPixel(x, y, BoardCellPixel(row, col, x, y));
            }
        }

        texture.Apply(false, true);
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0, 0, ProceduralBoardCellSize, ProceduralBoardCellSize),
            new Vector2(0.5f, 0.5f),
            ProceduralBoardCellSize);
        proceduralBoardCellSprites[key] = sprite;
        return sprite != null ? sprite : boardCellSprite;
    }

    private static Color BoardCellPixel(int row, int col, int x, int y)
    {
        int gx = col * ProceduralBoardCellSize + x;
        int gy = row * ProceduralBoardCellSize + y;
        float broadTone = 0.5f
            + Mathf.Sin((gx + gy * 0.47f) * 0.028f) * 0.12f
            + Mathf.Sin((gx * 0.31f - gy) * 0.041f) * 0.08f;
        float grain = Hash01(gx, gy, 17);
        Color color = Color.Lerp(Rgb(240, 234, 210), Rgb(255, 251, 233), Mathf.Clamp01(broadTone + (grain - 0.5f) * 0.12f));

        color = ApplyGlobalVein(color, gx, gy, 0.86f, 22f, 168f, Rgb(133, 134, 124), Rgb(208, 199, 169), 0);
        color = ApplyGlobalVein(color, gx, gy, -1.18f, 57f, 224f, Rgb(111, 115, 109), Rgb(201, 190, 163), 3);
        color = ApplyGlobalVein(color, gx, gy, 0.34f, 91f, 285f, Rgb(164, 155, 130), Rgb(228, 220, 192), 7);

        if (grain < 0.026f)
        {
            color = Color.Lerp(color, Rgb(143, 145, 134), 0.55f);
        }
        else if (grain > 0.966f)
        {
            color = Color.Lerp(color, Rgb(255, 253, 240), 0.75f);
        }
        else if (Hash01(gx, gy, 43) < 0.045f)
        {
            color = Color.Lerp(color, Rgb(209, 199, 169), 0.42f);
        }

        color = ApplyCellBorder(color, x, y);
        color = ApplyDiamondGroove(color, x, y);
        color.a = 1f;
        return color;
    }

    private static Color ApplyGlobalVein(Color color, int gx, int gy, float slope, float offset, float period, Color dark, Color warm, int salt)
    {
        float line = Mathf.Repeat(gx + gy * slope + offset, period);
        float distance = Mathf.Abs(line - period * 0.5f);
        float breakMask = Hash01(Mathf.FloorToInt((gx + gy * 0.7f) / 9f), Mathf.FloorToInt(line / 9f), salt);
        if (breakMask < 0.22f)
        {
            return color;
        }
        if (distance < 1.25f)
        {
            return Color.Lerp(color, dark, 0.32f);
        }
        if (distance < 4.25f)
        {
            return Color.Lerp(color, warm, 0.14f);
        }
        return color;
    }

    private static Color ApplyCellBorder(Color color, int x, int y)
    {
        bool outer = x == 0 || y == 0 || x == ProceduralBoardCellSize - 1 || y == ProceduralBoardCellSize - 1;
        bool inner = x == 1 || y == 1 || x == ProceduralBoardCellSize - 2 || y == ProceduralBoardCellSize - 2;
        if (outer)
        {
            return Color.Lerp(color, Rgb(17, 22, 25), 0.92f);
        }
        if (inner)
        {
            return Color.Lerp(color, Rgb(83, 87, 80), 0.72f);
        }
        return color;
    }

    private static Color ApplyDiamondGroove(Color color, int x, int y)
    {
        float center = (ProceduralBoardCellSize - 1) * 0.5f;
        float dx = x - center;
        float dy = y - center;
        float radius = ProceduralBoardCellSize * 0.42f;
        float distance = Mathf.Abs(dx) + Mathf.Abs(dy);
        float edgeDistance = Mathf.Abs(distance - radius);
        bool inside = distance < radius;

        if (inside)
        {
            float depth = Mathf.Clamp01((radius - distance) / radius);
            float lowerRight = Mathf.Clamp01((dx - dy) / ProceduralBoardCellSize + 0.5f);
            color = Color.Lerp(color, Rgb(214, 204, 174), 0.16f + depth * 0.10f);
            color = Color.Lerp(color, Rgb(125, 128, 119), lowerRight * 0.14f);
        }

        if (edgeDistance <= 3.0f)
        {
            float edge = 1f - Mathf.Clamp01(edgeDistance / 3.0f);
            bool litEdge = dy > 0f || dx < 0f;
            bool shadowEdge = dy < 0f || dx > 0f;
            if (shadowEdge)
            {
                color = Color.Lerp(color, Rgb(70, 74, 70), edge * 0.46f);
            }
            if (litEdge)
            {
                color = Color.Lerp(color, Rgb(255, 253, 239), edge * 0.72f);
            }
            if (edgeDistance < 0.85f)
            {
                color = Color.Lerp(color, Rgb(78, 82, 78), 0.34f);
            }
        }
        return color;
    }

    private static Color Rgb(int red, int green, int blue)
    {
        return new Color32((byte)red, (byte)green, (byte)blue, 255);
    }

    private static float Hash01(int x, int y, int salt)
    {
        unchecked
        {
            int hash = x * 374761393 + y * 668265263 + salt * 1442695041;
            hash = (hash ^ (hash >> 13)) * 1274126177;
            hash ^= hash >> 16;
            return (hash & 0x7fffffff) / 2147483647f;
        }
    }

    private static bool IsUsableLevelData(LevelData data)
    {
        return data != null && data.rows > 0 && data.cols > 0 && data.carpets != null && data.carpets.Count > 0;
    }

    private void PersistSavedLevels()
    {
        LevelBank bank = new LevelBank();
        foreach (KeyValuePair<int, LevelData> pair in savedLevels.OrderBy(p => p.Key))
        {
            bank.levels.Add(new LevelSlot { number = pair.Key, data = pair.Value });
        }
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(bank));
        PlayerPrefs.Save();
    }

    private List<CellData> MakeCells(int rows, int cols)
    {
        List<CellData> result = new List<CellData>(rows * cols);
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                result.Add(new CellData { row = row, col = col, color = "", owner = -1 });
            }
        }
        return result;
    }

    private void PaintCarpetStarts()
    {
        foreach (Carpet carpet in state.carpets)
        {
            if (!carpet.alive)
            {
                continue;
            }
            CellData cell = GetCell(carpet.row, carpet.col);
            if (cell == null)
            {
                continue;
            }
            cell.color = carpet.color;
            cell.owner = carpet.id;
        }
    }

    private void ResetPaintToCarpetPositions(string message)
    {
        pendingMotions.Clear();
        pendingDiamondAnimations.Clear();
        pendingPathReveals.Clear();
        activePathDiamondAnimations.Clear();
        foreach (CellData cell in state.cells)
        {
            cell.color = "";
            cell.owner = -1;
        }
        foreach (Carpet carpet in state.carpets)
        {
            carpet.history.Clear();
            carpet.steps = 0;
            carpet.hasMoveDirection = false;
            carpet.lastDirectionRow = 0;
            carpet.lastDirectionCol = 0;
        }
        PaintCarpetStarts();
        ResetDrag();
        RefreshVictory(false);
        SetToast(message);
        Render();
    }

    private void ResetRuntimeFlags()
    {
        state.activeId = 0;
        state.pointerDown = false;
        state.dragging = false;
        state.pressRow = -1;
        state.pressCol = -1;
        state.hoverRow = -1;
        state.hoverCol = -1;
        state.lastDragTarget = "";
        state.pressScreenPosition = Vector2.zero;
        state.nextDragStepTime = 0f;
        state.victory = false;
        pendingMotions.Clear();
        pendingDiamondAnimations.Clear();
        pendingPathReveals.Clear();
        activePathDiamondAnimations.Clear();
        foreach (Carpet carpet in state.carpets)
        {
            carpet.history.Clear();
            carpet.steps = 0;
            carpet.alive = true;
            carpet.hasMoveDirection = false;
            carpet.lastDirectionRow = 0;
            carpet.lastDirectionCol = 0;
        }
    }

    private void RebuildEditCarpetMemory()
    {
        state.editLastCarpetByColor.Clear();
        foreach (Carpet carpet in state.carpets)
        {
            if (carpet.alive)
            {
                state.editLastCarpetByColor[carpet.color] = carpet.id;
            }
        }
    }

    private void PlaceEditTool(int row, int col)
    {
        if (state.mode != GameMode.Edit || state.cells.Count == 0)
        {
            return;
        }

        string color = palette[state.editColorIndex];
        int editLength = Mathf.Clamp(ReadInt(lengthInput, 1), 0, 99);
        if (state.editTool == EditTool.Carpet)
        {
            if (CarpetAt(row, col, 0) != null)
            {
                SetToast("目标格已有地毯。");
                return;
            }

            Carpet carpet = new Carpet
            {
                id = GetNextCarpetId(),
                row = row,
                col = col,
                targetRow = row,
                targetCol = col,
                length = editLength,
                color = color,
                groupId = "",
                passColor = "",
                alive = true
            };
            state.carpets.Add(carpet);
            state.editLastCarpetByColor[color] = carpet.id;
            ResetPaintToCarpetPositions("地毯 #" + carpet.id + " 已放置。");
            return;
        }

        Carpet target = GetEditableCarpetForColor(color);
        if (target == null)
        {
            if (CarpetAt(row, col, 0) != null)
            {
                SetToast("目标格已有地毯。");
                return;
            }
            target = new Carpet
            {
                id = GetNextCarpetId(),
                row = row,
                col = col,
                targetRow = row,
                targetCol = col,
                length = editLength,
                color = color,
                groupId = "",
                passColor = "",
                alive = true
            };
            state.carpets.Add(target);
        }

        target.targetRow = row;
        target.targetCol = col;
        target.length = editLength;
        target.history.Clear();
        target.steps = 0;
        state.editLastCarpetByColor[color] = target.id;
        ResetPaintToCarpetPositions("目标 #" + target.id + " 已放置。");
    }

    private Carpet GetEditableCarpetForColor(string color)
    {
        if (state.editLastCarpetByColor.ContainsKey(color))
        {
            int preferredId = state.editLastCarpetByColor[color];
            Carpet preferred = state.carpets.FirstOrDefault(c => c.id == preferredId && c.alive && c.color == color);
            if (preferred != null)
            {
                return preferred;
            }
        }
        return state.carpets.LastOrDefault(c => c.alive && c.color == color);
    }

    private void SelectCarpet(int id)
    {
        if (state.victory)
        {
            return;
        }
        Carpet carpet = state.carpets.FirstOrDefault(c => c.id == id && c.alive);
        if (carpet == null)
        {
            return;
        }
        state.activeId = id;
        state.pointerDown = true;
        state.dragging = false;
        state.pressRow = carpet.row;
        state.pressCol = carpet.col;
        state.hoverRow = carpet.row;
        state.hoverCol = carpet.col;
        state.pressScreenPosition = Input.mousePosition;
        state.nextDragStepTime = 0f;
        state.lastDragTarget = "";
        SetToast("");
        RequestRender();
    }

    private void ResetDrag()
    {
        state.activeId = 0;
        state.pointerDown = false;
        state.dragging = false;
        state.lastDragTarget = "";
        state.pressRow = -1;
        state.pressCol = -1;
        state.hoverRow = -1;
        state.hoverCol = -1;
        state.pressScreenPosition = Vector2.zero;
        state.nextDragStepTime = 0f;
    }

    public void OnCellPointerDown(int row, int col, PointerEventData eventData)
    {
        Carpet carpet = CarpetAt(row, col, 0);
        if (carpet != null)
        {
            SelectCarpet(carpet.id);
            state.pressScreenPosition = eventData != null ? eventData.position : (Vector2)Input.mousePosition;
            return;
        }

        SetToast("Select a carpet, then drag through adjacent cells.");
    }

    public void OnCellPointerEnter(int row, int col, PointerEventData eventData)
    {
        if (!state.pointerDown || !Input.GetMouseButton(0))
        {
            return;
        }

        state.hoverRow = row;
        state.hoverCol = col;

        if (!state.dragging)
        {
            if (row == state.pressRow && col == state.pressCol)
            {
                return;
            }
            if (!HasDragStarted(eventData))
            {
                return;
            }
            state.dragging = true;
            state.nextDragStepTime = 0f;
        }

        if (state.dragging)
        {
            TryMoveTowardHover();
        }
    }

    public void OnCellPointerUp()
    {
        bool won = state.victory;
        ResetDrag();
        if (!won)
        {
            RequestRender();
        }
    }

    private bool HasDragStarted(PointerEventData eventData)
    {
        Vector2 currentPosition = eventData != null ? eventData.position : (Vector2)Input.mousePosition;
        return Vector2.Distance(currentPosition, state.pressScreenPosition) >= DragStartThresholdPixels;
    }

    private void TryMoveTowardHover()
    {
        Carpet carpet = GetActiveCarpet();
        if (carpet == null || state.hoverRow < 0 || state.hoverCol < 0 || Time.unscaledTime < state.nextDragStepTime)
        {
            return;
        }

        int rowDelta = state.hoverRow - carpet.row;
        int colDelta = state.hoverCol - carpet.col;
        if (rowDelta == 0 && colDelta == 0)
        {
            state.lastDragTarget = "";
            return;
        }

        int nextRow = carpet.row;
        int nextCol = carpet.col;
        if (Mathf.Abs(colDelta) > Mathf.Abs(rowDelta))
        {
            nextCol += Math.Sign(colDelta);
        }
        else if (rowDelta != 0)
        {
            nextRow += Math.Sign(rowDelta);
        }
        else
        {
            nextCol += Math.Sign(colDelta);
        }

        int beforeRow = carpet.row;
        int beforeCol = carpet.col;
        MoveActiveTo(nextRow, nextCol);
        bool moved = carpet.row != beforeRow || carpet.col != beforeCol;
        state.nextDragStepTime = Time.unscaledTime + (moved ? 0f : DragRetryInterval);
    }

    private void MoveActiveTo(int row, int col)
    {
        Carpet carpet = GetActiveCarpet();
        if (carpet == null || !state.dragging || state.victory)
        {
            return;
        }
        if (row < 0 || col < 0 || row >= state.rows || col >= state.cols)
        {
            return;
        }
        if (row == carpet.row && col == carpet.col)
        {
            state.lastDragTarget = "";
            return;
        }

        string targetKey = CellKey(row, col);
        if (state.lastDragTarget == targetKey)
        {
            return;
        }

        int distance = Mathf.Abs(row - carpet.row) + Mathf.Abs(col - carpet.col);
        if (distance != 1)
        {
            state.lastDragTarget = "";
            SetToast("只能沿上下左右相邻格移动。");
            return;
        }

        state.lastDragTarget = targetKey;
        int deltaRow = row - carpet.row;
        int deltaCol = col - carpet.col;
        List<Carpet> members = GetMoveGroup(carpet);
        HashSet<int> movingIds = new HashSet<int>(members.Select(c => c.id));
        List<MovePlan> plans = members.Select(c => new MovePlan
        {
            carpet = c,
            row = c.row + deltaRow,
            col = c.col + deltaCol
        }).ToList();
        HashSet<int> blockedIds = new HashSet<int>();
        List<string> messages = new List<string>();
        bool changed = true;

        while (changed)
        {
            changed = false;
            foreach (MovePlan plan in plans)
            {
                if (blockedIds.Contains(plan.carpet.id))
                {
                    continue;
                }
                MoveInfo info = CanMoveCarpetTo(plan.carpet, plan.row, plan.col, movingIds, blockedIds);
                if (!info.ok)
                {
                    blockedIds.Add(plan.carpet.id);
                    if (!string.IsNullOrEmpty(info.message))
                    {
                        messages.Add(info.message);
                    }
                    changed = true;
                    continue;
                }
                plan.moveInfo = info;
            }
        }

        List<MovePlan> movable = plans
            .Where(p => !blockedIds.Contains(p.carpet.id) && p.moveInfo != null)
            .OrderByDescending(p => p.carpet.row * deltaRow + p.carpet.col * deltaCol)
            .ToList();

        if (movable.Count == 0)
        {
            SetToast(messages.Count > 0 ? messages[0] : "同组地毯均被阻挡。");
            RequestRender();
            return;
        }

        List<Carpet> moved = new List<Carpet>();
        foreach (MovePlan plan in movable)
        {
            if (ExecuteCarpetMove(plan.carpet, plan.row, plan.col, plan.moveInfo))
            {
                moved.Add(plan.carpet);
            }
        }

        if (blockedIds.Count > 0 && moved.Count > 0)
        {
            SetToast("组 " + (carpet.groupId == "" ? "-" : carpet.groupId) + " 中 " + blockedIds.Count + " 块被阻挡，其余已移动。");
        }
        else
        {
            SetToast(movable.Any(p => p.moveInfo.undo) ? "已撤回一步。" : "");
        }

        ResolveMovedCarpets(moved);
        RequestRender();
    }

    private MoveInfo CanMoveCarpetTo(Carpet carpet, int row, int col, HashSet<int> movingIds, HashSet<int> blockedIds)
    {
        if (row < 0 || col < 0 || row >= state.rows || col >= state.cols)
        {
            return MoveInfo.Blocked("目标格超出棋盘。");
        }
        if (row == carpet.row && col == carpet.col)
        {
            return MoveInfo.Blocked("");
        }

        Carpet occupant = CarpetAt(row, col, carpet.id);
        bool occupantWillMove = occupant != null && movingIds.Contains(occupant.id) && !blockedIds.Contains(occupant.id);
        bool hasPassableSameColorEnd = !occupantWillMove && IsPassableSameColorEnd(occupant, carpet);
        if (occupant != null && !occupantWillMove && !hasPassableSameColorEnd)
        {
            return MoveInfo.Blocked(occupant.color == carpet.color ? "同色地毯尚未铺完，无法穿过。" : "目标格已有异色地毯。");
        }

        MoveRecord lastMove = carpet.history.Count > 0 ? carpet.history[carpet.history.Count - 1] : null;
        bool isUndo = lastMove != null && lastMove.fromRow == row && lastMove.fromCol == col;
        if (isUndo)
        {
            Carpet dependency = FindBorrowDependency(carpet, carpet.row, carpet.col);
            if (dependency != null)
            {
                return MoveInfo.Blocked("地毯 #" + dependency.id + " 正在借道该格，需先让它撤回。");
            }
            return MoveInfo.Ok(true, lastMove.cost);
        }

        CellData target = GetCell(row, col);
        if (IsDifferentColorCell(target, carpet))
        {
            return MoveInfo.Blocked("异色地块无法覆盖。");
        }
        if (target.color == carpet.color && target.owner == carpet.id && !hasPassableSameColorEnd)
        {
            return MoveInfo.Blocked("只能沿最近一步撤回。");
        }
        if (carpet.length <= 0)
        {
            return MoveInfo.Blocked("");
        }

        bool freePass = hasPassableSameColorEnd || IsPassColorCell(target, carpet) || (target.color == carpet.color && target.owner != carpet.id);
        return MoveInfo.Ok(false, freePass ? 0 : 1);
    }

    private bool ExecuteCarpetMove(Carpet carpet, int row, int col, MoveInfo info)
    {
        if (info.undo)
        {
            MoveRecord lastMove = carpet.history[carpet.history.Count - 1];
            CellData current = GetCell(carpet.row, carpet.col);
            CancelPathReveal(carpet.row, carpet.col, carpet.id);
            current.color = lastMove.previousColor ?? "";
            current.owner = lastMove.previousOwner;
            QueueCarpetMotion(carpet.id, carpet.row, carpet.col, lastMove.fromRow, lastMove.fromCol, UndoMoveAnimationDuration);
            SetLastMoveDirection(carpet, lastMove.fromRow - carpet.row, lastMove.fromCol - carpet.col);
            carpet.row = lastMove.fromRow;
            carpet.col = lastMove.fromCol;
            carpet.length += info.cost;
            carpet.steps = Mathf.Max(0, carpet.steps - info.cost);
            carpet.history.RemoveAt(carpet.history.Count - 1);
            return true;
        }

        CellData target = GetCell(row, col);
        bool movedToEmptyCell = target != null && string.IsNullOrEmpty(target.color);
        carpet.history.Add(new MoveRecord
        {
            fromRow = carpet.row,
            fromCol = carpet.col,
            toRow = row,
            toCol = col,
            previousColor = target.color,
            previousOwner = target.owner,
            borrowedColor = info.cost == 0 ? target.color : "",
            borrowedOwner = info.cost == 0 ? target.owner : -1,
            carpetId = carpet.id,
            cost = info.cost
        });

        if (info.cost > 0)
        {
            target.color = carpet.color;
            target.owner = carpet.id;
            QueuePathReveal(row, col, carpet.id);
        }

        QueueCarpetMotion(carpet.id, carpet.row, carpet.col, row, col, MoveAnimationDuration);
        SetLastMoveDirection(carpet, row - carpet.row, col - carpet.col);
        carpet.row = row;
        carpet.col = col;
        carpet.length -= info.cost;
        carpet.steps += info.cost;
        DiamondAnimationTrigger animationTrigger = DiamondAnimationTrigger.None;
        if (movedToEmptyCell)
        {
            animationTrigger = DiamondAnimationTrigger.Down;
        }
        QueueDiamondAnimation(carpet.id, animationTrigger);
        return true;
    }

    private void ResolveMovedCarpets(List<Carpet> moved)
    {
        List<Carpet> unique = moved.Distinct().ToList();
        List<Carpet> completed = new List<Carpet>();
        List<Carpet> spent = new List<Carpet>();

        foreach (Carpet carpet in unique)
        {
            if (!carpet.alive || carpet.length > 0)
            {
                continue;
            }
            if (IsAtAnySameColorTarget(carpet))
            {
                completed.Add(carpet);
            }
            else
            {
                spent.Add(carpet);
            }
        }

        RefreshVictory(true);
        if (state.victory)
        {
            return;
        }
        if (completed.Count == 1)
        {
            SetToast("地毯 #" + completed[0].id + " 已铺到目标，可撤回。");
        }
        else if (completed.Count > 1)
        {
            SetToast(completed.Count + " 块地毯已铺到目标，可撤回。");
        }
        else if (spent.Count > 0)
        {
            SetToast("地毯已铺满，可沿路径撤回调整。");
        }
    }

    private void RefreshVictory(bool announce)
    {
        List<Carpet> playable = state.carpets.Where(c => c.alive).ToList();
        bool won = playable.Count > 0 &&
            playable.All(c => c.length <= 0 && IsAtAnySameColorTarget(c)) &&
            OccupiesDistinctTargetCells(playable);
        if (won && announce && !state.victory)
        {
            QueueVictoryDiamondAnimations(playable);
            SetToast("胜利！所有地毯都铺到目标了。");
            if (state.mode == GameMode.Play)
            {
                Invoke(nameof(ReturnToLevelMenu), TargetAnimationDuration);
            }
        }
        state.victory = won;
    }

    private void QueueVictoryDiamondAnimations(IEnumerable<Carpet> carpets)
    {
        foreach (Carpet carpet in carpets)
        {
            QueueDiamondAnimation(carpet.id, DiamondAnimationTrigger.Target);
        }
    }

    private void ReturnToLevelMenu()
    {
        int nextLevel;
        if (!CarpetLevelFlow.CompleteActiveLevelAndTryGetNextLevel(out nextLevel))
        {
            return;
        }

        ApplyGameArtConfig(nextLevel);
        ApplyRootBackground();
        if (LoadLevel(nextLevel))
        {
            TryShowGuildPopupForLevel(nextLevel);
            return;
        }

        CarpetLevelFlow.ReturnToMenu();
    }

    private void ApplyRootBackground()
    {
        if (rootBackgroundImage == null)
        {
            return;
        }

        rootBackgroundImage.color = sceneBackgroundColor;
        rootBackgroundImage.sprite = sceneBackgroundSprite;
        rootBackgroundImage.type = Image.Type.Simple;
    }

    private List<Carpet> GetMoveGroup(Carpet carpet)
    {
        if (string.IsNullOrEmpty(carpet.groupId))
        {
            return new List<Carpet> { carpet };
        }
        return state.carpets.Where(c => c.alive && c.groupId == carpet.groupId).ToList();
    }

    private bool IsBorrowMove(MoveRecord move, int row, int col)
    {
        if (move == null || move.cost != 0 || move.toRow != row || move.toCol != col)
        {
            return false;
        }
        string color = string.IsNullOrEmpty(move.borrowedColor) ? move.previousColor : move.borrowedColor;
        int owner = move.borrowedOwner >= 0 ? move.borrowedOwner : move.previousOwner;
        return !string.IsNullOrEmpty(color) && owner != move.carpetId;
    }

    private Carpet FindBorrowDependency(Carpet ownerCarpet, int row, int col)
    {
        return state.carpets.FirstOrDefault(c =>
            c.alive &&
            c.id != ownerCarpet.id &&
            c.history.Any(move => IsBorrowMove(move, row, col)));
    }

    private bool IsPassableSameColorEnd(Carpet occupant, Carpet carpet)
    {
        return occupant != null && occupant.color == carpet.color && occupant.length <= 0;
    }

    private bool IsPassColorCell(CellData cell, Carpet carpet)
    {
        return cell != null &&
            !string.IsNullOrEmpty(cell.color) &&
            !string.IsNullOrEmpty(carpet.passColor) &&
            cell.color == carpet.passColor &&
            cell.color != carpet.color;
    }

    private bool IsDifferentColorCell(CellData cell, Carpet carpet)
    {
        return cell != null &&
            !string.IsNullOrEmpty(cell.color) &&
            cell.color != carpet.color &&
            !IsPassColorCell(cell, carpet);
    }

    private bool IsAtAnySameColorTarget(Carpet carpet)
    {
        return carpet != null &&
            state.carpets.Any(target =>
                target.alive &&
                target.color == carpet.color &&
                carpet.row == target.targetRow &&
                carpet.col == target.targetCol);
    }

    private static bool OccupiesDistinctTargetCells(IEnumerable<Carpet> carpets)
    {
        HashSet<string> occupiedTargets = new HashSet<string>();
        foreach (Carpet carpet in carpets)
        {
            string key = carpet.color + "|" + carpet.row + "|" + carpet.col;
            if (!occupiedTargets.Add(key))
            {
                return false;
            }
        }
        return true;
    }

    private Carpet GetActiveCarpet()
    {
        return state.carpets.FirstOrDefault(c => c.id == state.activeId && c.alive);
    }

    private CellData GetCell(int row, int col)
    {
        if (row < 0 || col < 0 || row >= state.rows || col >= state.cols)
        {
            return null;
        }
        return state.cells[row * state.cols + col];
    }

    private Carpet CarpetAt(int row, int col, int exceptId)
    {
        return state.carpets.FirstOrDefault(c => c.alive && c.id != exceptId && c.row == row && c.col == col);
    }

    private List<Carpet> CarpetsAt(int row, int col)
    {
        return state.carpets.Where(c => c.alive && c.row == row && c.col == col).OrderBy(c => c.id).ToList();
    }

    private int GetNextCarpetId()
    {
        return state.carpets.Count == 0 ? 1 : state.carpets.Max(c => c.id) + 1;
    }

    private static string CellKey(int row, int col)
    {
        return row + "," + col;
    }

    private void QueueCarpetMotion(int carpetId, int fromRow, int fromCol, int toRow, int toCol, float duration)
    {
        if (fromRow == toRow && fromCol == toCol)
        {
            return;
        }

        pendingMotions[carpetId] = new CarpetMotion
        {
            fromRow = fromRow,
            fromCol = fromCol,
            toRow = toRow,
            toCol = toCol,
            duration = Mathf.Max(0.01f, duration)
        };
    }

    private void QueueDiamondAnimation(int carpetId, DiamondAnimationTrigger trigger)
    {
        if (trigger == DiamondAnimationTrigger.None)
        {
            pendingDiamondAnimations.Remove(carpetId);
            return;
        }

        pendingDiamondAnimations[carpetId] = trigger;
    }

    private static void SetLastMoveDirection(Carpet carpet, int rowDelta, int colDelta)
    {
        if (carpet == null || (rowDelta == 0 && colDelta == 0))
        {
            return;
        }

        carpet.hasMoveDirection = true;
        carpet.lastDirectionRow = Math.Sign(rowDelta);
        carpet.lastDirectionCol = Math.Sign(colDelta);
    }

    private void QueuePathReveal(int row, int col, int owner)
    {
        string key = PathRevealKey(row, col, owner);
        pendingPathReveals.Remove(key);
        activePathDiamondAnimations[key] = Time.unscaledTime;
    }

    private void CancelPathReveal(int row, int col, int owner)
    {
        string key = PathRevealKey(row, col, owner);
        pendingPathReveals.Remove(key);
        activePathDiamondAnimations.Remove(key);
    }

    private void UpdatePathRevealTimers()
    {
        if (pendingPathReveals.Count == 0 && activePathDiamondAnimations.Count == 0)
        {
            return;
        }

        float now = Time.unscaledTime;
        List<string> readyKeys = pendingPathReveals
            .Where(pair => now >= pair.Value)
            .Select(pair => pair.Key)
            .ToList();
        List<string> completedAnimationKeys = activePathDiamondAnimations
            .Where(pair => now - pair.Value >= DownAnimationDuration)
            .Select(pair => pair.Key)
            .ToList();
        if (readyKeys.Count == 0 && completedAnimationKeys.Count == 0)
        {
            return;
        }

        foreach (string key in readyKeys)
        {
            pendingPathReveals.Remove(key);
        }

        foreach (string key in completedAnimationKeys)
        {
            activePathDiamondAnimations.Remove(key);
        }

        if (state.victory)
        {
            return;
        }

        RequestRender();
    }

    private bool IsPathRevealPending(CellData cell)
    {
        if (cell == null || cell.owner < 0)
        {
            return false;
        }

        string key = PathRevealKey(cell.row, cell.col, cell.owner);
        if (!pendingPathReveals.TryGetValue(key, out float revealAt))
        {
            return false;
        }

        if (Time.unscaledTime < revealAt)
        {
            return true;
        }

        pendingPathReveals.Remove(key);
        return false;
    }

    private static string PathRevealKey(int row, int col, int owner)
    {
        return row + "," + col + "#" + owner;
    }

    private static Vector2 CellVisualOffset(int fromRow, int fromCol, int toRow, int toCol, float pitch)
    {
        return new Vector2((fromCol - toCol) * pitch, (toRow - fromRow) * pitch);
    }

    private void Render()
    {
        RenderHeader();
        RenderBoard();
    }

    private void RequestRender()
    {
        if (boardContent == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            renderQueued = true;
            return;
        }

        Render();
    }

    private void RenderHeader()
    {
        int painted = state.cells.Count(c => !string.IsNullOrEmpty(c.color));
        int carpets = state.carpets.Count(c => c.alive);
        int unfinished = state.carpets.Count(c => c.alive && !(c.length <= 0 && IsAtAnySameColorTarget(c)));
        if (modeTitle != null) modeTitle.text = "关卡模式";
        if (activeHint != null) activeHint.text = state.victory ? "已通关" : (state.cells.Count == 0 ? "未载入关卡" : "剩余目标 " + unfinished);
        if (boardInfo != null) boardInfo.text = state.cells.Count == 0 ? "棋盘未载入" : state.cols + " x " + state.rows + " | 当前关卡 " + (state.currentLevel > 0 ? state.currentLevel.ToString() : "未载入");
        if (paintedText != null) paintedText.text = "染色 " + painted;
        if (carpetText != null) carpetText.text = "地毯 " + carpets;
        if (currentLevelText != null) currentLevelText.text = state.currentLevel > 0 ? LevelTitleLabel + " " + state.currentLevel : LevelTitleLabel;
        RenderLevelTitleSprites();
    }

    private void RenderLevelTitleSprites()
    {
        if (levelTitleDigits == null || renderedLevelTitle == state.currentLevel)
        {
            return;
        }

        renderedLevelTitle = state.currentLevel;
        ClearChildren(levelTitleDigits);

        if (state.currentLevel <= 0)
        {
            return;
        }

        string value = state.currentLevel.ToString();
        LayoutElement digitsLayout = levelTitleDigits.GetComponent<LayoutElement>();
        if (digitsLayout != null)
        {
            float preferredWidth = 0f;
            foreach (char ch in value)
            {
                int digit = ch - '0';
                if (digit >= 0 && digit < levelDigitSprites.Length)
                {
                    preferredWidth += SpriteWidthForHeight(levelDigitSprites[digit], LevelTitleArtHeight, digit == 1 ? 74 : 96);
                }
            }
            preferredWidth += Mathf.Max(0, value.Length - 1) * -6f;
            digitsLayout.preferredWidth = Mathf.Max(98, preferredWidth);
        }

        foreach (char ch in value)
        {
            int digit = ch - '0';
            if (digit < 0 || digit >= levelDigitSprites.Length || levelDigitSprites[digit] == null)
            {
                continue;
            }

            RectTransform digitRect = AddRect("Digit " + ch, levelTitleDigits);
            Image digitImage = digitRect.gameObject.AddComponent<Image>();
            digitImage.sprite = levelDigitSprites[digit];
            digitImage.color = Color.white;
            digitImage.preserveAspect = true;
            digitImage.raycastTarget = false;

            LayoutElement digitLayout = digitRect.gameObject.AddComponent<LayoutElement>();
            digitLayout.preferredWidth = SpriteWidthForHeight(levelDigitSprites[digit], LevelTitleArtHeight, digit == 1 ? 74 : 96);
            digitLayout.preferredHeight = LevelTitleArtHeight;
        }
    }

    private static float SpriteWidthForHeight(Sprite sprite, float height, float fallbackWidth)
    {
        if (sprite == null || sprite.rect.height <= 0f)
        {
            return fallbackWidth;
        }

        return sprite.rect.width / sprite.rect.height * height;
    }

    private void RenderToolButtons()
    {
        Color selected = Hex("#f4a261");
        SetButtonColor(carpetToolButton, state.editTool == EditTool.Carpet ? selected : Hex("#4f7cac"));
        SetButtonColor(targetToolButton, state.editTool == EditTool.Target ? selected : Hex("#b56576"));
        SetButtonColor(editModeButton, state.mode == GameMode.Edit ? Hex("#f4a261") : Hex("#2a9d8f"));
        SetButtonColor(playModeButton, state.mode == GameMode.Play ? Hex("#f4a261") : Hex("#457b9d"));
    }

    private void RenderBoard()
    {
        ClearChildren(boardContent);
        if (state.cells.Count == 0)
        {
            pendingMotions.Clear();
            pendingDiamondAnimations.Clear();
            pendingPathReveals.Clear();
            activePathDiamondAnimations.Clear();
            boardContent.sizeDelta = new Vector2(300, 200);
            boardContent.anchoredPosition = Vector2.zero;
            AddLabel(boardContent, "请选择数字 JSON 关卡", 22, FontStyle.Bold, Hex("#77716a"), 120);
            return;
        }

        float size = CellSize();
        float cellGap = 3f;
        float pitch = size + cellGap;
        GridLayoutGroup grid = boardContent.GetComponent<GridLayoutGroup>();
        if (grid == null)
        {
            grid = boardContent.gameObject.AddComponent<GridLayoutGroup>();
        }
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = state.cols;
        grid.cellSize = new Vector2(size, size);
        grid.spacing = new Vector2(cellGap, cellGap);
        grid.padding = new RectOffset(8, 8, 8, 8);
        boardContent.sizeDelta = new Vector2(state.cols * pitch + 20, state.rows * pitch + 20);
        boardContent.anchoredPosition = Vector2.zero;

        foreach (CellData cell in state.cells)
        {
            RectTransform cellRect = AddRect("Cell " + (cell.row + 1) + "," + (cell.col + 1), boardContent);
            Image image = cellRect.gameObject.AddComponent<Image>();
            image.color = emptyCellColor;
            ApplySprite(image, GetBoardCellSprite(cell.row, cell.col));
            CarpetGridCellView view = cellRect.gameObject.AddComponent<CarpetGridCellView>();
            view.Init(this, cell.row, cell.col);
            RenderTargetCornerTriangles(cellRect, cell.row, cell.col, size);
            RenderPathDiamond(cellRect, cell, size);

            List<Carpet> cellCarpets = CarpetsAt(cell.row, cell.col);
            for (int i = 0; i < cellCarpets.Count; i++)
            {
                Carpet carpet = cellCarpets[i];
                RectTransform piece = AddRect("Carpet #" + carpet.id, cellRect);
                piece.anchorMin = new Vector2(0.5f, 0.5f);
                piece.anchorMax = new Vector2(0.5f, 0.5f);
                piece.pivot = new Vector2(0.5f, 0.5f);
                float pieceSize = size * DiamondInsetScale;
                piece.sizeDelta = new Vector2(pieceSize, pieceSize);
                Vector2 settledPosition = new Vector2((i - (cellCarpets.Count - 1) * 0.5f) * 7, 0);
                piece.anchoredPosition = settledPosition;
                Image pieceGraphic = piece.gameObject.AddComponent<Image>();
                pieceGraphic.color = new Color(1f, 1f, 1f, 0f);
                pieceGraphic.raycastTarget = false;
                bool isActive = carpet.id == state.activeId;
                Vector2 startPosition = settledPosition;
                CarpetMotion motion;
                if (pendingMotions.TryGetValue(carpet.id, out motion))
                {
                    startPosition += CellVisualOffset(motion.fromRow, motion.fromCol, carpet.row, carpet.col, pitch);
                    pendingMotions.Remove(carpet.id);
                }
                DiamondAnimationTrigger diamondAnimation;
                if (!pendingDiamondAnimations.TryGetValue(carpet.id, out diamondAnimation))
                {
                    diamondAnimation = DiamondAnimationTrigger.None;
                }
                else
                {
                    pendingDiamondAnimations.Remove(carpet.id);
                }
                AddDiamondPrefabVisual(piece, carpet, pieceSize, false, diamondAnimation);
                float moveDuration = motion != null ? motion.duration : MoveAnimationDuration;
                piece.gameObject.AddComponent<CarpetPieceMotion>().Play(startPosition, settledPosition, moveDuration, isActive ? ActiveCarpetScale : 1f);

                if (state.mode == GameMode.Edit)
                {
                    Text id = AddText(piece, "#" + carpet.id, Mathf.Clamp((int)(size * 0.16f), 9, 12), FontStyle.Bold, Color.white);
                    id.raycastTarget = false;
                    id.rectTransform.anchorMin = new Vector2(0, 1);
                    id.rectTransform.anchorMax = new Vector2(0, 1);
                    id.rectTransform.pivot = new Vector2(0, 1);
                    id.rectTransform.anchoredPosition = new Vector2(3, -2);
                    id.rectTransform.sizeDelta = new Vector2(pieceSize, 14);
                    id.alignment = TextAnchor.UpperLeft;
                }
            }
        }
        pendingMotions.Clear();
        pendingDiamondAnimations.Clear();
    }

    private void RenderTargetCornerTriangles(RectTransform cellRect, int row, int col, float size)
    {
        foreach (Carpet target in state.carpets.Where(c => c.alive && c.targetRow == row && c.targetCol == col).OrderBy(c => c.id))
        {
            RectTransform corners = AddRect("TargetCornerTriangles #" + target.id, cellRect);
            Center(corners);
            corners.sizeDelta = new Vector2(size, size);
            Image image = corners.gameObject.AddComponent<Image>();
            Color targetColor = Hex(target.color);
            targetColor.a = 1f;
            image.color = targetColor;
            image.sprite = GetTargetCornerTrianglesSprite();
            image.raycastTarget = false;
        }
    }

    private Sprite GetTargetCornerTrianglesSprite()
    {
        if (targetCornerTrianglesSprite != null)
        {
            return targetCornerTrianglesSprite;
        }

        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        Color clear = new Color(1f, 1f, 1f, 0f);
        Color fill = Color.white;
        float center = (size - 1) * 0.5f;
        float radius = center;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float diamondDistance = Mathf.Abs(x - center) / radius + Mathf.Abs(y - center) / radius;
                texture.SetPixel(x, y, diamondDistance > 1f ? fill : clear);
            }
        }
        texture.Apply(false, true);
        targetCornerTrianglesSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return targetCornerTrianglesSprite;
    }

    private void RenderPathDiamond(RectTransform cellRect, CellData cell, float size)
    {
        if (cell == null || cell.owner < 0 || string.IsNullOrEmpty(cell.color))
        {
            return;
        }
        if (IsPathRevealPending(cell))
        {
            return;
        }

        RectTransform diamond = AddRect("PathDiamond #" + cell.owner, cellRect);
        Center(diamond);
        float diamondSize = size * DiamondInsetScale;
        diamond.sizeDelta = new Vector2(diamondSize, diamondSize);

        Carpet owner = state.carpets.FirstOrDefault(c => c.id == cell.owner);
        Vector2Int direction = owner != null ? GetCarpetDirection(owner) : new Vector2Int(0, 1);
        Vector2Int pathDirection;
        if (owner != null && TryGetPathDirection(owner, cell.row, cell.col, out pathDirection))
        {
            direction = pathDirection;
        }

        GameObject visual = AddDiamondPrefabVisual(diamond, cell.color, cell.color, diamondSize, "", owner != null, owner != null && UsesSilverDirection(owner), direction.x, direction.y, false, false);
        PlayActivePathDiamondAnimation(visual, cell);
    }

    private void AddDiamondHighlight(RectTransform parent, float size)
    {
        RectTransform highlight = AddRect("DiamondHighlight", parent);
        Center(highlight);
        highlight.sizeDelta = new Vector2(size, size);
        Image image = highlight.gameObject.AddComponent<Image>();
        image.sprite = GetDiamondHighlightSprite();
        image.color = Color.white;
        image.raycastTarget = false;
    }

    private Sprite GetDiamondHighlightSprite()
    {
        if (diamondHighlightSprite != null)
        {
            return diamondHighlightSprite;
        }

        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        Color clear = new Color(1f, 1f, 1f, 0f);
        float center = (size - 1) * 0.5f;
        float radius = size * 0.43f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float diamond = Mathf.Abs(dx) + Mathf.Abs(dy);
                Color pixel = clear;
                bool inside = diamond <= radius;
                bool topLeftFacet = inside && dx < -4f && dy > 5f && Mathf.Repeat(x, 4) < 2f;
                bool centerFacet = inside && Mathf.Abs(dx + dy) < 3.2f && dy > -8f;
                bool glint = inside && x >= 20 && x <= 30 && y >= 39 && y <= 49 && Mathf.Abs((x - 20) - (y - 39)) <= 2;
                if (topLeftFacet)
                {
                    pixel = new Color(1f, 1f, 1f, 0.22f);
                }
                if (centerFacet)
                {
                    pixel = new Color(1f, 1f, 1f, 0.16f);
                }
                if (glint)
                {
                    pixel = new Color(1f, 1f, 1f, 0.58f);
                }
                texture.SetPixel(x, y, pixel);
            }
        }

        texture.Apply(false, true);
        diamondHighlightSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return diamondHighlightSprite;
    }

    private GameObject AddDiamondPrefabVisual(RectTransform parent, Carpet carpet, float size, bool forceMainColor = false, DiamondAnimationTrigger animationTrigger = DiamondAnimationTrigger.None)
    {
        Vector2Int direction = GetCarpetDirection(carpet);
        bool showDirection = !forceMainColor;
        bool useSilver = showDirection && UsesSilverDirection(carpet);
        bool showSmallDiamond = !string.IsNullOrEmpty(carpet.passColor);
        bool showOutline = !string.IsNullOrEmpty(carpet.groupId);
        string outerColor = showSmallDiamond ? carpet.passColor : carpet.color;
        return AddDiamondPrefabVisual(
            parent,
            outerColor,
            carpet.color,
            size,
            forceMainColor || carpet.length <= 0 ? "" : carpet.length.ToString(),
            showDirection,
            useSilver,
            direction.x,
            direction.y,
            showSmallDiamond,
            showOutline,
            animationTrigger);
    }

    private GameObject AddDiamondPrefabVisual(RectTransform parent, string outerColorHex, string mainColorHex, float size, string textValue, bool showDirection, bool useSilver, int directionRow, int directionCol, bool showSmallDiamond, bool showOutline, DiamondAnimationTrigger animationTrigger = DiamondAnimationTrigger.None)
    {
        GameObject prefab = GetDiamondPrefab();
        if (prefab == null)
        {
            Image fallbackImage = parent.gameObject.AddComponent<Image>();
            fallbackImage.color = Hex(outerColorHex);
            fallbackImage.raycastTarget = false;
            ApplySprite(fallbackImage, carpetSprite);
            AddDiamondHighlight(parent, size);
            return parent.gameObject;
        }

        GameObject visual = Instantiate(prefab, parent, false);
        visual.name = string.IsNullOrEmpty(textValue) && !showDirection ? "DiamondPrefab StaticVisual" : "DiamondPrefab Visual";
        RectTransform visualRect = visual.transform as RectTransform;
        if (visualRect != null)
        {
            Center(visualRect);
            float baseSize = Mathf.Max(Mathf.Abs(visualRect.sizeDelta.x), Mathf.Abs(visualRect.sizeDelta.y));
            if (baseSize <= 0.01f)
            {
                baseSize = 64f;
            }
            visualRect.localScale = Vector3.one * (size / baseSize);
        }

        foreach (Graphic graphic in visual.GetComponentsInChildren<Graphic>(true))
        {
            graphic.raycastTarget = false;
        }

        Color outerColor = Hex(outerColorHex);
        Color mainColor = Hex(mainColorHex);
        SetDiamondImageColor(visual.transform, "10_DiamondNormal", outerColor);
        SetDiamondImageColor(visual.transform, "20_DiamondSmall", mainColor);
        SetDiamondLayerVisible(visual.transform, "20_DiamondSmall", showSmallDiamond);
        SetDiamondLayerVisible(visual.transform, "21_DiamondSmallHighlight", showSmallDiamond);
        SetDiamondLayerVisible(visual.transform, "40_Outline", showOutline);
        UpdateDiamondText(visual.transform, textValue);
        UpdateDiamondDirectionDecorations(visual.transform, showDirection, useSilver, directionRow, directionCol);
        TriggerDiamondAnimation(visual, animationTrigger);
        return visual;
    }

    private void PlayActivePathDiamondAnimation(GameObject visual, CellData cell)
    {
        if (visual == null || cell == null || cell.owner < 0)
        {
            return;
        }

        string key = PathRevealKey(cell.row, cell.col, cell.owner);
        if (!activePathDiamondAnimations.TryGetValue(key, out float startedAt))
        {
            return;
        }

        float elapsed = Time.unscaledTime - startedAt;
        if (elapsed >= DownAnimationDuration)
        {
            activePathDiamondAnimations.Remove(key);
            return;
        }

        Animator animator = visual.GetComponentInChildren<Animator>(true);
        if (animator == null)
        {
            return;
        }

        animator.Play("down", 0, Mathf.Clamp01(elapsed / DownAnimationDuration));
        animator.Update(0f);
    }

    private static void TriggerDiamondAnimation(GameObject visual, DiamondAnimationTrigger trigger)
    {
        if (visual == null || trigger == DiamondAnimationTrigger.None)
        {
            return;
        }

        Animator animator = visual.GetComponentInChildren<Animator>(true);
        if (animator == null)
        {
            return;
        }

        animator.ResetTrigger("down");
        animator.ResetTrigger("target");
        animator.SetTrigger(trigger == DiamondAnimationTrigger.Target ? "target" : "down");
    }

    private GameObject GetDiamondPrefab()
    {
        if (diamondPrefab == null)
        {
            diamondPrefab = Resources.Load<GameObject>(DiamondPrefabResourcePath);
        }

        return diamondPrefab;
    }

    private void SetDiamondImageColor(Transform root, string childName, Color color)
    {
        Transform child = FindChildRecursive(root, childName);
        if (child == null)
        {
            return;
        }

        Image image = child.GetComponent<Image>();
        if (image != null)
        {
            image.color = color;
        }
    }

    private void SetDiamondLayerVisible(Transform root, string childName, bool visible)
    {
        Transform child = FindChildRecursive(root, childName);
        if (child != null)
        {
            child.gameObject.SetActive(visible);
        }
    }

    private void UpdateDiamondText(Transform root, string textValue)
    {
        Text text = root.GetComponentsInChildren<Text>(true).FirstOrDefault();
        if (text == null)
        {
            return;
        }

        bool visible = !string.IsNullOrEmpty(textValue);
        text.gameObject.SetActive(visible);
        text.raycastTarget = false;
        text.text = visible ? textValue : "";
    }

    private void UpdateDiamondDirectionDecorations(Transform root, bool showDirection, bool useSilver, int directionRow, int directionCol)
    {
        Transform gold = FindChildRecursive(root, "30_DirectionGold");
        Transform silver = FindChildRecursive(root, "31_DirectionSilver");

        SetDirectionDecoration(gold, showDirection && !useSilver, directionRow, directionCol);
        SetDirectionDecoration(silver, showDirection && useSilver, directionRow, directionCol);
    }

    private void SetDirectionDecoration(Transform decoration, bool visible, int directionRow, int directionCol)
    {
        if (decoration == null)
        {
            return;
        }

        decoration.gameObject.SetActive(visible);
        if (!visible)
        {
            return;
        }

        decoration.localRotation = Quaternion.Euler(0f, 0f, DirectionAngle(directionRow, directionCol));
    }

    private bool UsesSilverDirection(Carpet carpet)
    {
        if (carpet == null || string.IsNullOrEmpty(carpet.color))
        {
            return false;
        }

        List<Carpet> sameColor = state.carpets
            .Where(c => c.alive && c.color == carpet.color)
            .OrderBy(c => c.id)
            .ToList();
        return sameColor.Count >= 2 && sameColor[0].id != carpet.id;
    }

    private Vector2Int GetCarpetDirection(Carpet carpet)
    {
        if (carpet == null)
        {
            return new Vector2Int(0, 1);
        }

        if (carpet.hasMoveDirection && (carpet.lastDirectionRow != 0 || carpet.lastDirectionCol != 0))
        {
            return new Vector2Int(carpet.lastDirectionRow, carpet.lastDirectionCol);
        }

        int rowDelta = carpet.targetRow - carpet.row;
        int colDelta = carpet.targetCol - carpet.col;
        if (colDelta != 0)
        {
            return new Vector2Int(0, Math.Sign(colDelta));
        }
        if (rowDelta != 0)
        {
            return new Vector2Int(Math.Sign(rowDelta), 0);
        }

        return new Vector2Int(0, 1);
    }

    private static float DirectionAngle(int rowDelta, int colDelta)
    {
        if (colDelta > 0) return 180f;
        if (rowDelta < 0) return 270f;
        if (colDelta < 0) return 0f;
        return 90f;
    }

    private static Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null)
        {
            return null;
        }
        if (root.name == childName)
        {
            return root;
        }

        foreach (Transform child in root)
        {
            Transform match = FindChildRecursive(child, childName);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    private void RenderPathOwnership(RectTransform cellRect, CellData cell, float size)
    {
        if (cell == null || cell.owner < 0 || string.IsNullOrEmpty(cell.color))
        {
            return;
        }
        if (IsPathRevealPending(cell))
        {
            return;
        }

        Carpet owner = state.carpets.FirstOrDefault(c => c.id == cell.owner);
        if (owner == null)
        {
            return;
        }

        Color ownerColor = Hex(owner.color);
        Vector2Int direction;
        if (TryGetPathDirection(owner, cell.row, cell.col, out direction))
        {
            AddPathArrow(cellRect, direction, ownerColor, size);
        }
    }

    private bool TryGetPathDirection(Carpet owner, int row, int col, out Vector2Int direction)
    {
        direction = Vector2Int.zero;
        List<Vector2Int> path = BuildOwnedPath(owner);
        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector2Int current = path[i];
            Vector2Int next = path[i + 1];
            if (current.x != row || current.y != col)
            {
                continue;
            }

            Vector2Int delta = next - current;
            if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1)
            {
                return false;
            }

            direction = delta;
            return true;
        }
        return false;
    }

    private List<Vector2Int> BuildOwnedPath(Carpet owner)
    {
        List<Vector2Int> path = new List<Vector2Int>();
        if (owner.history.Count == 0)
        {
            path.Add(new Vector2Int(owner.row, owner.col));
            return path;
        }

        path.Add(new Vector2Int(owner.history[0].fromRow, owner.history[0].fromCol));
        foreach (MoveRecord move in owner.history)
        {
            if (move.cost <= 0)
            {
                continue;
            }

            Vector2Int next = new Vector2Int(move.toRow, move.toCol);
            if (path.Count == 0 || path[path.Count - 1] != next)
            {
                path.Add(next);
            }
        }
        return path;
    }

    private void AddPathArrow(RectTransform parent, Vector2Int direction, Color ownerColor, float cellSize)
    {
        RectTransform arrow = AddRect("PathArrow", parent);
        Center(arrow);
        arrow.sizeDelta = new Vector2(Mathf.Clamp(cellSize * 0.38f, 18f, 26f), Mathf.Clamp(cellSize * 0.38f, 18f, 26f));

        Color arrowColor = ReadablePathColor(ownerColor);
        arrowColor.a = 0.58f;
        Text text = AddText(arrow, DirectionSymbol(direction), Mathf.Clamp((int)(cellSize * 0.34f), 16, 22), FontStyle.Bold, arrowColor);
        text.raycastTarget = false;
        Fill(text.rectTransform, 0);
    }

    private static string DirectionSymbol(Vector2Int direction)
    {
        if (direction.x < 0) return "↑";
        if (direction.x > 0) return "↓";
        if (direction.y < 0) return "←";
        return "→";
    }

    private void RenderCarpetList()
    {
        ClearChildren(leftListContent);
        if (leftListContent != null)
        {
            if (state.carpets.Count == 0)
            {
                AddLabel(leftListContent, "未载入关卡。", 15, FontStyle.Normal, Hex("#77716a"), 44);
                return;
            }

            foreach (Carpet carpet in state.carpets.Where(c => c.alive).OrderBy(c => c.id))
            {
                RectTransform card = AddRect("CarpetInfo " + carpet.id, leftListContent);
                Image bg = card.gameObject.AddComponent<Image>();
                bg.color = Hex("#fffdf8");
                Outline border = card.gameObject.AddComponent<Outline>();
                border.effectColor = Hex("#ded6c9");
                border.effectDistance = new Vector2(1, -1);
                VerticalLayoutGroup layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(8, 8, 8, 8);
                layout.spacing = 4;
                layout.childControlWidth = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                card.gameObject.AddComponent<LayoutElement>().preferredHeight = 96;

                RectTransform header = AddRect("Header", card);
                HorizontalLayoutGroup headerLayout = header.gameObject.AddComponent<HorizontalLayoutGroup>();
                headerLayout.spacing = 8;
                headerLayout.childAlignment = TextAnchor.MiddleLeft;
                headerLayout.childControlWidth = true;
                headerLayout.childControlHeight = true;
                headerLayout.childForceExpandHeight = false;
                header.gameObject.AddComponent<LayoutElement>().preferredHeight = 30;
                AddColorSquare(header, carpet.color, 24);
                Text title = AddLabel(header, "地毯 #" + carpet.id, 15, FontStyle.Bold, Hex("#3f3a34"), 28);
                EnsureLayoutElement(title.gameObject).flexibleWidth = 1;

                AddLabel(card, "剩余长度 " + carpet.length + " / 目标 (" + (carpet.targetCol + 1) + ", " + (carpet.targetRow + 1) + ")", 13, FontStyle.Normal, Hex("#5d5750"), 22);
                AddLabel(card, string.IsNullOrEmpty(carpet.groupId) ? "无联动组" : "联动组 " + carpet.groupId, 13, FontStyle.Normal, Hex("#5d5750"), 22);
            }
            return;
        }

        if (state.carpets.Count == 0)
        {
            AddLabel(leftListContent, "暂无地毯。生成棋盘或用工具放置。", 15, FontStyle.Normal, Hex("#77716a"), 44);
            return;
        }

        foreach (Carpet carpet in state.carpets.Where(c => c.alive).OrderBy(c => c.id))
        {
            RectTransform card = AddRect("CarpetCard " + carpet.id, leftListContent);
            Image bg = card.gameObject.AddComponent<Image>();
            bg.color = Hex("#fffdf8");
            Outline border = card.gameObject.AddComponent<Outline>();
            border.effectColor = Hex("#ded6c9");
            border.effectDistance = new Vector2(1, -1);
            VerticalLayoutGroup layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 6;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            card.gameObject.AddComponent<LayoutElement>().preferredHeight = 260;

            RectTransform header = AddRect("Header", card);
            HorizontalLayoutGroup headerLayout = header.gameObject.AddComponent<HorizontalLayoutGroup>();
            headerLayout.spacing = 8;
            headerLayout.childAlignment = TextAnchor.MiddleLeft;
            headerLayout.childControlWidth = false;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandHeight = false;
            header.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;
            AddColorSquare(header, carpet.color, 26);
            Text title = AddLabel(header, "地毯 #" + carpet.id + " / 目标 #" + carpet.id, 15, FontStyle.Bold, Hex("#3f3a34"), 30);
            EnsureLayoutElement(title.gameObject).flexibleWidth = 1;
            EnsureLayoutElement(AddButton(header, "删", Hex("#d9534f"), () => DeleteCarpet(carpet.id)).gameObject).preferredWidth = 48;

            RectTransform row1 = AddRect("Row1", card);
            HorizontalLayoutGroup rowLayout1 = row1.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout1.spacing = 6;
            rowLayout1.childControlWidth = true;
            rowLayout1.childControlHeight = true;
            rowLayout1.childForceExpandWidth = true;
            rowLayout1.childForceExpandHeight = false;
            row1.gameObject.AddComponent<LayoutElement>().preferredHeight = 54;
            AddCarpetInput(row1, "列", carpet.col + 1, value => ConfigureCarpet(carpet.id, "col", value));
            AddCarpetInput(row1, "行", carpet.row + 1, value => ConfigureCarpet(carpet.id, "row", value));
            AddCarpetInput(row1, "长", carpet.length, value => ConfigureCarpet(carpet.id, "length", value));

            RectTransform row2 = AddRect("Row2", card);
            HorizontalLayoutGroup rowLayout2 = row2.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout2.spacing = 6;
            rowLayout2.childControlWidth = true;
            rowLayout2.childControlHeight = true;
            rowLayout2.childForceExpandWidth = true;
            rowLayout2.childForceExpandHeight = false;
            row2.gameObject.AddComponent<LayoutElement>().preferredHeight = 54;
            AddCarpetInput(row2, "目标列", carpet.targetCol + 1, value => ConfigureCarpet(carpet.id, "targetCol", value));
            AddCarpetInput(row2, "目标行", carpet.targetRow + 1, value => ConfigureCarpet(carpet.id, "targetRow", value));
            AddCarpetInput(row2, "组", string.IsNullOrEmpty(carpet.groupId) ? 0 : int.Parse(carpet.groupId), value => ConfigureCarpet(carpet.id, "groupId", value));

            AddLabel(card, "可通过异色", 13, FontStyle.Bold, Hex("#5d5750"), 20);
            RectTransform passRow = AddRect("PassColors", card);
            GridLayoutGroup passLayout = passRow.gameObject.AddComponent<GridLayoutGroup>();
            passLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            passLayout.constraintCount = 6;
            passLayout.cellSize = new Vector2(42, 28);
            passLayout.spacing = new Vector2(6, 6);
            passRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;
            AddIconButton(passRow, "无", Hex("#888888"), () => ConfigurePassColor(carpet.id, ""));
            foreach (string color in palette)
            {
                string picked = color;
                if (picked == carpet.color)
                {
                    continue;
                }
                int colorIndex = Array.IndexOf(palette, picked);
                string colorLabel = colorIndex >= 0 ? paletteNames[colorIndex] : "色";
                Button button = AddIconButton(passRow, carpet.passColor == picked ? "✓" + colorLabel : colorLabel, Hex(picked), () => ConfigurePassColor(carpet.id, picked));
                if (carpet.passColor == picked)
                {
                    Outline outline = button.gameObject.AddComponent<Outline>();
                    outline.effectColor = Hex("#111111");
                    outline.effectDistance = new Vector2(2, -2);
                }
            }
        }
    }

    private void RenderLevelList()
    {
        if (levelListContent == null)
        {
            return;
        }
        ClearChildren(levelListContent);
        VerticalLayoutGroup vertical = levelListContent.gameObject.GetComponent<VerticalLayoutGroup>();
        if (vertical != null)
        {
            DestroyImmediate(vertical);
        }
        GridLayoutGroup grid = levelListContent.gameObject.GetComponent<GridLayoutGroup>();
        if (grid == null)
        {
            grid = levelListContent.gameObject.AddComponent<GridLayoutGroup>();
        }
        if (grid == null)
        {
            SetToast("关卡列表布局创建失败，请重新进入 Play。");
            return;
        }
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 5;
        grid.cellSize = new Vector2(38, 32);
        grid.spacing = new Vector2(6, 6);
        List<int> levels = savedLevels.Keys.OrderBy(level => level).ToList();
        if (levels.Count == 0)
        {
            levelListContent.sizeDelta = new Vector2(220, 80);
            AddLabel(levelListContent, "未找到数字 JSON", 14, FontStyle.Bold, Hex("#77716a"), 40);
            return;
        }

        levelListContent.sizeDelta = new Vector2(220, Mathf.Max(80, Mathf.Ceil(levels.Count / 5f) * 38 + 12));

        foreach (int level in levels)
        {
            int captured = level;
            Button button = AddIconButton(levelListContent, level.ToString(), Hex("#2a9d8f"), () =>
            {
                LoadLevel(captured);
            });
            if (state.currentLevel == level)
            {
                Outline outline = button.gameObject.AddComponent<Outline>();
                outline.effectColor = Hex("#111111");
                outline.effectDistance = new Vector2(2, -2);
            }
        }
    }

    private void ConfigureCarpet(int id, string field, int value)
    {
        Carpet carpet = state.carpets.FirstOrDefault(c => c.id == id);
        if (carpet == null)
        {
            return;
        }

        if (field == "length")
        {
            carpet.length = Mathf.Clamp(value, 0, 99);
        }
        else if (field == "groupId")
        {
            carpet.groupId = value > 0 ? Mathf.Clamp(value, 1, 99).ToString() : "";
        }
        else if (field == "targetRow")
        {
            carpet.targetRow = Mathf.Clamp(value, 1, state.rows) - 1;
        }
        else if (field == "targetCol")
        {
            carpet.targetCol = Mathf.Clamp(value, 1, state.cols) - 1;
        }
        else
        {
            int nextRow = field == "row" ? Mathf.Clamp(value, 1, state.rows) - 1 : carpet.row;
            int nextCol = field == "col" ? Mathf.Clamp(value, 1, state.cols) - 1 : carpet.col;
            Carpet occupant = CarpetAt(nextRow, nextCol, carpet.id);
            if (occupant != null)
            {
                SetToast("该格已有地毯 #" + occupant.id + "。");
                Render();
                return;
            }
            carpet.row = nextRow;
            carpet.col = nextCol;
            carpet.history.Clear();
            carpet.steps = 0;
            ResetPaintToCarpetPositions("地毯 #" + carpet.id + " 位置已更新。");
            return;
        }

        ResetPaintToCarpetPositions("地毯 #" + carpet.id + " 已更新。");
    }

    private void ConfigurePassColor(int id, string color)
    {
        Carpet carpet = state.carpets.FirstOrDefault(c => c.id == id);
        if (carpet == null)
        {
            return;
        }
        carpet.passColor = color == carpet.color ? "" : color;
        SetToast(string.IsNullOrEmpty(carpet.passColor) ? "地毯 #" + carpet.id + " 已取消异色通行。" : "地毯 #" + carpet.id + " 可通过指定异色。");
        Render();
    }

    private void DeleteCarpet(int id)
    {
        Carpet carpet = state.carpets.FirstOrDefault(c => c.id == id);
        if (carpet == null)
        {
            return;
        }
        state.carpets.Remove(carpet);
        RebuildEditCarpetMemory();
        ResetPaintToCarpetPositions("地毯与目标 #" + id + " 已删除。");
    }

    private float CellSize()
    {
        if (Application.isPlaying && playBoardFrameSize.x > 0f && playBoardFrameSize.y > 0f && state.cols > 0 && state.rows > 0)
        {
            float cellGap = 3f;
            float horizontal = (playBoardFrameSize.x - 20f - Mathf.Max(0, state.cols - 1) * cellGap) / state.cols;
            float vertical = (playBoardFrameSize.y - 20f - Mathf.Max(0, state.rows - 1) * cellGap) / state.rows;
            return Mathf.Clamp(Mathf.Floor(Mathf.Min(horizontal, vertical)), 22f, 160f);
        }

        int longest = Mathf.Max(state.cols, state.rows);
        if (longest <= 12) return 58;
        if (longest <= 24) return 42;
        if (longest <= 48) return 30;
        return 22;
    }

    private void SetToast(string message)
    {
        if (toastText != null)
        {
            toastText.text = message;
        }
    }

    private RectTransform CreatePanel(RectTransform parent, string name, float width, float height)
    {
        RectTransform panel = AddRect(name, parent);
        Image image = panel.gameObject.AddComponent<Image>();
        image.color = Hex("#fbf8f2");
        LayoutElement layout = panel.gameObject.AddComponent<LayoutElement>();
        if (width > 0) layout.preferredWidth = width;
        if (height > 0) layout.preferredHeight = height;
        return panel;
    }

    private RectTransform CreateScroll(RectTransform parent, string name, out RectTransform content, bool useVerticalContent = true)
    {
        RectTransform scroll = AddRect(name, parent);
        EnsureLayoutElement(scroll.gameObject);
        Image bg = scroll.gameObject.AddComponent<Image>();
        bg.color = Hex("#f3eee6");
        ScrollRect scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        RectTransform viewport = AddRect("Viewport", scroll);
        Stretch(viewport);
        Image viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = Hex("#f3eee6");
        Mask mask = viewport.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        content = AddRect("Content", viewport);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);
        content.anchoredPosition = Vector2.zero;
        if (useVerticalContent)
        {
            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.spacing = 8;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        scrollRect.viewport = viewport;
        scrollRect.content = content;
        return scroll;
    }

    private static LayoutElement EnsureLayoutElement(GameObject target)
    {
        LayoutElement layout = target.GetComponent<LayoutElement>();
        return layout != null ? layout : target.AddComponent<LayoutElement>();
    }

    private InputField AddLabeledInput(RectTransform parent, string label, string value)
    {
        RectTransform box = AddRect("Field " + label, parent);
        VerticalLayoutGroup layout = box.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 3;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        AddLabel(box, label, 13, FontStyle.Bold, Hex("#5d5750"), 18);
        return AddInput(box, value);
    }

    private void AddCarpetInput(RectTransform parent, string label, int value, Action<int> onEndEdit)
    {
        InputField input = AddLabeledInput(parent, label, value.ToString());
        input.onEndEdit.AddListener(text =>
        {
            int parsed;
            if (int.TryParse(text, out parsed))
            {
                onEndEdit(parsed);
            }
            else
            {
                Render();
            }
        });
    }

    private InputField AddInput(RectTransform parent, string value)
    {
        RectTransform rect = AddRect("Input", parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = Color.white;
        InputField input = rect.gameObject.AddComponent<InputField>();
        input.contentType = InputField.ContentType.IntegerNumber;
        Text text = AddText(rect, value, 15, FontStyle.Normal, Hex("#2f2b26"));
        text.alignment = TextAnchor.MiddleLeft;
        text.rectTransform.offsetMin = new Vector2(8, 0);
        text.rectTransform.offsetMax = new Vector2(-8, 0);
        Text placeholder = AddText(rect, "", 15, FontStyle.Normal, Hex("#a49d93"));
        input.textComponent = text;
        input.placeholder = placeholder;
        input.text = value;
        rect.gameObject.AddComponent<LayoutElement>().preferredHeight = 30;
        return input;
    }

    private Button AddButton(RectTransform parent, string label, Color color, Action onClick)
    {
        Button button = AddIconButton(parent, label, color, onClick);
        LayoutElement layout = button.gameObject.GetComponent<LayoutElement>();
        if (layout == null)
        {
            layout = button.gameObject.AddComponent<LayoutElement>();
        }
        layout.preferredHeight = 36;
        return button;
    }

    private Button AddIconOnlyButton(RectTransform parent, string name, Color color, Action onClick, int icon)
    {
        RectTransform rect = AddRect("IconButton " + name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0f);
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => onClick());
        rect.gameObject.AddComponent<UiPressScale>();

        RectTransform iconRect = AddRect("Icon", rect);
        Center(iconRect);
        iconRect.sizeDelta = icon == 0 ? new Vector2(178, 100) : new Vector2(132, 146);
        Sprite iconSprite = icon == 0 ? backIconSprite : restartIconSprite;
        if (iconSprite != null)
        {
            Image iconImage = iconRect.gameObject.AddComponent<Image>();
            iconImage.color = Color.white;
            iconImage.sprite = iconSprite;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
        }
        else
        {
            IconButtonGraphic graphic = iconRect.gameObject.AddComponent<IconButtonGraphic>();
            graphic.icon = icon;
            graphic.color = Hex("#2b2b2b");
            graphic.raycastTarget = false;
        }

        LayoutElement layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = 32;
        return button;
    }

    private Button AddIconButton(RectTransform parent, string label, Color color, Action onClick)
    {
        RectTransform rect = AddRect("Button " + label, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => onClick());
        Text text = AddText(rect, label, 14, FontStyle.Bold, Color.white);
        Fill(text.rectTransform, 2);
        LayoutElement layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = 32;
        return button;
    }

    private void SetButtonColor(Button button, Color color)
    {
        if (button == null)
        {
            return;
        }
        Image image = button.GetComponent<Image>();
        if (image != null)
        {
            image.color = color;
        }
    }

    private Text AddLabel(RectTransform parent, string text, int size, FontStyle style, Color color, float height)
    {
        Text label = AddText(parent, text, size, style, color);
        label.alignment = TextAnchor.MiddleLeft;
        LayoutElement layout = label.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        return label;
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

    private void ApplyPixelLevelTitleStyle(Text text)
    {
        if (text == null)
        {
            return;
        }

        text.font = pixelUiFont != null ? pixelUiFont : uiFont;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        Outline outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = Hex("#f8e7b5");
        outline.effectDistance = new Vector2(3f, -3f);

        Shadow shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = Hex("#7a5530");
        shadow.effectDistance = new Vector2(5f, -5f);
    }

    private void AddColorSquare(RectTransform parent, string color, float size)
    {
        RectTransform square = AddRect("Color", parent);
        square.gameObject.AddComponent<Image>().color = Hex(color);
        LayoutElement layout = square.gameObject.AddComponent<LayoutElement>();
        layout.preferredWidth = size;
        layout.preferredHeight = size;
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

    private static Font LoadPixelUiFont()
    {
        string[] candidates =
        {
            "Press Start 2P",
            "Cascadia Mono",
            "Consolas",
            "Courier New",
            "Arial"
        };

        Font font = Font.CreateDynamicFontFromOSFont(candidates, 16);
        return font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private static void ClearChildren(Transform parent)
    {
        if (parent == null)
        {
            return;
        }

        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            GameObject child = parent.GetChild(i).gameObject;
            if (Application.isPlaying)
            {
                child.SetActive(false);
                child.transform.SetParent(null, false);
                Destroy(child, 1f);
            }
            else
            {
                DestroyImmediate(child);
            }
        }
    }

    private int ReadInt(InputField input, int fallback)
    {
        if (input == null)
        {
            return fallback;
        }
        int value;
        return int.TryParse(input.text, out value) ? value : fallback;
    }

    private static Color Hex(string hex)
    {
        if (string.IsNullOrEmpty(hex))
        {
            return Color.clear;
        }
        Color color;
        if (ColorUtility.TryParseHtmlString(hex, out color))
        {
            return color;
        }
        return Color.white;
    }

    private static Color Tint(Color color, float amount)
    {
        return Color.Lerp(color, Color.white, 1f - amount);
    }

    private static Color ReadablePathColor(Color baseColor)
    {
        float luminance = baseColor.r * 0.299f + baseColor.g * 0.587f + baseColor.b * 0.114f;
        return luminance > 0.58f ? Color.black : Color.white;
    }

    [Serializable]
    private sealed class GameState
    {
        public GameMode mode = GameMode.Edit;
        public int rows = 8;
        public int cols = 8;
        public int currentLevel;
        public List<CellData> cells = new List<CellData>();
        public List<Carpet> carpets = new List<Carpet>();
        public int activeId;
        public bool pointerDown;
        public bool dragging;
        public int pressRow = -1;
        public int pressCol = -1;
        public int hoverRow = -1;
        public int hoverCol = -1;
        public Vector2 pressScreenPosition;
        public float nextDragStepTime;
        public string lastDragTarget = "";
        public bool victory;
        public EditTool editTool = EditTool.Carpet;
        public int editColorIndex;
        public Dictionary<string, int> editLastCarpetByColor = new Dictionary<string, int>();
    }

    [Serializable]
    private sealed class CellData
    {
        public int row;
        public int col;
        public string color = "";
        public int owner = -1;
    }

    [Serializable]
    private sealed class Carpet
    {
        public int id;
        public int row;
        public int col;
        public int targetRow;
        public int targetCol;
        public int length;
        public string color = "";
        public string groupId = "";
        public string passColor = "";
        public bool alive = true;
        public int steps;
        public bool hasMoveDirection;
        public int lastDirectionRow;
        public int lastDirectionCol;
        public List<MoveRecord> history = new List<MoveRecord>();
    }

    [Serializable]
    private sealed class MoveRecord
    {
        public int fromRow;
        public int fromCol;
        public int toRow;
        public int toCol;
        public string previousColor = "";
        public int previousOwner = -1;
        public string borrowedColor = "";
        public int borrowedOwner = -1;
        public int carpetId;
        public int cost;
    }

    [Serializable]
    private sealed class CarpetSave
    {
        public int id;
        public int row;
        public int col;
        public int targetRow;
        public int targetCol;
        public int length;
        public string color = "";
        public string groupId = "";
        public string passColor = "";
    }

    [Serializable]
    private sealed class LevelData
    {
        public int rows;
        public int cols;
        public List<CarpetSave> carpets = new List<CarpetSave>();
    }

    [Serializable]
    private sealed class LevelFile
    {
        public LevelData level = new LevelData();
    }

    [Serializable]
    private sealed class GameArtConfig
    {
        public string sceneBackground = "";
        public string boardBackground = "";
        public string boardCell = "";
        public string carpet = "";
        public string target = "";
        public string backIcon = "Art/icon_back_arrow.png";
        public string restartIcon = "Art/icon_restart_arrow.png";
        public string sceneBackgroundColor = "";
        public string boardBackgroundColor = "";
        public string emptyCellColor = "";
        public GameArtChapterConfig[] chapters = Array.Empty<GameArtChapterConfig>();
    }

    [Serializable]
    private sealed class GameArtChapterConfig
    {
        public int chapterIndex;
        public int[] levels = Array.Empty<int>();
        public string sceneBackground = "";
        public string boardBackground = "";
        public string boardCell = "";
        public string carpet = "";
        public string target = "";
        public string sceneBackgroundColor = "";
        public string boardBackgroundColor = "";
        public string emptyCellColor = "";
    }

    [Serializable]
    private sealed class LevelSlot
    {
        public int number;
        public LevelData data;
    }

    [Serializable]
    private sealed class LevelBank
    {
        public List<LevelSlot> levels = new List<LevelSlot>();
    }

    private sealed class MoveInfo
    {
        public bool ok;
        public bool undo;
        public int cost;
        public string message = "";

        public static MoveInfo Ok(bool undo, int cost)
        {
            return new MoveInfo { ok = true, undo = undo, cost = cost };
        }

        public static MoveInfo Blocked(string message)
        {
            return new MoveInfo { ok = false, message = message };
        }
    }

    private sealed class MovePlan
    {
        public Carpet carpet;
        public int row;
        public int col;
        public MoveInfo moveInfo;
    }

    private sealed class CarpetMotion
    {
        public int fromRow;
        public int fromCol;
        public int toRow;
        public int toCol;
        public float duration;
    }

    private enum GameMode
    {
        Edit,
        Play
    }

    private enum EditTool
    {
        Carpet,
        Target
    }

    private enum DiamondAnimationTrigger
    {
        None,
        Down,
        Target
    }
}

public sealed class CarpetPieceMotion : MonoBehaviour
{
    private RectTransform rect;
    private Vector2 from;
    private Vector2 to;
    private float duration = 0.12f;
    private float elapsed;
    private float targetScale = 1f;

    public void Play(Vector2 startPosition, Vector2 endPosition, float seconds, float scale)
    {
        rect = transform as RectTransform;
        from = startPosition;
        to = endPosition;
        duration = Mathf.Max(0.01f, seconds);
        targetScale = Mathf.Max(0.01f, scale);
        elapsed = 0f;

        if (rect != null)
        {
            rect.anchoredPosition = from;
            rect.localScale = Vector3.one;
        }
    }

    private void Update()
    {
        if (rect == null)
        {
            rect = transform as RectTransform;
            if (rect == null)
            {
                enabled = false;
                return;
            }
        }

        elapsed += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(elapsed / duration);
        float eased = t * t * (3f - 2f * t);
        rect.anchoredPosition = Vector2.LerpUnclamped(from, to, eased);
        rect.localScale = Vector3.one * Mathf.Lerp(1f, targetScale, eased);

        if (t >= 1f)
        {
            rect.anchoredPosition = to;
            rect.localScale = Vector3.one * targetScale;
            enabled = false;
        }
    }
}

public sealed class UiPressScale : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    private const float PressedScale = 0.90f;
    private const float ScaleSpeed = 16f;

    private RectTransform rect;
    private Vector3 baseScale = Vector3.one;
    private float currentScale = 1f;
    private float targetScale = 1f;

    private void Awake()
    {
        CacheRect();
    }

    private void OnEnable()
    {
        CacheRect();
        currentScale = 1f;
        targetScale = 1f;
        ApplyScale();
    }

    private void OnDisable()
    {
        currentScale = 1f;
        targetScale = 1f;
        ApplyScale();
    }

    private void Update()
    {
        if (Mathf.Approximately(currentScale, targetScale))
        {
            return;
        }

        currentScale = Mathf.Lerp(currentScale, targetScale, 1f - Mathf.Exp(-ScaleSpeed * Time.unscaledDeltaTime));
        if (Mathf.Abs(currentScale - targetScale) < 0.001f)
        {
            currentScale = targetScale;
        }
        ApplyScale();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            targetScale = PressedScale;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        targetScale = 1f;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        targetScale = 1f;
    }

    private void CacheRect()
    {
        if (rect != null)
        {
            return;
        }

        rect = transform as RectTransform;
        baseScale = rect != null ? rect.localScale : transform.localScale;
    }

    private void ApplyScale()
    {
        Transform target = rect != null ? rect : transform;
        target.localScale = baseScale * currentScale;
    }
}

public sealed class CarpetGridCellView : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, IPointerUpHandler
{
    private CarpetGridGame game;
    private int row;
    private int col;

    public void Init(CarpetGridGame owner, int cellRow, int cellCol)
    {
        game = owner;
        row = cellRow;
        col = cellCol;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        game.OnCellPointerDown(row, col, eventData);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        game.OnCellPointerEnter(row, col, eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        game.OnCellPointerUp();
    }
}

public sealed class IconButtonGraphic : Graphic
{
    public int icon;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = rectTransform.rect;
        float width = rect.width;
        float height = rect.height;
        float thickness = Mathf.Max(2.5f, Mathf.Min(width, height) * 0.14f);
        Vector2 center = rect.center;

        if (icon == 0)
        {
            float left = rect.xMin + width * 0.24f;
            float right = rect.xMax - width * 0.22f;
            AddLine(vh, new Vector2(right, center.y), new Vector2(left, center.y), thickness);
            AddLine(vh, new Vector2(left, center.y), new Vector2(center.x, rect.yMax - height * 0.22f), thickness);
            AddLine(vh, new Vector2(left, center.y), new Vector2(center.x, rect.yMin + height * 0.22f), thickness);
            return;
        }

        float radius = Mathf.Min(width, height) * 0.34f;
        float startAngle = 220f * Mathf.Deg2Rad;
        float sweepAngle = 280f * Mathf.Deg2Rad;
        Vector2 previous = center + new Vector2(Mathf.Cos(startAngle), Mathf.Sin(startAngle)) * radius;
        for (int i = 1; i <= 24; i++)
        {
            float t = startAngle + sweepAngle * (i / 24f);
            Vector2 next = center + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * radius;
            AddLine(vh, previous, next, thickness);
            previous = next;
        }

        Vector2 tip = center + new Vector2(Mathf.Cos(startAngle + sweepAngle), Mathf.Sin(startAngle + sweepAngle)) * radius;
        AddLine(vh, tip, tip + new Vector2(radius * 0.54f, radius * 0.05f), thickness);
        AddLine(vh, tip, tip + new Vector2(radius * 0.20f, -radius * 0.50f), thickness);
    }

    private void AddLine(VertexHelper vh, Vector2 from, Vector2 to, float thickness)
    {
        Vector2 delta = to - from;
        if (delta.sqrMagnitude <= 0.001f)
        {
            return;
        }

        Vector2 normal = new Vector2(-delta.y, delta.x).normalized * (thickness * 0.5f);
        int start = vh.currentVertCount;
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = color;

        vertex.position = from + normal;
        vh.AddVert(vertex);
        vertex.position = from - normal;
        vh.AddVert(vertex);
        vertex.position = to - normal;
        vh.AddVert(vertex);
        vertex.position = to + normal;
        vh.AddVert(vertex);

        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }
}
