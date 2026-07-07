using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public static class CarpetLevelFlow
{
    public const string IntroSceneName = "Intro";
    public const string MenuSceneName = "LevelSelectMenu";
    public const string GameSceneName = "Main";

    private static CarpetSceneTransitionRunner runner;
    private static bool transitionInProgress;

    public static int RequestedLevel { get; private set; }
    public static int RequestedButtonIndex { get; private set; } = -1;

    public static bool IsTransitioning => transitionInProgress;

    public static void StartLevel(int buttonIndex, int level)
    {
        if (transitionInProgress)
        {
            return;
        }

        RequestedButtonIndex = buttonIndex;
        RequestedLevel = level;
        LoadSceneDeferred(GameSceneName);
    }

    public static int ConsumeRequestedLevel()
    {
        int level = RequestedLevel;
        RequestedLevel = 0;
        return level;
    }

    public static void CompleteActiveLevelAndReturn()
    {
        if (transitionInProgress)
        {
            return;
        }

        if (RequestedButtonIndex >= 0)
        {
            CarpetLevelMenu.AdvanceButtonProgress(RequestedButtonIndex);
        }
        RequestedButtonIndex = -1;
        RequestedLevel = 0;
        LoadSceneDeferred(MenuSceneName);
    }

    public static void ReturnToMenu()
    {
        if (transitionInProgress)
        {
            return;
        }

        RequestedButtonIndex = -1;
        RequestedLevel = 0;
        LoadSceneDeferred(MenuSceneName);
    }

    private static void LoadSceneDeferred(string sceneName)
    {
        transitionInProgress = true;
        GetRunner().SwitchTo(sceneName, () => transitionInProgress = false);
    }

    private static CarpetSceneTransitionRunner GetRunner()
    {
        if (runner != null)
        {
            return runner;
        }

        GameObject host = new GameObject("Carpet Scene Transition Runner");
        UnityEngine.Object.DontDestroyOnLoad(host);
        runner = host.AddComponent<CarpetSceneTransitionRunner>();
        return runner;
    }
}

public sealed class CarpetSceneTransitionRunner : MonoBehaviour
{
    public void SwitchTo(string sceneName, Action onComplete)
    {
        StopAllCoroutines();
        StartCoroutine(SwitchRoutine(sceneName, onComplete));
    }

    private IEnumerator SwitchRoutine(string sceneName, Action onComplete)
    {
        ClearEventSystemSelection();
        yield return null;

        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        if (operation == null)
        {
            Debug.LogError("Failed to start loading scene: " + sceneName);
            onComplete?.Invoke();
            yield break;
        }

        while (!operation.isDone)
        {
            yield return null;
        }

        ClearEventSystemSelection();
        yield return null;
        onComplete?.Invoke();
    }

    private static void ClearEventSystemSelection()
    {
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
