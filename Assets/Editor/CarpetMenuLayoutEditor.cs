using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed class CarpetMenuLayoutEditor : EditorWindow
{
    private const float ReferenceWidth = 1440f;
    private const float ReferenceHeight = 900f;
    private const float CompactLayoutWidth = 760f;
    private const float MinimumPreviewHeight = 220f;
    private const int MinimumButtonCount = 4;
    private const string ConfigAssetPath = "Assets/StreamingAssets/Menu/menu_config.json";

    private MenuConfig config;
    private SelectionKind selectedKind = SelectionKind.Button;
    private int selectedIndex;
    private int selectedDecorationIndex;
    private bool dragging;
    private Vector2 dragOffset;
    private Vector2 sideScroll;
    private bool dirty;

    [MenuItem("Tools/Carpet/Menu Layout Editor")]
    public static void Open()
    {
        GetWindow<CarpetMenuLayoutEditor>("Menu Layout");
    }

    private void OnEnable()
    {
        minSize = new Vector2(560f, 420f);
        LoadConfig();
    }

    private void OnGUI()
    {
        DrawToolbar();

        if (config == null)
        {
            EditorGUILayout.HelpBox("Menu config was not loaded.", MessageType.Warning);
            return;
        }

        if (position.width < CompactLayoutWidth)
        {
            float previewHeight = Mathf.Max(MinimumPreviewHeight, (position.height - EditorGUIUtility.singleLineHeight) * 0.48f);
            Rect previewRect = GUILayoutUtility.GetRect(1f, previewHeight, GUILayout.ExpandWidth(true), GUILayout.Height(previewHeight));
            DrawPreview(previewRect);
            DrawInspector(0f);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        Rect widePreviewRect = GUILayoutUtility.GetRect(320f, 240f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        DrawPreview(widePreviewRect);
        DrawInspector(Mathf.Clamp(position.width * 0.30f, 300f, 360f));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(70)))
        {
            LoadConfig();
        }
        GUI.enabled = config != null;
        if (GUILayout.Button(dirty ? "Save *" : "Save", EditorStyles.toolbarButton, GUILayout.Width(70)))
        {
            SaveConfig();
        }
        GUI.enabled = true;
        GUILayout.FlexibleSpace();
        GUILayout.Label(ConfigAssetPath, EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawPreview(Rect hostRect)
    {
        float scale = Mathf.Min(hostRect.width / ReferenceWidth, hostRect.height / ReferenceHeight);
        Vector2 previewSize = new Vector2(ReferenceWidth * scale, ReferenceHeight * scale);
        Rect previewRect = new Rect(
            hostRect.center.x - previewSize.x * 0.5f,
            hostRect.center.y - previewSize.y * 0.5f,
            previewSize.x,
            previewSize.y);

        EditorGUI.DrawRect(hostRect, new Color(0.16f, 0.16f, 0.16f, 1f));
        EditorGUI.DrawRect(previewRect, ParseColor(config.backgroundColor, new Color(0.91f, 0.87f, 0.78f, 1f)));
        DrawPreviewGuides(previewRect);

        Event current = Event.current;
        DrawDecorationPreviews(previewRect, scale, current);

        for (int i = 0; i < ButtonCount; i++)
        {
            MenuButtonConfig button = GetButton(i);
            Rect buttonRect = ButtonRect(previewRect, button, scale, i);
            Color color = selectedKind == SelectionKind.Button && i == selectedIndex ? new Color(0.20f, 0.54f, 0.64f, 1f) : new Color(0.18f, 0.43f, 0.50f, 0.88f);
            Matrix4x4 oldMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(-button.rotation, buttonRect.center);
            EditorGUI.DrawRect(buttonRect, color);
            GUI.Label(buttonRect, button.label, CenteredWhiteLabel);
            GUI.matrix = oldMatrix;

            if (current.type == EventType.MouseDown && current.button == 0 && ButtonContainsMouse(buttonRect, button.rotation, current.mousePosition))
            {
                selectedKind = SelectionKind.Button;
                selectedIndex = i;
                dragging = true;
                dragOffset = current.mousePosition - buttonRect.center;
                current.Use();
                Repaint();
            }
        }

        if (dragging)
        {
            if (current.type == EventType.MouseDrag && current.button == 0)
            {
                Vector2 center = current.mousePosition - dragOffset;
                if (selectedKind == SelectionKind.Decoration && DecorationCount > 0)
                {
                    GetDecoration(selectedDecorationIndex).position = GuiToMenuPosition(previewRect, center, scale);
                }
                else
                {
                    GetButton(selectedIndex).position = GuiToMenuPosition(previewRect, center, scale);
                }
                dirty = true;
                current.Use();
                Repaint();
            }
            else if (current.type == EventType.MouseUp)
            {
                dragging = false;
                current.Use();
            }
        }
    }

    private void DrawDecorationPreviews(Rect previewRect, float scale, Event current)
    {
        for (int i = 0; i < DecorationCount; i++)
        {
            MenuDecorationConfig decoration = GetDecoration(i);
            Rect decorationRect = DecorationRect(previewRect, decoration, scale);
            Texture2D texture = LoadResourceTexture(decoration.image);
            if (decoration.shadow)
            {
                Rect shadowRect = DecorationShadowRect(previewRect, decoration, scale);
                Matrix4x4 oldShadowMatrix = GUI.matrix;
                GUIUtility.RotateAroundPivot(-decoration.rotation, shadowRect.center);
                Color oldColor = GUI.color;
                GUI.color = ParseColor(decoration.shadowColor, new Color(0.10f, 0.07f, 0.08f, 0.40f));
                if (texture != null)
                {
                    GUI.DrawTexture(shadowRect, texture, ScaleMode.StretchToFill, true);
                }
                else
                {
                    EditorGUI.DrawRect(shadowRect, GUI.color);
                }
                GUI.color = oldColor;
                GUI.matrix = oldShadowMatrix;
            }

            Matrix4x4 oldMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(-decoration.rotation, decorationRect.center);
            if (texture != null)
            {
                GUI.DrawTexture(decorationRect, texture, ScaleMode.StretchToFill, true);
            }
            else
            {
                EditorGUI.DrawRect(decorationRect, new Color(0.76f, 0.47f, 0.22f, 0.45f));
                GUI.Label(decorationRect, decoration.id, CenteredWhiteLabel);
            }

            if (selectedKind == SelectionKind.Decoration && i == selectedDecorationIndex)
            {
                DrawRectOutline(decorationRect, new Color(1f, 0.68f, 0.18f, 1f), 2f);
            }
            GUI.matrix = oldMatrix;

            if (current.type == EventType.MouseDown && current.button == 0 && ButtonContainsMouse(decorationRect, decoration.rotation, current.mousePosition))
            {
                selectedKind = SelectionKind.Decoration;
                selectedDecorationIndex = i;
                dragging = true;
                dragOffset = current.mousePosition - decorationRect.center;
                current.Use();
                Repaint();
            }
        }
    }

    private void DrawPreviewGuides(Rect previewRect)
    {
        Color guide = new Color(0f, 0f, 0f, 0.14f);
        EditorGUI.DrawRect(new Rect(previewRect.center.x - 0.5f, previewRect.y, 1f, previewRect.height), guide);
        EditorGUI.DrawRect(new Rect(previewRect.x, previewRect.center.y - 0.5f, previewRect.width, 1f), guide);
        GUI.Label(new Rect(previewRect.x + 8f, previewRect.y + 6f, 260f, 20f), "Drag buttons or decorations here, then Save.", EditorStyles.miniLabel);
    }

    private void DrawInspector(float width)
    {
        if (width > 0f)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(width));
        }
        else
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        }
        sideScroll = EditorGUILayout.BeginScrollView(sideScroll);
        EditorGUILayout.Space(8);
        selectedKind = (SelectionKind)GUILayout.Toolbar((int)selectedKind, new[] { "Buttons", "Decorations" });
        EditorGUILayout.Space(10);

        if (selectedKind == SelectionKind.Decoration)
        {
            DrawDecorationInspector();
        }
        else
        {
            DrawButtonInspector();
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.HelpBox("Position uses the menu center as (0, 0). Positive Y moves up. Rotation is in degrees.", MessageType.Info);
        if (GUILayout.Button("Save Config", GUILayout.Height(34)))
        {
            SaveConfig();
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawButtonInspector()
    {
        EditorGUILayout.LabelField("Chapter Buttons", EditorStyles.boldLabel);

        string[] labels = Enumerable.Range(0, ButtonCount).Select(i => GetButton(i).label).ToArray();
        selectedIndex = Mathf.Clamp(GUILayout.SelectionGrid(selectedIndex, labels, 1), 0, ButtonCount - 1);

        EditorGUILayout.Space(10);
        MenuButtonConfig button = GetButton(selectedIndex);
        EditorGUI.BeginChangeCheck();
        button.label = EditorGUILayout.TextField("Label", button.label);
        button.position = EditorGUILayout.Vector2Field("Position", button.position);
        button.size = EditorGUILayout.Vector2Field("Size", button.size);
        button.size.x = Mathf.Max(40f, button.size.x);
        button.size.y = Mathf.Max(28f, button.size.y);
        button.rotation = EditorGUILayout.Slider("Rotation", button.rotation, -180f, 180f);

        string levels = string.Join(", ", button.levels ?? Array.Empty<int>());
        string editedLevels = EditorGUILayout.TextField("Levels", levels);
        if (editedLevels != levels)
        {
            button.levels = ParseLevels(editedLevels);
        }

        if (EditorGUI.EndChangeCheck())
        {
            dirty = true;
            Repaint();
        }
    }

    private void DrawDecorationInspector()
    {
        EditorGUILayout.LabelField("Decorations", EditorStyles.boldLabel);
        if (DecorationCount == 0)
        {
            EditorGUILayout.HelpBox("No decorations are configured.", MessageType.Info);
            return;
        }

        string[] labels = Enumerable.Range(0, DecorationCount).Select(i => DecorationLabel(GetDecoration(i), i)).ToArray();
        selectedDecorationIndex = Mathf.Clamp(GUILayout.SelectionGrid(selectedDecorationIndex, labels, 1), 0, DecorationCount - 1);

        EditorGUILayout.Space(10);
        MenuDecorationConfig decoration = GetDecoration(selectedDecorationIndex);
        EditorGUI.BeginChangeCheck();
        decoration.id = EditorGUILayout.TextField("Id", decoration.id);
        decoration.image = EditorGUILayout.TextField("Image", decoration.image);
        decoration.position = EditorGUILayout.Vector2Field("Position", decoration.position);

        Vector2 resolvedSize = ResolveDecorationSize(decoration);
        float width = Mathf.Max(8f, EditorGUILayout.FloatField("Width", resolvedSize.x));
        decoration.size = ResolveDecorationSize(decoration, width);
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.FloatField("Height (auto)", decoration.size.y);
        EditorGUI.EndDisabledGroup();

        decoration.rotation = EditorGUILayout.Slider("Rotation", decoration.rotation, -180f, 180f);
        decoration.color = EditorGUILayout.TextField("Color", decoration.color);
        decoration.shadow = EditorGUILayout.Toggle("Shadow", decoration.shadow);
        if (decoration.shadow)
        {
            decoration.shadowOffset = EditorGUILayout.Vector2Field("Shadow Offset", decoration.shadowOffset);
            decoration.shadowScale = EditorGUILayout.Vector2Field("Shadow Scale", decoration.shadowScale);
            decoration.shadowScale.x = Mathf.Max(0.05f, decoration.shadowScale.x);
            decoration.shadowScale.y = Mathf.Max(0.05f, decoration.shadowScale.y);
            decoration.shadowColor = EditorGUILayout.TextField("Shadow Color", decoration.shadowColor);
        }

        if (EditorGUI.EndChangeCheck())
        {
            dirty = true;
            Repaint();
        }
    }

    private Rect ButtonRect(Rect previewRect, MenuButtonConfig button, float scale, int index)
    {
        Vector2 size = ResolveButtonSize(button) * scale;
        Vector2 position = ResolveButtonPosition(button, index, ResolveButtonSize(button));
        Vector2 center = new Vector2(previewRect.center.x + position.x * scale, previewRect.center.y - position.y * scale);
        return new Rect(center.x - size.x * 0.5f, center.y - size.y * 0.5f, size.x, size.y);
    }

    private Rect DecorationRect(Rect previewRect, MenuDecorationConfig decoration, float scale)
    {
        Vector2 size = ResolveDecorationSize(decoration) * scale;
        Vector2 center = new Vector2(previewRect.center.x + decoration.position.x * scale, previewRect.center.y - decoration.position.y * scale);
        return new Rect(center.x - size.x * 0.5f, center.y - size.y * 0.5f, size.x, size.y);
    }

    private Rect DecorationShadowRect(Rect previewRect, MenuDecorationConfig decoration, float scale)
    {
        Vector2 size = ScaleDecorationSize(ResolveDecorationSize(decoration), decoration.shadowScale) * scale;
        Vector2 position = decoration.position + decoration.shadowOffset;
        Vector2 center = new Vector2(previewRect.center.x + position.x * scale, previewRect.center.y - position.y * scale);
        return new Rect(center.x - size.x * 0.5f, center.y - size.y * 0.5f, size.x, size.y);
    }

    private static Vector2 GuiToMenuPosition(Rect previewRect, Vector2 guiPosition, float scale)
    {
        return new Vector2((guiPosition.x - previewRect.center.x) / scale, -(guiPosition.y - previewRect.center.y) / scale);
    }

    private static bool ButtonContainsMouse(Rect rect, float rotation, Vector2 mousePosition)
    {
        Vector2 local = mousePosition - rect.center;
        float radians = rotation * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians);
        float cos = Mathf.Cos(radians);
        Vector2 unrotated = new Vector2(local.x * cos - local.y * sin, local.x * sin + local.y * cos) + rect.center;
        return rect.Contains(unrotated);
    }

    private void LoadConfig()
    {
        string fullPath = Path.Combine(Directory.GetCurrentDirectory(), ConfigAssetPath);
        if (!File.Exists(fullPath))
        {
            config = new MenuConfig();
            selectedIndex = 0;
            dirty = true;
            return;
        }

        config = JsonUtility.FromJson<MenuConfig>(File.ReadAllText(fullPath, Encoding.UTF8));
        if (config == null)
        {
            config = new MenuConfig();
        }
        EnsureButtonDefaults();
        EnsureDecorationDefaults();
        selectedIndex = Mathf.Clamp(selectedIndex, 0, ButtonCount - 1);
        selectedDecorationIndex = Mathf.Clamp(selectedDecorationIndex, 0, Mathf.Max(0, DecorationCount - 1));
        dirty = false;
    }

    private void SaveConfig()
    {
        EnsureButtonDefaults();
        EnsureDecorationDefaults();
        string fullPath = Path.Combine(Directory.GetCurrentDirectory(), ConfigAssetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        File.WriteAllText(fullPath, JsonUtility.ToJson(config, true), Encoding.UTF8);
        AssetDatabase.Refresh();
        dirty = false;
    }

    private void EnsureButtonDefaults()
    {
        if (config.buttons == null || config.buttons.Length < ButtonCount)
        {
            MenuButtonConfig[] next = new MenuButtonConfig[ButtonCount];
            for (int i = 0; i < ButtonCount; i++)
            {
                next[i] = config.buttons != null && i < config.buttons.Length && config.buttons[i] != null ? config.buttons[i] : DefaultButton(i);
            }
            config.buttons = next;
        }

        for (int i = 0; i < config.buttons.Length; i++)
        {
            if (config.buttons[i] == null)
            {
                config.buttons[i] = DefaultButton(i);
            }
            if (config.buttons[i].size.x <= 0f || config.buttons[i].size.y <= 0f)
            {
                config.buttons[i].size = new Vector2(360f, 72f);
                config.buttons[i].position = ResolveButtonPosition(config.buttons[i], i, config.buttons[i].size);
            }
        }
    }

    private void EnsureDecorationDefaults()
    {
        if (config.decorations == null)
        {
            config.decorations = Array.Empty<MenuDecorationConfig>();
        }

        for (int i = 0; i < config.decorations.Length; i++)
        {
            if (config.decorations[i] == null)
            {
                config.decorations[i] = new MenuDecorationConfig();
            }
            config.decorations[i].size = ResolveDecorationSize(config.decorations[i]);
        }
    }

    private MenuButtonConfig GetButton(int index)
    {
        EnsureButtonDefaults();
        return config.buttons[Mathf.Clamp(index, 0, ButtonCount - 1)];
    }

    private MenuDecorationConfig GetDecoration(int index)
    {
        EnsureDecorationDefaults();
        return config.decorations[Mathf.Clamp(index, 0, DecorationCount - 1)];
    }

    private static Vector2 ResolveButtonSize(MenuButtonConfig button)
    {
        return button != null && button.size.x > 0f && button.size.y > 0f ? button.size : new Vector2(360f, 72f);
    }

    private Vector2 ResolveDecorationSize(MenuDecorationConfig decoration)
    {
        float width = decoration != null && decoration.size.x > 0f ? decoration.size.x : 120f;
        return ResolveDecorationSize(decoration, width);
    }

    private Vector2 ResolveDecorationSize(MenuDecorationConfig decoration, float width)
    {
        width = Mathf.Max(8f, width);
        float aspect = DecorationAspect(decoration);
        return new Vector2(width, Mathf.Max(8f, width * aspect));
    }

    private static Vector2 ScaleDecorationSize(Vector2 size, Vector2 scale)
    {
        float x = scale.x > 0f ? scale.x : 1f;
        float y = scale.y > 0f ? scale.y : 1f;
        return new Vector2(size.x * x, size.y * y);
    }

    private float DecorationAspect(MenuDecorationConfig decoration)
    {
        Texture2D texture = LoadResourceTexture(decoration != null ? decoration.image : "");
        if (texture != null && texture.width > 0)
        {
            return Mathf.Max(0.01f, texture.height / (float)texture.width);
        }
        if (decoration != null && decoration.size.x > 0f && decoration.size.y > 0f)
        {
            return Mathf.Max(0.01f, decoration.size.y / decoration.size.x);
        }
        return 1f;
    }

    private static Texture2D LoadResourceTexture(string resourcePath)
    {
        string assetPath = ResourceImageToAssetPath(resourcePath);
        return string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    private static string ResourceImageToAssetPath(string resourcePath)
    {
        if (string.IsNullOrEmpty(resourcePath))
        {
            return "";
        }

        string normalized = resourcePath.Replace('\\', '/');
        if (!Path.HasExtension(normalized))
        {
            normalized += ".png";
        }
        return "Assets/Resources/" + normalized;
    }

    private static string DecorationLabel(MenuDecorationConfig decoration, int index)
    {
        if (decoration != null && !string.IsNullOrEmpty(decoration.id))
        {
            return decoration.id;
        }
        return "Decoration " + (index + 1);
    }

    private static void DrawRectOutline(Rect rect, Color color, float thickness)
    {
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, thickness, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), color);
    }

    private static Vector2 ResolveButtonPosition(MenuButtonConfig button, int index, Vector2 size)
    {
        if (button != null && button.size.x > 0f && button.size.y > 0f)
        {
            return button.position;
        }

        float spacing = 28f;
        float totalHeight = MinimumButtonCount * size.y + (MinimumButtonCount - 1) * spacing;
        float topY = totalHeight * 0.5f - size.y * 0.5f;
        return new Vector2(0f, topY - index * (size.y + spacing));
    }

    private static MenuButtonConfig DefaultButton(int index)
    {
        string[] names = { "章节一", "章节二", "章节三", "章节四" };
        int[][] levels =
        {
            new[] { 1, 2 },
            new[] { 3, 4 },
            new[] { 5, 6 },
            new[] { 7, 10 }
        };

        Vector2 size = new Vector2(360f, 72f);
        return new MenuButtonConfig
        {
            label = index >= 0 && index < names.Length ? names[index] : "章节",
            levels = index >= 0 && index < levels.Length ? levels[index] : new[] { index + 1 },
            size = size,
            position = ResolveButtonPosition(null, index, size)
        };
    }

    private static int[] ParseLevels(string value)
    {
        return value
            .Split(new[] { ',', '，', ' ', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(text =>
            {
                int parsed;
                return int.TryParse(text, out parsed) ? parsed : 0;
            })
            .Where(level => level > 0)
            .ToArray();
    }

    private static Color ParseColor(string value, Color fallback)
    {
        Color color;
        return !string.IsNullOrEmpty(value) && ColorUtility.TryParseHtmlString(value, out color) ? color : fallback;
    }

    private int ButtonCount
    {
        get { return Mathf.Max(MinimumButtonCount, config != null && config.buttons != null ? config.buttons.Length : MinimumButtonCount); }
    }

    private int DecorationCount
    {
        get { return config != null && config.decorations != null ? config.decorations.Length : 0; }
    }

    private static GUIStyle CenteredWhiteLabel
    {
        get
        {
            GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
            style.alignment = TextAnchor.MiddleCenter;
            style.normal.textColor = Color.white;
            return style;
        }
    }

    [Serializable]
    private sealed class MenuConfig
    {
        public string backgroundColor = "#e9dfc7";
        public string backgroundImage = "";
        public MenuButtonConfig[] buttons = Array.Empty<MenuButtonConfig>();
        public MenuDecorationConfig[] decorations = Array.Empty<MenuDecorationConfig>();
        public MenuAnimationConfig[] animations = Array.Empty<MenuAnimationConfig>();
    }

    [Serializable]
    private sealed class MenuButtonConfig
    {
        public string label = "";
        public int[] levels = Array.Empty<int>();
        public Vector2 position;
        public Vector2 size;
        public float rotation;
    }

    [Serializable]
    private sealed class MenuDecorationConfig
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
        public float breathAmplitude = 6f;
        public float breathScale = 0.018f;
        public float breathSeconds = 2.8f;
        public float frameFadeSeconds = 0.08f;
    }

    [Serializable]
    private sealed class MenuAnimationConfig
    {
        public string id = "";
        public string type = "";
        public string color = "#ffffff";
        public Vector2 position;
        public Vector2 size = new Vector2(120, 120);
        public float speed = 1f;
        public string payload = "";
    }

    private enum SelectionKind
    {
        Button,
        Decoration
    }
}
