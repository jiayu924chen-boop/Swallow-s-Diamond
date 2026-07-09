using UnityEngine;

[CreateAssetMenu(fileName = "BoardVisualConfig", menuName = "Swallow Diamond/Board Visual Config")]
public sealed class BoardVisualConfig : ScriptableObject
{
    [Header("Shared background")]
    public bool overrideBackgroundColor = true;
    public Color backgroundColor = Color.white;
    public Sprite backgroundSprite;

    [Header("Cells")]
    public Sprite boardCellSprite;
    public bool overrideCellTint = true;
    public Color cellTint = Color.white;
}
