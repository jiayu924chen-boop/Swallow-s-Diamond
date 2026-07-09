using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public sealed class GuideLayerController : MonoBehaviour
{
    [SerializeField] private Button nextButton;
    [SerializeField] private GameObject guideBox;
    [SerializeField] private Text guideText;
    [SerializeField] private GuideDialogueBinding[] dialogues = Array.Empty<GuideDialogueBinding>();

    private GuideDialogueConfig activeDialogue;
    private int lineIndex = -1;

    private void Awake()
    {
        ResolveBindings();
        HideGuide();
    }

    public void StartGuide(GuideTextType guideType)
    {
        ResolveBindings();
        activeDialogue = FindDialogue(guideType);
        lineIndex = 0;

        if (activeDialogue == null || activeDialogue.lines == null || activeDialogue.lines.Length == 0)
        {
            Debug.LogWarning("Guide dialogue is missing or empty: " + guideType);
            HideGuide();
            return;
        }

        if (guideBox != null)
        {
            guideBox.SetActive(true);
        }
        if (nextButton != null)
        {
            nextButton.interactable = true;
        }

        UpdateGuideText();
    }

    private void ShowNextLine()
    {
        if (activeDialogue == null || activeDialogue.lines == null || activeDialogue.lines.Length == 0)
        {
            HideGuide();
            return;
        }

        if (lineIndex < activeDialogue.lines.Length - 1)
        {
            lineIndex++;
            UpdateGuideText();
            return;
        }

        HideGuide();
    }

    private void UpdateGuideText()
    {
        if (guideText == null || activeDialogue == null || activeDialogue.lines == null)
        {
            return;
        }

        int safeIndex = Mathf.Clamp(lineIndex, 0, activeDialogue.lines.Length - 1);
        guideText.text = activeDialogue.lines[safeIndex] ?? string.Empty;
    }

    private void HideGuide()
    {
        if (nextButton != null)
        {
            nextButton.interactable = false;
        }
        if (guideBox != null)
        {
            guideBox.SetActive(false);
        }
    }

    private GuideDialogueConfig FindDialogue(GuideTextType guideType)
    {
        GuideDialogueBinding binding = dialogues == null
            ? null
            : dialogues.FirstOrDefault(item => item != null && item.guideType == guideType);
        return binding != null ? binding.dialogue : null;
    }

    private void ResolveBindings()
    {
        nextButton = nextButton != null ? nextButton : GetComponent<Button>();
        guideBox = guideBox != null ? guideBox : FindChild("GuideBox")?.gameObject;
        guideText = guideText != null ? guideText : (guideBox != null ? guideBox.GetComponentInChildren<Text>(true) : GetComponentInChildren<Text>(true));

        if (nextButton != null)
        {
            nextButton.onClick.RemoveListener(ShowNextLine);
            nextButton.onClick.AddListener(ShowNextLine);
        }
    }

    private Transform FindChild(string childName)
    {
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(child.name, childName, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }
        return null;
    }

    [Serializable]
    public sealed class GuideDialogueBinding
    {
        public GuideTextType guideType;
        public GuideDialogueConfig dialogue;
    }
}
