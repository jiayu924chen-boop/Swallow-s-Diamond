using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class ChapterButtonStateController : MonoBehaviour
{
    [SerializeField] private GameObject lockState;
    [SerializeField] private GameObject unlockState;
    [SerializeField] private GameObject finishState;
    [SerializeField] private ChapterButtonState currentState = ChapterButtonState.Lock;
    [SerializeField] private Animator animator;

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
        if (animator != null && !string.IsNullOrEmpty(animationName))
        {
            animator.Play(animationName, 0, 0f);
        }
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
            graphic.raycastTarget = active;
        }
    }
}

public enum ChapterButtonState
{
    Lock,
    Unlock,
    Finish
}
