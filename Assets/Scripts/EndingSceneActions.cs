using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class EndingSceneActions : MonoBehaviour
{
    private const int EndingFontSize = 58;
    private const float CharacterDelaySeconds = 0.045f;

    private GameObject[] lineObjects = new GameObject[0];
    private Text[] lineTexts = new Text[0];
    private string[] lineValues = new string[0];
    private GameObject exitHint;
    private int visibleLineIndex;
    private Coroutine typeRoutine;
    private bool readyToReset;
    private bool isTyping;

    private void Awake()
    {
        NormalizeEventSystems();
        ApplyFont(LoadUiFont());
        BindSinglePage();
    }

    public void AdvanceEnding()
    {
        if (isTyping)
        {
            AudioManager.PlaySfx(AudioSfx.UI);
            FinishCurrentLine();
            return;
        }

        if (readyToReset)
        {
            ResetGameAndReturnToIntro();
            return;
        }

        if (lineObjects == null || lineObjects.Length == 0)
        {
            return;
        }

        if (visibleLineIndex + 1 < lineObjects.Length)
        {
            AudioManager.PlaySfx(AudioSfx.UI);
            visibleLineIndex++;
            StartTypingCurrentLine();
            return;
        }

        readyToReset = true;
    }

    public void ResetGameAndReturnToIntro()
    {
        CarpetLevelFlow.ResetGameAndReturnToIntro();
    }

    private void ApplyFont(Font uiFont)
    {
        if (uiFont == null)
        {
            return;
        }

        foreach (Text text in FindObjectsOfType<Text>(true))
        {
            text.font = uiFont;
        }
    }

    private void BindSinglePage()
    {
        Transform pagesRoot = GameObject.Find("EndingPages")?.transform;
        if (pagesRoot == null || pagesRoot.childCount == 0)
        {
            lineObjects = new GameObject[0];
            return;
        }

        Transform hostPage = pagesRoot.GetChild(0);
        Button button = hostPage.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(AdvanceEnding);
        }

        lineObjects = new GameObject[Mathf.Min(4, pagesRoot.childCount)];
        lineTexts = new Text[lineObjects.Length];
        lineValues = new string[lineObjects.Length];
        int lineCount = 0;
        for (int i = 0; i < pagesRoot.childCount; i++)
        {
            Transform page = pagesRoot.GetChild(i);
            Text[] labels = page.GetComponentsInChildren<Text>(true);
            foreach (Text label in labels)
            {
                if (label.gameObject.name == "Exit Hint")
                {
                    exitHint = label.gameObject;
                    continue;
                }

                if (lineCount >= lineObjects.Length)
                {
                    continue;
                }

                RectTransform rect = label.rectTransform;
                rect.SetParent(hostPage, false);
                LayoutLine(rect, lineCount);
                lineObjects[lineCount] = label.gameObject;
                lineTexts[lineCount] = label;
                lineValues[lineCount] = label.text;
                lineCount++;
            }
        }

        if (lineCount != lineObjects.Length)
        {
            System.Array.Resize(ref lineObjects, lineCount);
            System.Array.Resize(ref lineTexts, lineCount);
            System.Array.Resize(ref lineValues, lineCount);
        }

        if (exitHint != null)
        {
            RectTransform hintRect = exitHint.GetComponent<RectTransform>();
            hintRect.SetParent(hostPage, false);
            hintRect.anchorMin = new Vector2(0f, 0f);
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.anchoredPosition = new Vector2(0f, 74f);
            hintRect.sizeDelta = new Vector2(-120f, 62f);
        }

        for (int i = 1; i < pagesRoot.childCount; i++)
        {
            pagesRoot.GetChild(i).gameObject.SetActive(false);
        }

        hostPage.gameObject.SetActive(true);
        visibleLineIndex = 0;
        readyToReset = false;
        RefreshVisibleLines(false);
        StartTypingCurrentLine();
    }

    private static void LayoutLine(RectTransform rect, int index)
    {
        Vector2[] minMax =
        {
            new Vector2(0.63f, 0.84f),
            new Vector2(0.42f, 0.63f),
            new Vector2(0.27f, 0.40f),
            new Vector2(0.12f, 0.25f)
        };

        int safeIndex = Mathf.Clamp(index, 0, minMax.Length - 1);
        rect.anchorMin = new Vector2(0.07f, minMax[safeIndex].x);
        rect.anchorMax = new Vector2(0.93f, minMax[safeIndex].y);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;

        Text text = rect.GetComponent<Text>();
        if (text != null)
        {
            text.alignment = TextAnchor.MiddleCenter;
            text.resizeTextForBestFit = false;
            text.fontSize = EndingFontSize;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
        }
    }

    private void RefreshVisibleLines(bool revealCompletedText)
    {
        for (int i = 0; i < lineObjects.Length; i++)
        {
            if (lineObjects[i] != null)
            {
                lineObjects[i].SetActive(i <= visibleLineIndex);
            }

            if (revealCompletedText && i < visibleLineIndex && i < lineTexts.Length && lineTexts[i] != null)
            {
                lineTexts[i].text = lineValues[i];
            }
        }

        bool endingShown = lineObjects.Length > 0 && visibleLineIndex >= lineObjects.Length - 1;
        if (exitHint != null)
        {
            exitHint.SetActive(endingShown && !isTyping);
        }

        readyToReset = endingShown && !isTyping;
    }

    private void StartTypingCurrentLine()
    {
        if (typeRoutine != null)
        {
            StopCoroutine(typeRoutine);
        }

        RefreshVisibleLines(true);
        typeRoutine = StartCoroutine(TypeCurrentLineRoutine());
    }

    private IEnumerator TypeCurrentLineRoutine()
    {
        isTyping = true;
        readyToReset = false;
        if (exitHint != null)
        {
            exitHint.SetActive(false);
        }

        if (visibleLineIndex < 0 || visibleLineIndex >= lineTexts.Length || lineTexts[visibleLineIndex] == null)
        {
            isTyping = false;
            RefreshVisibleLines(true);
            yield break;
        }

        Text text = lineTexts[visibleLineIndex];
        string value = lineValues[visibleLineIndex] ?? "";
        text.text = "";

        for (int i = 0; i <= value.Length; i++)
        {
            text.text = value.Substring(0, i);
            yield return new WaitForSecondsRealtime(CharacterDelaySeconds);
        }

        isTyping = false;
        typeRoutine = null;
        RefreshVisibleLines(true);
    }

    private void FinishCurrentLine()
    {
        if (typeRoutine != null)
        {
            StopCoroutine(typeRoutine);
            typeRoutine = null;
        }

        if (visibleLineIndex >= 0 && visibleLineIndex < lineTexts.Length && lineTexts[visibleLineIndex] != null)
        {
            lineTexts[visibleLineIndex].text = lineValues[visibleLineIndex];
        }

        isTyping = false;
        RefreshVisibleLines(true);
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

    private void NormalizeEventSystems()
    {
        EventSystem[] systems = FindObjectsOfType<EventSystem>(true);
        if (systems.Length <= 1)
        {
            return;
        }

        foreach (EventSystem system in systems)
        {
            if (system != null && system.gameObject.scene == gameObject.scene)
            {
                system.gameObject.SetActive(false);
                return;
            }
        }
    }
}
