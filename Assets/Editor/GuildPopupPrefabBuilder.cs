using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class GuildPopupPrefabBuilder
{
    private const string PopupSpritePath = "Assets/Resources/Art/Guide/guild_popup_frame.png";
    private const string ConfirmSpritePath = "Assets/Resources/Art/Guide/guild_confirm_button.png";
    private const string PrefabFolder = "Assets/Resources/Prefabs/Guild";

    private static readonly string[] Names = { "Guild01", "Guild02", "Guild03", "Guild04" };

    private static readonly string[] Lines =
    {
        "滑动钻石到相应颜色目标点\n同时消耗完所有步数。",
        "同色钻石可以不消耗步数\n通过同色通道。",
        "部分同色钻石需要同时移动\n单个钻石遇到阻挡时\n不影响同组其他钻石移动。",
        "镶边钻石可以不消耗步数\n通过异色（对应镶边）通道。"
    };

    [MenuItem("Tools/Guide/Build Guild Popup Prefabs")]
    public static void BuildAll()
    {
        ConfigureSprite(PopupSpritePath, 128);
        ConfigureSprite(ConfirmSpritePath, 128);
        AssetDatabase.Refresh();

        Sprite popupSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PopupSpritePath);
        Sprite confirmSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ConfirmSpritePath);
        if (popupSprite == null || confirmSprite == null)
        {
            throw new InvalidOperationException("Guild popup sprites are missing or not imported as sprites.");
        }

        EnsureFolder(PrefabFolder);

        for (int i = 0; i < Names.Length; i++)
        {
            GameObject root = CreatePrefabRoot(Names[i], Lines[i], popupSprite, confirmSprite, i + 1);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/" + Names[i] + ".prefab");
            UnityEngine.Object.DestroyImmediate(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static GameObject CreatePrefabRoot(string popupName, string line, Sprite popupSprite, Sprite confirmSprite, int index)
    {
        GameObject root = new GameObject(popupName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(GuildPopup));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        Stretch(rootRect);

        Image overlay = root.GetComponent<Image>();
        overlay.color = new Color(0f, 0f, 0f, 0.36f);
        overlay.raycastTarget = true;

        GameObject frameObject = new GameObject("GuideBox", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        frameObject.transform.SetParent(root.transform, false);
        RectTransform frameRect = frameObject.GetComponent<RectTransform>();
        frameRect.anchorMin = new Vector2(0.5f, 0.5f);
        frameRect.anchorMax = new Vector2(0.5f, 0.5f);
        frameRect.pivot = new Vector2(0.5f, 0.5f);
        frameRect.anchoredPosition = new Vector2(0f, 40f);
        frameRect.sizeDelta = new Vector2(960f, 910f);

        Image frameImage = frameObject.GetComponent<Image>();
        frameImage.sprite = popupSprite;
        frameImage.preserveAspect = true;
        frameImage.raycastTarget = false;

        GameObject textObject = new GameObject("GuideText", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Outline));
        textObject.transform.SetParent(frameObject.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.09f, 0.30f);
        textRect.anchorMax = new Vector2(0.91f, 0.76f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        Text text = textObject.GetComponent<Text>();
        text.text = line;
        text.fontSize = 52;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.color = new Color(0.22f, 0.22f, 0.24f, 1f);
        text.resizeTextForBestFit = false;
        text.resizeTextMinSize = 52;
        text.resizeTextMaxSize = 52;

        Outline outline = textObject.GetComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.76f);
        outline.effectDistance = new Vector2(2f, -2f);

        GameObject buttonObject = new GameObject("ConfirmButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(frameObject.transform, false);
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0f);
        buttonRect.anchorMax = new Vector2(0.5f, 0f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = new Vector2(0f, 160f);
        buttonRect.sizeDelta = new Vector2(260f, 196f);

        Image buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.sprite = confirmSprite;
        buttonImage.preserveAspect = true;
        buttonImage.raycastTarget = true;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = buttonImage;
        button.transition = Selectable.Transition.ColorTint;

        GuildPopup popup = root.GetComponent<GuildPopup>();
        SerializedObject serializedPopup = new SerializedObject(popup);
        serializedPopup.FindProperty("confirmButton").objectReferenceValue = button;
        serializedPopup.FindProperty("saveKey").stringValue = "swallow-diamond-guild-popup-" + index.ToString("00");
        serializedPopup.ApplyModifiedPropertiesWithoutUndo();

        return root;
    }

    private static void ConfigureSprite(string path, float pixelsPerUnit)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = pixelsPerUnit;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    private static void EnsureFolder(string folder)
    {
        string[] parts = folder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
