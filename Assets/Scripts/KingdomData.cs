using UnityEngine;

[CreateAssetMenu(fileName = "KingdomData_New", menuName = "Kingdoms/Kingdom Data")]
public class KingdomData : ScriptableObject
{
    [Header("Identity")]
    public string kingdomName;

    [Header("Kingdom Logo")]
    public Sprite kingdomLogo;

    [Header("Flag")]
    public Sprite flagImage;

    [Header("Description Background")]
    public Sprite descriptionBackground;

    [Header("Description - Middle")]
    [TextArea(3, 6)]
    public string descriptionMiddle;

    [Header("Description - Bottom")]
    [TextArea(3, 6)]
    public string descriptionBottom;
}