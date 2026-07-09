using System;
using UnityEngine;

public sealed class ChapterButtonStateController : MonoBehaviour
{
    [SerializeField] private GameObject lockState;
    [SerializeField] private GameObject unlockState;
    [SerializeField] private GameObject finishState;
    [SerializeField] private ChapterButtonState currentState = ChapterButtonState.Lock;

    public ChapterButtonState CurrentState => currentState;

    private void Awake()
    {
        ResolveBindings();
        ApplyState(currentState);
    }

    private void Reset()
    {
        ResolveBindings();
        ApplyState(ChapterButtonState.Lock);
    }

    private void OnValidate()
    {
        ResolveBindings();
        ApplyState(currentState);
    }

    public void SetLockState()
    {
        ApplyState(ChapterButtonState.Lock);
    }

    public void SetUnlockState()
    {
        ApplyState(ChapterButtonState.Unlock);
    }

    public void SetFinishState()
    {
        ApplyState(ChapterButtonState.Finish);
    }

    public void ApplyState(ChapterButtonState state)
    {
        currentState = state;

        if (lockState != null)
        {
            lockState.SetActive(state == ChapterButtonState.Lock);
        }
        if (unlockState != null)
        {
            unlockState.SetActive(state == ChapterButtonState.Unlock);
        }
        if (finishState != null)
        {
            finishState.SetActive(state == ChapterButtonState.Finish);
        }
    }

    private void ResolveBindings()
    {
        lockState = lockState != null ? lockState : FindDirectChild("Lock");
        unlockState = unlockState != null ? unlockState : FindDirectChild("Unlock", "UnLock");
        finishState = finishState != null ? finishState : FindDirectChild("Finish");
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
}

public enum ChapterButtonState
{
    Lock,
    Unlock,
    Finish
}
