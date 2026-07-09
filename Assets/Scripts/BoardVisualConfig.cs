using UnityEngine;

[CreateAssetMenu(fileName = "BoardVisualConfig", menuName = "Swallow Diamond/Board Visual Config")]
public sealed class BoardVisualConfig : ScriptableObject
{
    [Header("Board background")]
    public bool overrideBoardBackgroundColor = true;
    public Color boardBackgroundColor = Color.white;
    public Sprite boardBackgroundSprite;

    [Header("Cells")]
    public Sprite boardCellSprite;
    public bool overrideCellTint = true;
    public Color cellTint = Color.white;
}
