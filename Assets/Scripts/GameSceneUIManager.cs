using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// UI controller for the Game (battle) scene: the cloud screen-cover
/// transition into the game scene panel, and the speed dial / menu bar
/// toggle. Attach to a single "GameSceneUIManager" GameObject in the
/// Game scene and wire every reference below in the Inspector.
/// </summary>
public class GameSceneUIManager : MonoBehaviour
{
    public static GameSceneUIManager Instance { get; private set; }

    private void Awake()
    {
        Instance = this;

        // Force the clouds to the top of the Canvas draw order regardless of
        // where cloudsContainer sits in the hierarchy - this is the most common
        // reason clouds "don't show": they were rendering underneath the panel.
        if (cloudsContainer != null) cloudsContainer.SetAsLastSibling();

        SetupSpeedDial();
    }

    // ---------------------------------------------------------------
    // GAME SCENE CLOUD TRANSITION
    // On entry: clouds are spawned at the exact positions you set in
    // cloudHomePositions (same list, same positions, every time - no
    // randomization of placement or count). Each cloud flies in from the
    // direction it actually sits relative to screen center - left-side
    // clouds enter from the left, top clouds from the top, corner clouds
    // enter diagonally, etc. - with a randomized start delay per cloud so
    // the entrance still feels organic rather than perfectly synchronized.
    // Once they've covered the screen the panel underneath swaps while
    // hidden, then after a hold they all fly back out along the exact path
    // they came in on and get destroyed - nothing stays behind.
    // "cloudsContainer" must be a RectTransform stretched to the full
    // Canvas (same size as the screen) so anchoredPosition math lines up.
    // "cloudPrefab" just needs an Image component on its root - pivot 0.5/0.5.
    // ---------------------------------------------------------------
    [Header("Game Scene Cloud Transition")]
    [Tooltip("Panel to SetActive(true) once the clouds fully cover the screen.")]
    [SerializeField] private GameObject gameScenePanel;
    [Tooltip("Full-screen RectTransform that parents the spawned cloud instances.")]
    [SerializeField] private RectTransform cloudsContainer;
    [Tooltip("Cloud prefab - must have an Image component on its root.")]
    [SerializeField] private GameObject cloudPrefab;
    [Tooltip("Every cloud's exact resting position (anchoredPosition inside cloudsContainer). Add/remove elements in this list to control how many clouds spawn - these positions are used exactly as entered, every single time, no randomization. Use the 'Generate Even Grid Cloud Positions' context menu below for a starting point you can then hand-tune.")]
    [SerializeField] private List<Vector2> cloudHomePositions = new List<Vector2>();
    [Tooltip("Fixed pixel size for every cloud.")]
    [SerializeField] private Vector2 cloudSize = new Vector2(700f, 700f);
    [SerializeField] private float cloudRotationJitter = 8f;
    [SerializeField] private float cloudMoveDuration = 0.55f;
    [Tooltip("Each cloud gets a random start delay between 0 and this value, so the entrance/exit reads as organic rather than a rigid synchronized grid. Set to 0 for every cloud to move at exactly the same time.")]
    [SerializeField] private float cloudStaggerMax = 0.2f;
    [Tooltip("How long the clouds stay fully covering the screen before they part again.")]
    [SerializeField] private float cloudsCoveredHoldDuration = 0.25f;
    [SerializeField] private Ease cloudInEase = Ease.OutCubic;
    [SerializeField] private Ease cloudOutEase = Ease.InCubic;
    [Tooltip("Logs cloud counts/positions to the Console - turn off once the transition is confirmed working.")]
    [SerializeField] private bool enableCloudDebugLogs = true;

    [Header("Position Generator (editor helper only)")]
    [Tooltip("Used only by the 'Generate Even Grid Cloud Positions' context menu below, to fill cloudHomePositions with a gap-free starting layout. Has no effect at runtime - only cloudHomePositions itself matters when playing.")]
    [Range(0.3f, 1f)]
    [SerializeField] private float generatorGridOverlap = 0.65f;

    private class CloudSlot
    {
        public RectTransform rect;
        public Image image;
        public Vector2 homePos;
        public Vector2 offscreenPos;
    }

    private readonly List<CloudSlot> cloudSlots = new List<CloudSlot>();
    private bool isCloudTransitioning;

    /// <summary>
    /// Right-click the GameSceneUIManager component header in the Inspector
    /// (works in or out of Play mode) and pick this to fill cloudHomePositions
    /// with an even, gap-free grid based on cloudsContainer's current size.
    /// Use it as a starting point, then hand-edit individual entries in the
    /// list above to nudge specific clouds exactly where you want them.
    /// </summary>
    [ContextMenu("Generate Even Grid Cloud Positions")]
    private void GenerateEvenGridPositions()
    {
        if (cloudsContainer == null)
        {
            Debug.LogWarning("GameSceneUIManager: cloudsContainer not assigned, can't generate positions.");
            return;
        }

        Rect bounds = cloudsContainer.rect;
        float spacingX = cloudSize.x * generatorGridOverlap;
        float spacingY = cloudSize.y * generatorGridOverlap;

        int columns = Mathf.Max(1, Mathf.CeilToInt(bounds.width / spacingX)) + 2;
        int rows = Mathf.Max(1, Mathf.CeilToInt(bounds.height / spacingY)) + 2;

        float gridWidth = spacingX * columns;
        float gridHeight = spacingY * rows;
        float originX = bounds.center.x - gridWidth * 0.5f;
        float originY = bounds.center.y - gridHeight * 0.5f;

        cloudHomePositions.Clear();
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                cloudHomePositions.Add(new Vector2(
                    originX + spacingX * (col + 0.5f),
                    originY + spacingY * (row + 0.5f)));
            }
        }

        Debug.Log($"GameSceneUIManager: generated {cloudHomePositions.Count} positions into cloudHomePositions - hand-tune any entry in the Inspector list, or add/remove entries to change the cloud count.");
    }

    /// <summary>
    /// Call this instead of "gameScenePanel.SetActive(true)" directly.
    /// Optionally pass the panel that's currently showing so it gets
    /// hidden while the clouds are covering the screen.
    /// </summary>
    public void ShowGameScenePanelWithClouds(GameObject panelToHide = null)
    {
        if (isCloudTransitioning) return;

        if (gameScenePanel == null)
        {
            Debug.LogWarning("GameSceneUIManager: gameScenePanel not assigned, skipping cloud transition.");
            return;
        }

        isCloudTransitioning = true;

        PlayCloudsIn(() =>
        {
            if (panelToHide != null) panelToHide.SetActive(false);
            gameScenePanel.SetActive(true);

            DOVirtual.DelayedCall(cloudsCoveredHoldDuration, PlayCloudsOut);
        });
    }

    /// <summary>
    /// Right-click the GameSceneUIManager component header in the Inspector
    /// while in Play mode and pick this from the menu to test the cloud
    /// transition without needing any other game logic wired up yet.
    /// </summary>
    [ContextMenu("Test: Play Cloud Transition")]
    private void TestPlayCloudTransition()
    {
        ShowGameScenePanelWithClouds();
    }

    private void SpawnClouds()
    {
        ClearClouds();

        if (cloudPrefab == null || cloudsContainer == null)
        {
            Debug.LogWarning("GameSceneUIManager: cloudPrefab or cloudsContainer not assigned, skipping cloud transition.");
            return;
        }

        if (cloudHomePositions.Count == 0)
        {
            Debug.LogWarning("GameSceneUIManager: cloudHomePositions is empty - right-click the component header and use 'Generate Even Grid Cloud Positions' for a starting layout, or add entries by hand.");
            return;
        }

        Rect bounds = cloudsContainer.rect;

        // Far enough that a cloud starting from any home position, pushed out
        // in any random direction, ends up fully off-screen.
        float clearDistance = bounds.size.magnitude + cloudSize.magnitude;

        foreach (Vector2 homePos in cloudHomePositions)
        {
            GameObject cloudGO = Instantiate(cloudPrefab, cloudsContainer, false);
            RectTransform cloudRect = cloudGO.GetComponent<RectTransform>();
            Image cloudImage = cloudGO.GetComponent<Image>();

            cloudRect.sizeDelta = cloudSize; // fixed size, always
            cloudRect.localRotation = Quaternion.Euler(0f, 0f,
                UnityEngine.Random.Range(-cloudRotationJitter, cloudRotationJitter));

            if (cloudImage != null)
            {
                Color c = cloudImage.color;
                c.a = 0f; // starts invisible - fades in to 1 as it flies in
                cloudImage.color = c;
            }
            else if (enableCloudDebugLogs)
            {
                Debug.LogWarning("GameSceneUIManager: cloudPrefab has no Image component on its root - can't fade alpha for this cloud.");
            }

            // Direction is based on where this cloud actually sits relative to
            // screen center - left-half clouds come from the left, top-half
            // clouds come from the top, corner clouds come in diagonally, etc.
            // (homePos itself is untouched - exact, no jitter, no randomization.)
            Vector2 offsetFromCenter = homePos - bounds.center;
            Vector2 dir = offsetFromCenter.sqrMagnitude > 0.0001f
                ? offsetFromCenter.normalized
                : Vector2.up; // dead-center cloud (rare) - just default to entering from above
            Vector2 offscreenPos = homePos + dir * clearDistance;

            cloudRect.anchoredPosition = offscreenPos;

            cloudSlots.Add(new CloudSlot
            {
                rect = cloudRect,
                image = cloudImage,
                homePos = homePos,
                offscreenPos = offscreenPos
            });
        }
    }

    private void PlayCloudsIn(Action onComplete)
    {
        SpawnClouds();

        if (cloudSlots.Count == 0)
        {
            if (enableCloudDebugLogs) Debug.LogWarning("GameSceneUIManager: SpawnClouds() produced 0 cloud slots - check the warnings above for an unassigned field.");
            onComplete?.Invoke();
            return;
        }

        if (enableCloudDebugLogs)
        {
            CloudSlot first = cloudSlots[0];
            Debug.Log($"GameSceneUIManager: spawned {cloudSlots.Count} clouds. First cloud: anchoredPosition={first.rect.anchoredPosition}, moving to homePos={first.homePos}, sizeDelta={first.rect.sizeDelta}, containerRect={cloudsContainer.rect}.");
        }

        Sequence seq = DOTween.Sequence();
        foreach (CloudSlot slot in cloudSlots)
        {
            float delay = UnityEngine.Random.Range(0f, cloudStaggerMax);
            seq.Insert(delay, slot.rect.DOAnchorPos(slot.homePos, cloudMoveDuration).SetEase(cloudInEase));
            if (slot.image != null)
            {
                seq.Insert(delay, slot.image.DOFade(1f, cloudMoveDuration).SetEase(cloudInEase));
            }
        }
        seq.OnComplete(() =>
        {
            if (enableCloudDebugLogs && cloudSlots.Count > 0)
            {
                Debug.Log($"GameSceneUIManager: cloud fly-in finished. First cloud now at anchoredPosition={cloudSlots[0].rect.anchoredPosition} (should match homePos={cloudSlots[0].homePos}).");
            }
            onComplete?.Invoke();
        });
    }

    private void PlayCloudsOut()
    {
        if (cloudSlots.Count == 0)
        {
            isCloudTransitioning = false;
            return;
        }

        Sequence seq = DOTween.Sequence();
        foreach (CloudSlot slot in cloudSlots)
        {
            float delay = UnityEngine.Random.Range(0f, cloudStaggerMax);
            seq.Insert(delay, slot.rect.DOAnchorPos(slot.offscreenPos, cloudMoveDuration).SetEase(cloudOutEase));
            if (slot.image != null)
            {
                seq.Insert(delay, slot.image.DOFade(0f, cloudMoveDuration).SetEase(cloudOutEase));
            }
        }
        seq.OnComplete(() =>
        {
            // Every cloud goes back out and gets destroyed - nothing stays behind.
            foreach (CloudSlot slot in cloudSlots)
            {
                if (slot.rect != null) Destroy(slot.rect.gameObject);
            }
            cloudSlots.Clear();
            isCloudTransitioning = false;
        });
    }

    private void ClearClouds()
    {
        foreach (CloudSlot slot in cloudSlots)
        {
            if (slot.rect != null) Destroy(slot.rect.gameObject);
        }
        cloudSlots.Clear();
    }

    // ---------------------------------------------------------------
    // SPEED DIAL (MENU BAR TOGGLE)
    // Ported from MenuBarToggle.cs as-is: toggles the menu icon between
    // blue/gray and slides the sub-buttons row in/out from the right
    // when the menu button is clicked.
    // ---------------------------------------------------------------
    [Header("Speed Dial / Menu Bar")]
    [SerializeField] private GameObject speedDialIconBlue;   // shown when closed
    [SerializeField] private GameObject speedDialIconGray;   // shown when open
    [SerializeField] private Button speedDialMenuButton;
    [SerializeField] private RectTransform speedDialButtonsRow;
    [SerializeField] private CanvasGroup speedDialButtonsRowCanvasGroup; // optional, blocks clicks while hidden
    [SerializeField] private float speedDialHiddenPosX = 540f; // off to the right (match row width)
    [SerializeField] private float speedDialShownPosX = 0f;
    [SerializeField] private float speedDialSlideDuration = 0.35f;
    [SerializeField] private Ease speedDialSlideEase = Ease.OutCubic;

    private bool isSpeedDialOpen = false;
    private Tween speedDialSlideTween;

    private void SetupSpeedDial()
    {
        if (speedDialMenuButton == null || speedDialButtonsRow == null) return;

        speedDialMenuButton.onClick.AddListener(OnSpeedDialMenuButtonClicked);

        // Start closed: icon blue, row hidden off-screen
        SetSpeedDialIconState(open: false);

        Vector2 pos = speedDialButtonsRow.anchoredPosition;
        pos.x = speedDialHiddenPosX;
        speedDialButtonsRow.anchoredPosition = pos;

        SetSpeedDialRowInteractable(false);
    }

    private void OnSpeedDialMenuButtonClicked()
    {
        isSpeedDialOpen = !isSpeedDialOpen;

        SetSpeedDialIconState(isSpeedDialOpen);
        SetSpeedDialRowInteractable(isSpeedDialOpen);

        speedDialSlideTween?.Kill();

        float targetX = isSpeedDialOpen ? speedDialShownPosX : speedDialHiddenPosX;
        speedDialSlideTween = speedDialButtonsRow
            .DOAnchorPosX(targetX, speedDialSlideDuration)
            .SetEase(speedDialSlideEase);
    }

    private void SetSpeedDialIconState(bool open)
    {
        // gray shows while open, blue shows while closed
        if (speedDialIconBlue != null) speedDialIconBlue.SetActive(!open);
        if (speedDialIconGray != null) speedDialIconGray.SetActive(open);
    }

    private void SetSpeedDialRowInteractable(bool value)
    {
        if (speedDialButtonsRowCanvasGroup == null) return;
        speedDialButtonsRowCanvasGroup.interactable = value;
        speedDialButtonsRowCanvasGroup.blocksRaycasts = value;
        // leave alpha as-is if you're relying on the mask to hide it;
        // set alpha too if you want a fade alongside the slide:
        // speedDialButtonsRowCanvasGroup.alpha = value ? 1f : 1f;
    }

    private void OnDestroy()
    {
        if (speedDialMenuButton != null) speedDialMenuButton.onClick.RemoveListener(OnSpeedDialMenuButtonClicked);
        speedDialSlideTween?.Kill();
        ClearClouds();
    }
}