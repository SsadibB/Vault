//using System;
//using System.Collections.Generic;
//using UnityEngine;
//using UnityEngine.UI;
//using TMPro;
//using DG.Tweening;

///// <summary>
///// Central UI controller for the whole flow:
///// LoadingPanel (bar fills) -> StartPanel (pulsing button) -> KingdomSelection
///// (list) -> Kingdom detail panel (slides in from the right, list slides out
///// to the left; reversed on Back). Attach to a single "UIManager" GameObject
///// in the scene and wire every reference below in the Inspector.
///// </summary>
//public class UIManager : MonoBehaviour
//{
//    public static UIManager Instance { get; private set; }

//    // ---------------------------------------------------------------
//    // LOADING
//    // ---------------------------------------------------------------
//    [Header("Loading")]
//    [SerializeField] private GameObject loadingPanel;
//    [Tooltip("Assign ONE of these two - Slider or fill Image.")]
//    [SerializeField] private Slider loadingBarSlider;
//    [SerializeField] private Image loadingBarImage;
//    [SerializeField] private float loadingDuration = 2f;
//    [SerializeField] private Ease loadingEase = Ease.Linear;

//    // ---------------------------------------------------------------
//    // START PANEL
//    // ---------------------------------------------------------------
//    [Header("Start Panel")]
//    [SerializeField] private GameObject startPanel;
//    [SerializeField] private Button startButton;
//    [Tooltip("The RectTransform that pulses - usually the Start button's Text (TMP).")]
//    [SerializeField] private RectTransform startButtonPulseTarget;
//    [SerializeField] private float pulseScale = 1.1f;
//    [SerializeField] private float pulseDuration = 0.6f;

//    private Tween pulseTween;

//    // ---------------------------------------------------------------
//    // KINGDOM SELECTION
//    // ---------------------------------------------------------------
//    [Header("Kingdom Selection")]
//    [SerializeField] private GameObject kingdomSelectionPanel;
//    [SerializeField] private GameObject kingdomsListPanel;
//    [SerializeField] private GameObject kingdomDetailPanel;
//    [SerializeField] private Button backButton;

//    [Serializable]
//    public class KingdomEntry
//    {
//        public Button button;
//        public KingdomData data;
//    }

//    [Tooltip("One entry per kingdom button in the list - drag the button and its matching KingdomData asset.")]
//    [SerializeField] private List<KingdomEntry> kingdomEntries;

//    [Header("Kingdom Detail References")]
//    [SerializeField] private Image detailLogoImage;
//    [SerializeField] private Image detailFlagImage;
//    [SerializeField] private Image detailBackgroundImage;
//    [SerializeField] private TMP_Text detailNameText;
//    [SerializeField] private TMP_Text detailMiddleText;
//    [SerializeField] private TMP_Text detailBottomText;

//    [Header("Transitions")]
//    [SerializeField] private float fadeDuration = 0.3f;
//    [Tooltip("Duration of the list <-> detail slide transition.")]
//    [SerializeField] private float slideDuration = 0.35f;
//    [SerializeField] private Ease slideInEase = Ease.OutCubic;
//    [SerializeField] private Ease slideOutEase = Ease.InCubic;

//    private CanvasGroup kingdomSelectionCanvasGroup;
//    private RectTransform kingdomsListRect;
//    private RectTransform kingdomDetailRect;
//    private Vector2 kingdomsListHomePos;
//    private Vector2 kingdomDetailHomePos;
//    private bool isTransitioning;

//    // ---------------------------------------------------------------
//    // SETUP
//    // ---------------------------------------------------------------
//    private void Awake()
//    {
//        Instance = this;

//        kingdomSelectionCanvasGroup = GetOrAddCanvasGroup(kingdomSelectionPanel);

//        kingdomsListRect = kingdomsListPanel.GetComponent<RectTransform>();
//        kingdomDetailRect = kingdomDetailPanel.GetComponent<RectTransform>();
//        kingdomsListHomePos = kingdomsListRect.anchoredPosition;
//        kingdomDetailHomePos = kingdomDetailRect.anchoredPosition;

//        startButton.onClick.AddListener(OnStartButtonClicked);
//        backButton.onClick.AddListener(HideKingdomDetail);

//        foreach (var entry in kingdomEntries)
//        {
//            if (entry.button == null || entry.data == null)
//            {
//                Debug.LogWarning("UIManager: a Kingdom Entry is missing its Button or KingdomData.");
//                continue;
//            }

//            KingdomData data = entry.data; // local copy for the closure
//            entry.button.onClick.AddListener(() => ShowKingdomDetail(data));
//        }

//        kingdomDetailPanel.SetActive(false);
//        kingdomSelectionPanel.SetActive(false);
//    }

//    private void Start()
//    {
//        loadingPanel.SetActive(true);
//        startPanel.SetActive(false);
//        StartLoading();
//    }

//    private CanvasGroup GetOrAddCanvasGroup(GameObject go)
//    {
//        var cg = go.GetComponent<CanvasGroup>();
//        if (cg == null) cg = go.AddComponent<CanvasGroup>();
//        return cg;
//    }

//    // ---------------------------------------------------------------
//    // LOADING
//    // ---------------------------------------------------------------
//    private void StartLoading()
//    {
//        if (loadingBarSlider != null)
//        {
//            loadingBarSlider.value = 0f;
//            loadingBarSlider
//                .DOValue(1f, loadingDuration)
//                .SetEase(loadingEase)
//                .OnComplete(OnLoadingComplete);
//        }
//        else if (loadingBarImage != null)
//        {
//            loadingBarImage.fillAmount = 0f;
//            loadingBarImage
//                .DOFillAmount(1f, loadingDuration)
//                .SetEase(loadingEase)
//                .OnComplete(OnLoadingComplete);
//        }
//        else
//        {
//            Debug.LogWarning("UIManager: no loading bar assigned, skipping straight to StartPanel.");
//            OnLoadingComplete();
//        }
//    }

//    private void OnLoadingComplete()
//    {
//        loadingPanel.SetActive(false);
//        startPanel.SetActive(true);
//        StartPulse();
//    }

//    // ---------------------------------------------------------------
//    // START PANEL
//    // ---------------------------------------------------------------
//    private void StartPulse()
//    {
//        if (startButtonPulseTarget == null) return;

//        startButtonPulseTarget.localScale = Vector3.one;
//        pulseTween = startButtonPulseTarget
//            .DOScale(pulseScale, pulseDuration)
//            .SetEase(Ease.InOutSine)
//            .SetLoops(-1, LoopType.Yoyo);
//    }

//    private void StopPulse()
//    {
//        pulseTween?.Kill();
//        if (startButtonPulseTarget != null)
//        {
//            startButtonPulseTarget.localScale = Vector3.one;
//        }
//    }

//    private void OnStartButtonClicked()
//    {
//        StopPulse();

//        startPanel.SetActive(false);
//        kingdomSelectionPanel.SetActive(true);
//        kingdomsListPanel.SetActive(true);

//        kingdomSelectionCanvasGroup.alpha = 0f;
//        kingdomSelectionCanvasGroup.DOFade(1f, fadeDuration);
//    }

//    // ---------------------------------------------------------------
//    // KINGDOM SELECTION / DETAIL  (slide right -> left)
//    // ---------------------------------------------------------------
//    public void ShowKingdomDetail(KingdomData data)
//    {
//        if (isTransitioning) return;
//        isTransitioning = true;

//        if (detailLogoImage != null) detailLogoImage.sprite = data.kingdomLogo;
//        if (detailFlagImage != null) detailFlagImage.sprite = data.flagImage;
//        if (detailBackgroundImage != null) detailBackgroundImage.sprite = data.descriptionBackground;
//        if (detailNameText != null) detailNameText.text = data.kingdomName;
//        if (detailMiddleText != null) detailMiddleText.text = data.descriptionMiddle;
//        if (detailBottomText != null) detailBottomText.text = data.descriptionBottom;

//        float width = kingdomDetailRect.rect.width;

//        // Detail panel starts off-screen to the right, slides in to home.
//        kingdomDetailPanel.SetActive(true);
//        kingdomDetailRect.anchoredPosition = kingdomDetailHomePos + new Vector2(width, 0f);
//        kingdomDetailRect
//            .DOAnchorPos(kingdomDetailHomePos, slideDuration)
//            .SetEase(slideInEase);

//        // List panel slides out to the left and deactivates once gone.
//        kingdomsListRect
//            .DOAnchorPos(kingdomsListHomePos - new Vector2(width, 0f), slideDuration)
//            .SetEase(slideOutEase)
//            .OnComplete(() =>
//            {
//                kingdomsListPanel.SetActive(false);
//                isTransitioning = false;
//            });
//    }

//    private void HideKingdomDetail()
//    {
//        if (isTransitioning) return;
//        isTransitioning = true;

//        float width = kingdomDetailRect.rect.width;

//        // List panel comes back in from the left.
//        kingdomsListPanel.SetActive(true);
//        kingdomsListRect.anchoredPosition = kingdomsListHomePos - new Vector2(width, 0f);
//        kingdomsListRect
//            .DOAnchorPos(kingdomsListHomePos, slideDuration)
//            .SetEase(slideInEase);

//        // Detail panel slides out to the right and deactivates once gone.
//        kingdomDetailRect
//            .DOAnchorPos(kingdomDetailHomePos + new Vector2(width, 0f), slideDuration)
//            .SetEase(slideOutEase)
//            .OnComplete(() =>
//            {
//                kingdomDetailPanel.SetActive(false);
//                isTransitioning = false;
//            });
//    }
//}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Central UI controller for the whole flow:
/// LoadingPanel (bar fills) -> StartPanel (pulsing button) -> KingdomSelection
/// (list) -> Kingdom detail panel (slides in from the right, list slides out
/// to the left; reversed on Back). Attach to a single "UIManager" GameObject
/// in the scene and wire every reference below in the Inspector.
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    // ---------------------------------------------------------------
    // LOADING
    // ---------------------------------------------------------------
    [Header("Loading")]
    [SerializeField] private GameObject loadingPanel;
    [Tooltip("Assign ONE of these two - Slider or fill Image.")]
    [SerializeField] private Slider loadingBarSlider;
    [SerializeField] private Image loadingBarImage;
    [SerializeField] private float loadingDuration = 2f;
    [SerializeField] private Ease loadingEase = Ease.Linear;

    // ---------------------------------------------------------------
    // START PANEL
    // ---------------------------------------------------------------
    [Header("Start Panel")]
    [SerializeField] private GameObject startPanel;
    [SerializeField] private Button startButton;
    [Tooltip("The RectTransform that pulses - usually the Start button's Text (TMP).")]
    [SerializeField] private RectTransform startButtonPulseTarget;
    [SerializeField] private float pulseScale = 1.1f;
    [SerializeField] private float pulseDuration = 0.6f;

    private Tween pulseTween;

    // ---------------------------------------------------------------
    // KINGDOM SELECTION
    // ---------------------------------------------------------------
    [Header("Kingdom Selection")]
    [SerializeField] private GameObject kingdomSelectionPanel;
    [SerializeField] private GameObject kingdomsListPanel;
    [SerializeField] private GameObject kingdomDetailPanel;
    [SerializeField] private Button backButton;

    [Serializable]
    public class KingdomEntry
    {
        public Button button;
        public KingdomData data;
    }

    [Tooltip("One entry per kingdom button in the list - drag the button and its matching KingdomData asset.")]
    [SerializeField] private List<KingdomEntry> kingdomEntries;

    [Header("Kingdom Detail References")]
    [Tooltip("The 'KingdomFlag' child (flag image + name text) - slides in from the LEFT.")]
    [SerializeField] private RectTransform kingdomFlagRect;
    [Tooltip("The 'Kingdom_Description' child - slides in from the RIGHT.")]
    [SerializeField] private RectTransform kingdomDescriptionRect;
    [SerializeField] private Image detailLogoImage;
    [SerializeField] private Image detailFlagImage;
    [SerializeField] private Image detailBackgroundImage;
    [SerializeField] private TMP_Text detailNameText;
    [SerializeField] private TMP_Text detailMiddleText;
    [SerializeField] private TMP_Text detailBottomText;

    [Header("Transitions")]
    [SerializeField] private float fadeDuration = 0.3f;
    [Tooltip("Duration of the list <-> detail slide transition.")]
    [SerializeField] private float slideDuration = 0.35f;
    [SerializeField] private Ease slideInEase = Ease.OutCubic;
    [SerializeField] private Ease slideOutEase = Ease.InCubic;

    private CanvasGroup kingdomSelectionCanvasGroup;
    private RectTransform kingdomsListRect;
    private Vector2 kingdomsListHomePos;
    private Vector2 kingdomFlagHomePos;
    private Vector2 kingdomDescriptionHomePos;
    private bool isTransitioning;

    // ---------------------------------------------------------------
    // SETUP
    // ---------------------------------------------------------------
    private void Awake()
    {
        Instance = this;

        // Each Init method below is null-checked internally so a scene that only
        // wires up SOME of these systems (e.g. a Game scene that only needs the
        // cloud transition + speed dial, not the Loading/Start/Kingdom flow)
        // won't throw a NullReferenceException here and get this component
        // auto-disabled by Unity.
        InitStartFlow();
        InitKingdomSelection();
    }

    private void InitStartFlow()
    {
        if (startButton != null) startButton.onClick.AddListener(OnStartButtonClicked);
    }

    private void InitKingdomSelection()
    {
        if (kingdomSelectionPanel == null || kingdomsListPanel == null
            || kingdomFlagRect == null || kingdomDescriptionRect == null)
        {
            return;
        }

        kingdomSelectionCanvasGroup = GetOrAddCanvasGroup(kingdomSelectionPanel);

        kingdomsListRect = kingdomsListPanel.GetComponent<RectTransform>();
        kingdomsListHomePos = kingdomsListRect.anchoredPosition;
        kingdomFlagHomePos = kingdomFlagRect.anchoredPosition;
        kingdomDescriptionHomePos = kingdomDescriptionRect.anchoredPosition;

        if (backButton != null) backButton.onClick.AddListener(HideKingdomDetail);

        if (kingdomEntries != null)
        {
            foreach (var entry in kingdomEntries)
            {
                if (entry.button == null || entry.data == null)
                {
                    Debug.LogWarning("UIManager: a Kingdom Entry is missing its Button or KingdomData.");
                    continue;
                }

                KingdomData data = entry.data;   // local copy for the closure
                Button clickedButton = entry.button;
                entry.button.onClick.AddListener(() => ShowKingdomDetail(data, clickedButton));
            }
        }

        if (kingdomDetailPanel != null) kingdomDetailPanel.SetActive(false);
        kingdomSelectionPanel.SetActive(false);
    }

    private void Start()
    {
        if (loadingPanel == null || startPanel == null) return;

        loadingPanel.SetActive(true);
        startPanel.SetActive(false);
        StartLoading();
    }

    private CanvasGroup GetOrAddCanvasGroup(GameObject go)
    {
        var cg = go.GetComponent<CanvasGroup>();
        if (cg == null) cg = go.AddComponent<CanvasGroup>();
        return cg;
    }

    // ---------------------------------------------------------------
    // LOADING
    // ---------------------------------------------------------------
    private void StartLoading()
    {
        if (loadingBarSlider != null)
        {
            loadingBarSlider.value = 0f;
            loadingBarSlider
                .DOValue(1f, loadingDuration)
                .SetEase(loadingEase)
                .OnComplete(OnLoadingComplete);
        }
        else if (loadingBarImage != null)
        {
            loadingBarImage.fillAmount = 0f;
            loadingBarImage
                .DOFillAmount(1f, loadingDuration)
                .SetEase(loadingEase)
                .OnComplete(OnLoadingComplete);
        }
        else
        {
            Debug.LogWarning("UIManager: no loading bar assigned, skipping straight to StartPanel.");
            OnLoadingComplete();
        }
    }

    private void OnLoadingComplete()
    {
        loadingPanel.SetActive(false);
        startPanel.SetActive(true);
        StartPulse();
    }

    // ---------------------------------------------------------------
    // START PANEL
    // ---------------------------------------------------------------
    private void StartPulse()
    {
        if (startButtonPulseTarget == null) return;

        startButtonPulseTarget.localScale = Vector3.one;
        pulseTween = startButtonPulseTarget
            .DOScale(pulseScale, pulseDuration)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo);
    }

    private void StopPulse()
    {
        pulseTween?.Kill();
        if (startButtonPulseTarget != null)
        {
            startButtonPulseTarget.localScale = Vector3.one;
        }
    }

    private void OnStartButtonClicked()
    {
        StopPulse();

        startPanel.SetActive(false);
        kingdomSelectionPanel.SetActive(true);
        kingdomsListPanel.SetActive(true);

        kingdomSelectionCanvasGroup.alpha = 0f;
        kingdomSelectionCanvasGroup.DOFade(1f, fadeDuration);
    }

    // ---------------------------------------------------------------
    // KINGDOM SELECTION / DETAIL
    // Flag panel slides in from the LEFT, description panel from the RIGHT.
    // ---------------------------------------------------------------
    public void ShowKingdomDetail(KingdomData data, Button clickedButton = null)
    {
        if (isTransitioning) return;
        isTransitioning = true;

        if (detailLogoImage != null) detailLogoImage.sprite = data.kingdomLogo;
        if (detailFlagImage != null) detailFlagImage.sprite = data.flagImage;
        if (detailBackgroundImage != null) detailBackgroundImage.sprite = data.descriptionBackground;
        if (detailNameText != null) detailNameText.text = data.kingdomName;
        if (detailMiddleText != null) detailMiddleText.text = data.descriptionMiddle;
        if (detailBottomText != null) detailBottomText.text = data.descriptionBottom;

        // Hide every other kingdom button, keep only the one that was clicked.
        foreach (var entry in kingdomEntries)
        {
            if (entry.button == null) continue;
            entry.button.gameObject.SetActive(entry.button == clickedButton);
        }

        // Kingdoms panel is hidden right away.
        kingdomsListPanel.SetActive(false);

        kingdomDetailPanel.SetActive(true);

        float flagWidth = kingdomFlagRect.rect.width;
        float descWidth = kingdomDescriptionRect.rect.width;

        // Flag panel starts off-screen to the LEFT, slides in to home.
        kingdomFlagRect.anchoredPosition = kingdomFlagHomePos - new Vector2(flagWidth, 0f);
        kingdomFlagRect
            .DOAnchorPos(kingdomFlagHomePos, slideDuration)
            .SetEase(slideInEase);

        // Description panel starts off-screen to the RIGHT, slides in to home.
        kingdomDescriptionRect.anchoredPosition = kingdomDescriptionHomePos + new Vector2(descWidth, 0f);
        kingdomDescriptionRect
            .DOAnchorPos(kingdomDescriptionHomePos, slideDuration)
            .SetEase(slideInEase)
            .OnComplete(() => isTransitioning = false);
    }

    private void HideKingdomDetail()
    {
        if (isTransitioning) return;
        isTransitioning = true;

        // Bring back every kingdom button for the next visit to the list.
        foreach (var entry in kingdomEntries)
        {
            if (entry.button == null) continue;
            entry.button.gameObject.SetActive(true);
        }

        // Kingdoms panel reappears immediately.
        kingdomsListPanel.SetActive(true);
        kingdomsListRect.anchoredPosition = kingdomsListHomePos;

        float flagWidth = kingdomFlagRect.rect.width;
        float descWidth = kingdomDescriptionRect.rect.width;

        // Flag panel exits back out to the LEFT.
        kingdomFlagRect
            .DOAnchorPos(kingdomFlagHomePos - new Vector2(flagWidth, 0f), slideDuration)
            .SetEase(slideOutEase);

        // Description panel exits back out to the RIGHT, then the whole detail panel deactivates.
        kingdomDescriptionRect
            .DOAnchorPos(kingdomDescriptionHomePos + new Vector2(descWidth, 0f), slideDuration)
            .SetEase(slideOutEase)
            .OnComplete(() =>
            {
                kingdomDetailPanel.SetActive(false);
                isTransitioning = false;
            });
    }

}