using UnityEngine;
using UnityEngine.UI;

public sealed class GuildPopup : MonoBehaviour
{
    private const string SaveKeyPrefix = "swallow-diamond-guild-popup-";

    [SerializeField] private Button confirmButton;
    [SerializeField] private string saveKey = "";

    private void Awake()
    {
        ApplyReadableFont();

        if (confirmButton == null)
        {
            confirmButton = GetComponentInChildren<Button>(true);
        }

        if (confirmButton != null)
        {
            confirmButton.onClick.RemoveListener(Confirm);
            confirmButton.onClick.AddListener(Confirm);
        }
    }

    private void ApplyReadableFont()
    {
        Font font = Font.CreateDynamicFontFromOSFont(
            new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "Arial" },
            32);
        if (font == null)
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        foreach (Text text in GetComponentsInChildren<Text>(true))
        {
            text.font = font;
        }
    }

    public void SetSaveKey(string key)
    {
        saveKey = key ?? "";
    }

    public static bool HasConfirmed(string key)
    {
        return !string.IsNullOrEmpty(key) && PlayerPrefs.GetInt(key, 0) != 0;
    }

    public static string SaveKeyForIndex(int index)
    {
        return index > 0 ? SaveKeyPrefix + index.ToString("00") : "";
    }

    public static void ResetConfirmedStates(int count)
    {
        for (int i = 1; i <= count; i++)
        {
            PlayerPrefs.DeleteKey(SaveKeyForIndex(i));
        }
    }

    private void Confirm()
    {
        if (!string.IsNullOrEmpty(saveKey))
        {
            PlayerPrefs.SetInt(saveKey, 1);
            PlayerPrefs.Save();
        }

        Destroy(gameObject);
    }
}
