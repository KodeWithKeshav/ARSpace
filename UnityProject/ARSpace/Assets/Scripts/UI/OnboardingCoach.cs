using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ARSpace.Core;

namespace ARSpace.UI
{
    public enum OnboardingStep
    {
        ScanningFloor,
        PlaceFurniture,
        ManipulateAndInspect,
        Completed
    }

    /// <summary>
    /// Contextual AR onboarding coach guiding the user step-by-step through:
    /// 1. Floor scanning and surface detection
    /// 2. First furniture placement
    /// 3. Object selection, rotation, and CRE inspection
    /// Auto-advances based on live application events and auto-minimizes once complete.
    /// </summary>
    public class OnboardingCoach : MonoBehaviour
    {
        [Header("UI Root Elements")]
        [SerializeField]
        GameObject m_CoachCardRoot;

        [SerializeField]
        TextMeshProUGUI m_StepIndicatorText;

        [SerializeField]
        TextMeshProUGUI m_TitleText;

        [SerializeField]
        TextMeshProUGUI m_InstructionsText;

        [SerializeField]
        Button m_DismissButton;

        [Header("Configuration")]
        [Tooltip("Minimum detected floor area in m² required to advance past floor mapping.")]
        [SerializeField]
        float m_MinAreaToAdvance = 1.5f;

        [SerializeField]
        bool m_AlwaysShowOnStartup = false;

        const string OnboardingPrefKey = "ARSpace_OnboardingDone";
        OnboardingStep m_CurrentStep = OnboardingStep.ScanningFloor;
        bool m_IsDismissed = false;
        float m_CompletedTimer = 0f;

        public OnboardingStep CurrentStep => m_CurrentStep;

        void Awake()
        {
            if (m_DismissButton != null)
            {
                m_DismissButton.onClick.AddListener(Dismiss);
            }
        }

        void OnEnable()
        {
            GameEvents.FloorAreaUpdated += OnFloorAreaUpdated;
            GameEvents.ObjectPlaced += OnObjectPlaced;
            GameEvents.ObjectSelectionChanged += OnSelectionChanged;
            GameEvents.PresentationModeToggled += OnPresentationModeToggled;
        }

        void OnDisable()
        {
            GameEvents.FloorAreaUpdated -= OnFloorAreaUpdated;
            GameEvents.ObjectPlaced -= OnObjectPlaced;
            GameEvents.ObjectSelectionChanged -= OnSelectionChanged;
            GameEvents.PresentationModeToggled -= OnPresentationModeToggled;
        }

        void Start()
        {
            if (!m_AlwaysShowOnStartup && PlayerPrefs.GetInt(OnboardingPrefKey, 0) == 1)
            {
                m_IsDismissed = true;
                if (m_CoachCardRoot != null)
                    m_CoachCardRoot.SetActive(false);
                return;
            }

            SetStep(OnboardingStep.ScanningFloor);
        }

        void Update()
        {
            if (m_CurrentStep == OnboardingStep.Completed && !m_IsDismissed)
            {
                m_CompletedTimer += Time.deltaTime;
                if (m_CompletedTimer >= 4.0f)
                {
                    Dismiss();
                }
            }
        }

        public void SetStep(OnboardingStep step)
        {
            if (m_IsDismissed) return;

            m_CurrentStep = step;
            if (m_CoachCardRoot != null)
            {
                m_CoachCardRoot.SetActive(true);
            }

            switch (step)
            {
                case OnboardingStep.ScanningFloor:
                    UpdateUI("1 / 3", "Map Your Workspace", "Slowly pan your phone over the bare floor to detect surfaces.");
                    break;

                case OnboardingStep.PlaceFurniture:
                    UpdateUI("2 / 3", "Place Furniture", "Open the Catalogue drawer below, choose a desk or cubicle, and tap Place.");
                    break;

                case OnboardingStep.ManipulateAndInspect:
                    UpdateUI("3 / 3", "Select & Customize", "Tap placed furniture to drag on floor, rotate with two fingers, or inspect CRE specs.");
                    break;

                case OnboardingStep.Completed:
                    UpdateUI("Ready!", "Workspace Ready", "Design freely! Use Layout Menu to save arrangements, or Analytics for CRE density.");
                    m_CompletedTimer = 0f;
                    PlayerPrefs.SetInt(OnboardingPrefKey, 1);
                    PlayerPrefs.Save();
                    break;
            }
        }

        void UpdateUI(string stepNum, string title, string instructions)
        {
            if (m_StepIndicatorText != null) m_StepIndicatorText.text = stepNum;
            if (m_TitleText != null) m_TitleText.text = title;
            if (m_InstructionsText != null) m_InstructionsText.text = instructions;
        }

        public void Dismiss()
        {
            m_IsDismissed = true;
            if (m_CoachCardRoot != null)
            {
                m_CoachCardRoot.SetActive(false);
            }
            PlayerPrefs.SetInt(OnboardingPrefKey, 1);
            PlayerPrefs.Save();
        }

        public void RestartTutorial()
        {
            m_IsDismissed = false;
            PlayerPrefs.DeleteKey(OnboardingPrefKey);
            SetStep(OnboardingStep.ScanningFloor);
        }

        void OnFloorAreaUpdated(float area)
        {
            if (m_CurrentStep == OnboardingStep.ScanningFloor && area >= m_MinAreaToAdvance)
            {
                SetStep(OnboardingStep.PlaceFurniture);
            }
        }

        void OnObjectPlaced(GameObject go)
        {
            if (m_CurrentStep == OnboardingStep.PlaceFurniture)
            {
                SetStep(OnboardingStep.ManipulateAndInspect);
            }
        }

        void OnSelectionChanged(GameObject selected)
        {
            if (m_CurrentStep == OnboardingStep.ManipulateAndInspect && selected != null)
            {
                SetStep(OnboardingStep.Completed);
            }
        }

        void OnPresentationModeToggled(bool presenting)
        {
            if (m_CoachCardRoot != null)
            {
                m_CoachCardRoot.SetActive(!presenting && !m_IsDismissed);
            }
        }
    }
}
