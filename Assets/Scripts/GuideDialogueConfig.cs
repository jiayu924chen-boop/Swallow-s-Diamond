using System;
using UnityEngine;

[CreateAssetMenu(fileName = "GuideDialogueConfig", menuName = "Swallow Diamond/Guide Dialogue")]
public sealed class GuideDialogueConfig : ScriptableObject
{
    [TextArea(2, 6)]
    public string[] lines = Array.Empty<string>();
}
