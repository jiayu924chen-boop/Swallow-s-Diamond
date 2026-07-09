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
    private static bool hasPendingMenuGuide;
    private static GuideTextType pendingMenuGuideType;

    public static int RequestedLevel { get; private set; }
    public static int RequestedButtonIndex { get; private set; } = -1;

    public static bool IsTransitioning => transitionInProgress;

    public static void RequestMenuGuide(GuideTextType guideType)
    {
        hasPendingMenuGuide = true;
        pendingMenuGuideType = guideType;
    }

    public static bool TryConsumePendingMenuGuide(out GuideTextType guideType)
    {
        guideType = pendingMenuGuideType;
        if (!hasPendingMenuGuide)
        {
            return false;
        }

        hasPendingMenuGuide = false;
        return true;
    }

    public static void EnterMenuFromIntro()
    {
        if (transitionInProgress)
        {
            return;
        }

        RequestedButtonIndex = -1;
        RequestedLevel = 0;
        LoadSceneDeferred(MenuSceneName);
    }

    public static void StartLevel(int buttonIndex, int level)
    {
        if (transitionInProgress)
        {
            return;
        }

        RequestedButtonIndex = buttonIndex;
        RequestedLevel = level;
        LoadLevelSceneDeferred(GameSceneName);
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
        UnloadLevelSceneDeferred(GameSceneName);
    }

    public static bool CompleteActiveLevelAndTryGetNextLevel(out int nextLevel)
    {
        nextLevel = 0;
        if (transitionInProgress)
        {
            return false;
        }

        if (RequestedButtonIndex >= 0 && CarpetLevelMenu.CompleteButtonProgressAndTryGetNextLevel(RequestedButtonIndex, out nextLevel))
        {
            RequestedLevel = nextLevel;
            return true;
        }

        RequestedButtonIndex = -1;
        RequestedLevel = 0;
        UnloadLevelSceneDeferred(GameSceneName);
        return false;
    }

    public static void ReturnToMenu()
    {
        if (transitionInProgress)
        {
            return;
        }

        RequestedButtonIndex = -1;
        RequestedLevel = 0;
        UnloadLevelSceneDeferred(GameSceneName);
    }

    public static void ResetGameAndReturnToIntro()
    {
        RequestedButtonIndex = -1;
        RequestedLevel = 0;
        CarpetLevelMenu.ResetSavedProgress();
        CarpetBgmPlayer.RestartFromBeginning();
        LoadSceneDeferred(IntroSceneName);
    }

    private static void LoadSceneDeferred(string sceneName)
    {
        transitionInProgress = true;
        GetRunner().SwitchTo(sceneName, () => transitionInProgress = false);
    }

    private static void LoadLevelSceneDeferred(string sceneName)
    {
        transitionInProgress = true;
        GetRunner().LoadLevelAdditive(sceneName, () => transitionInProgress = false);
    }

    private static void UnloadLevelSceneDeferred(string sceneName)
    {
        transitionInProgress = true;
        GetRunner().UnloadLevel(sceneName, () => transitionInProgress = false);
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

    public void LoadLevelAdditive(string sceneName, Action onComplete)
    {
        StopAllCoroutines();
        StartCoroutine(LoadLevelAdditiveRoutine(sceneName, onComplete));
    }

    public void UnloadLevel(string sceneName, Action onComplete)
    {
        StopAllCoroutines();
        StartCoroutine(UnloadLevelRoutine(sceneName, onComplete));
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

    private IEnumerator LoadLevelAdditiveRoutine(string sceneName, Action onComplete)
    {
        ClearEventSystemSelection();
        SetSceneRootsActive(CarpetLevelFlow.MenuSceneName, false);
        yield return null;

        Scene existingScene = SceneManager.GetSceneByName(sceneName);
        if (existingScene.IsValid() && existingScene.isLoaded)
        {
            SceneManager.SetActiveScene(existingScene);
            ClearEventSystemSelection();
            yield return null;
            onComplete?.Invoke();
            yield break;
        }

        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        if (operation == null)
        {
            Debug.LogError("Failed to start loading scene: " + sceneName);
            SetSceneRootsActive(CarpetLevelFlow.MenuSceneName, true);
            onComplete?.Invoke();
            yield break;
        }

        while (!operation.isDone)
        {
            yield return null;
        }

        Scene loadedScene = SceneManager.GetSceneByName(sceneName);
        if (loadedScene.IsValid() && loadedScene.isLoaded)
        {
            SceneManager.SetActiveScene(loadedScene);
        }

        ClearEventSystemSelection();
        yield return null;
        onComplete?.Invoke();
    }

    private IEnumerator UnloadLevelRoutine(string sceneName, Action onComplete)
    {
        ClearEventSystemSelection();
        yield return null;

        Scene menuScene = SceneManager.GetSceneByName(CarpetLevelFlow.MenuSceneName);
        if (!menuScene.IsValid() || !menuScene.isLoaded)
        {
            AsyncOperation menuOperation = SceneManager.LoadSceneAsync(CarpetLevelFlow.MenuSceneName, LoadSceneMode.Single);
            if (menuOperation == null)
            {
                Debug.LogError("Failed to start loading scene: " + CarpetLevelFlow.MenuSceneName);
                onComplete?.Invoke();
                yield break;
            }

            while (!menuOperation.isDone)
            {
                yield return null;
            }

            ClearEventSystemSelection();
            yield return null;
            onComplete?.Invoke();
            yield break;
        }

        Scene scene = SceneManager.GetSceneByName(sceneName);
        if (scene.IsValid() && scene.isLoaded)
        {
            AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);
            if (operation == null)
            {
                Debug.LogError("Failed to start unloading scene: " + sceneName);
            }
            else
            {
                while (!operation.isDone)
                {
                    yield return null;
                }
            }
        }

        menuScene = SceneManager.GetSceneByName(CarpetLevelFlow.MenuSceneName);
        if (menuScene.IsValid() && menuScene.isLoaded)
        {
            SceneManager.SetActiveScene(menuScene);
        }
        SetSceneRootsActive(CarpetLevelFlow.MenuSceneName, true);
        CarpetLevelMenu.RefreshMenuState();

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

    private static void SetSceneRootsActive(string sceneName, bool active)
    {
        Scene scene = SceneManager.GetSceneByName(sceneName);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return;
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root != null && root.GetComponent<EventSystem>() == null)
            {
                root.SetActive(active);
            }
        }
    }
}
