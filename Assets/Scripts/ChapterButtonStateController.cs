using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class ChapterButtonStateController : MonoBehaviour
{
    private const string UnlockArrowResourcePath = "Menu/unlock_arrow";
    private const string UnlockArrowObjectName = "UnlockArrowPointer";
    private const float UnlockArrowGap = 44f;
    private const float ChapterTwoUnlockArrowGap = 30f;
    private const float UnlockArrowFallbackSize = 144f;
    private const float UnlockArrowPulseRange = 20f;
    private const float UnlockArrowPulseSeconds = 0.9f;

    [SerializeField] private GameObject lockState;
    [SerializeField] private GameObject unlockState;
    [SerializeField] private GameObject finishState;
    [SerializeField] private ChapterButtonState currentState = ChapterButtonState.Lock;
    [SerializeField] private Animator animator;

    private RawImage unlockArrowImage;
    private UnlockArrowPointerMotion unlockArrowMotion;

    private const string LockAnimation = "Lock";
    private const string UnlockAnimation = "Unlock";
    private const string FinishAnimation = "Finish";
    private const string LockToUnlockAnimation = "LockToUnlock";
    private const string UnlockToFinishAnimation = "UnlockToFinish";

    public ChapterButtonState CurrentState => currentState;

    private void Awake()
    {
        ResolveBindings();
        ShowState(currentState);
    }

    private void Reset()
    {
        ResolveBindings();
        ShowState(ChapterButtonState.Lock);
    }

    private void OnValidate()
    {
        ResolveBindings();
        ShowState(currentState);
    }

    public void SetLockState()
    {
        ShowState(ChapterButtonState.Lock);
    }

    public void SetUnlockState()
    {
        ShowState(ChapterButtonState.Unlock);
    }

    public void SetFinishState()
    {
        ShowState(ChapterButtonState.Finish);
    }

    public void PlayLockToUnlock()
    {
        PlayTransition(ChapterButtonState.Unlock, LockToUnlockAnimation);
        ShowUnlockArrow();
    }

    public void PlayUnlockToFinish()
    {
        PlayTransition(ChapterButtonState.Finish, UnlockToFinishAnimation);
    }

    public void ApplyState(ChapterButtonState state)
    {
        ShowState(state);
    }

    public void ShowState(ChapterButtonState state)
    {
        currentState = state;

        SetStateObjectActive(lockState, state == ChapterButtonState.Lock);
        SetStateObjectActive(unlockState, state == ChapterButtonState.Unlock);
        SetStateObjectActive(finishState, state == ChapterButtonState.Finish);
        HideUnlockArrow();
        PlayAnimation(StateAnimationName(state));
    }

    private void PlayTransition(ChapterButtonState finalState, string animationName)
    {
        ShowState(finalState);
        PlayAnimation(animationName);
    }

    private void ResolveBindings()
    {
        lockState = lockState != null ? lockState : FindDirectChild("Lock");
        unlockState = unlockState != null ? unlockState : FindDirectChild("Unlock", "UnLock");
        finishState = finishState != null ? finishState : FindDirectChild("Finish");
        animator = animator != null ? animator : GetComponent<Animator>();
    }

    private GameObject FindDirectChild(params string[] names)
    {
        if (names == null || names.Length == 0)
        {
            return null;
        }

        foreach (Transform child in transform)
        {
            foreach (string name in names)
            {
                if (string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return child.gameObject;
                }
            }
        }

        return null;
    }

    private void PlayAnimation(string animationName)
    {
        if (animator != null && animator.gameObject.activeInHierarchy && !string.IsNullOrEmpty(animationName))
        {
            animator.Play(animationName, 0, 0f);
        }
    }

    private void ShowUnlockArrow()
    {
        RawImage arrow = EnsureUnlockArrow();
        if (arrow == null)
        {
            return;
        }

        arrow.gameObject.SetActive(true);
    }

    private void HideUnlockArrow()
    {
        if (unlockArrowImage != null)
        {
            unlockArrowImage.gameObject.SetActive(false);
        }
    }

    private RawImage EnsureUnlockArrow()
    {
        if (unlockArrowImage != null)
        {
            PositionUnlockArrow(unlockArrowImage.rectTransform);
            return unlockArrowImage;
        }

        Texture2D texture = Resources.Load<Texture2D>(UnlockArrowResourcePath);
        if (texture == null)
        {
            Debug.LogWarning("Chapter unlock arrow texture is missing: Resources/" + UnlockArrowResourcePath);
            return null;
        }

        GameObject arrowObject = new GameObject(UnlockArrowObjectName, typeof(RectTransform), typeof(RawImage));
        arrowObject.transform.SetParent(transform, false);
        unlockArrowImage = arrowObject.GetComponent<RawImage>();
        unlockArrowImage.texture = texture;
        unlockArrowImage.color = Color.white;
        unlockArrowImage.raycastTarget = false;
        unlockArrowMotion = arrowObject.AddComponent<UnlockArrowPointerMotion>();
        PositionUnlockArrow(unlockArrowImage.rectTransform);
        return unlockArrowImage;
    }

    private void PositionUnlockArrow(RectTransform arrowRect)
    {
        if (arrowRect == null)
        {
            return;
        }

        RectTransform buttonRect = transform as RectTransform;
        float buttonWidth = ResolveRectSize(buttonRect, true);
        float buttonHeight = ResolveRectSize(buttonRect, false);
        float arrowSize = Mathf.Max(36f, Mathf.Min(UnlockArrowFallbackSize, buttonHeight * 1.35f));

        arrowRect.anchorMin = new Vector2(0.5f, 0.5f);
        arrowRect.anchorMax = new Vector2(0.5f, 0.5f);
        arrowRect.pivot = new Vector2(0.5f, 0.5f);
        arrowRect.sizeDelta = new Vector2(arrowSize, arrowSize);
        float gap = IsChapterTwoButton() ? ChapterTwoUnlockArrowGap : UnlockArrowGap;
        arrowRect.anchoredPosition = new Vector2(-(buttonWidth * 0.5f + gap + arrowSize * 0.5f), 0f);
        arrowRect.localRotation = Quaternion.identity;
        arrowRect.localScale = Vector3.one;
        if (unlockArrowMotion != null)
        {
            unlockArrowMotion.Init(arrowRect, arrowRect.anchoredPosition, UnlockArrowPulseRange, UnlockArrowPulseSeconds);
        }
    }

    private bool IsChapterTwoButton()
    {
        return string.Equals(name, "ChapterButton2", StringComparison.OrdinalIgnoreCase);
    }

    private static float ResolveRectSize(RectTransform rect, bool width)
    {
        if (rect == null)
        {
            return width ? 0f : UnlockArrowFallbackSize;
        }

        float rectValue = width ? rect.rect.width : rect.rect.height;
        if (rectValue > 0f)
        {
            return rectValue;
        }

        float sizeDeltaValue = width ? Mathf.Abs(rect.sizeDelta.x) : Mathf.Abs(rect.sizeDelta.y);
        if (sizeDeltaValue > 0f)
        {
            return sizeDeltaValue;
        }

        return width ? 0f : UnlockArrowFallbackSize;
    }

    private static string StateAnimationName(ChapterButtonState state)
    {
        switch (state)
        {
            case ChapterButtonState.Unlock:
                return UnlockAnimation;
            case ChapterButtonState.Finish:
                return FinishAnimation;
            default:
                return LockAnimation;
        }
    }

    private static void SetStateObjectActive(GameObject stateObject, bool active)
    {
        if (stateObject == null)
        {
            return;
        }

        stateObject.SetActive(active);

        Graphic graphic = stateObject.GetComponent<Graphic>();
        if (graphic != null)
        {
            graphic.raycastTarget = false;
        }
    }
}

public enum ChapterButtonState
{
    Lock,
    Unlock,
    Finish
}

public sealed class UnlockArrowPointerMotion : MonoBehaviour
{
    private RectTransform rect;
    private Vector2 basePosition;
    private float range;
    private float seconds = 1f;
    private float startTime;

    public void Init(RectTransform target, Vector2 position, float moveRange, float cycleSeconds)
    {
        rect = target;
        basePosition = position;
        range = Mathf.Max(0f, moveRange);
        seconds = Mathf.Max(0.01f, cycleSeconds);
        startTime = Time.unscaledTime;
    }

    private void Update()
    {
        if (rect == null || range <= 0f)
        {
            return;
        }

        float t = (Time.unscaledTime - startTime) / seconds;
        float offset = Mathf.Sin(t * Mathf.PI * 2f) * range;
        rect.anchoredPosition = basePosition + new Vector2(offset, 0f);
    }
}
