public static class CarpetBgmPlayer
{
    public static void ApplySavedSetting()
    {
        AudioManager.ApplySavedSetting();
    }

    public static void EnsurePlaying()
    {
        AudioManager.EnsureDefaultBgmPlaying();
    }

    public static void RestartFromBeginning()
    {
        AudioManager.RestartDefaultBgm();
    }
}
