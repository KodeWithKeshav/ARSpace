using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using TMPro;
using ARSpace.Core;
using ARSpace.AR;
using ARSpace.Placement;
using ARSpace.Furniture;
using ARSpace.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace ARSpace.Editor.AssetBuilders
{
    /// <summary>
    /// Editor tool: ARSpace → Build AR Workspace Scene (Full Rebuild)
    ///
    /// The scene shipped in the APK was never actually wired to the real ARSpace
    /// gameplay code — it was still the untouched Unity "Mobile AR" template scene
    /// (GoalManager / ARTemplateMenuManager + a handful of debug shape buttons),
    /// which is why the build showed a black screen with a single inert button:
    /// the real ARSessionController / PlaneDetectionManager / ARPlacementManager /
    /// GestureRouter pipeline that lives under Assets/Scripts was never placed in
    /// the scene at all.
    ///
    /// This tool rebuilds Assets/Scenes/ARWorkspace.unity from scratch:
    /// - Keeps the AR Foundation rig Unity's template already configured correctly
    ///   (AR Session, XR Origin with AR Camera Manager/Background, Directional Light)
    ///   and ensures ARPlaneManager/ARRaycastManager/ARAnchorManager are present.
    /// - Removes every leftover template/demo object (SimplePlacementManager, the
    ///   debug shape buttons, the old Canvas tree, the old EventSystem).
    /// - Adds a single "ARSpaceManagers" object hosting the real pipeline.
    /// - Builds a minimal working UI: catalogue drawer, Place/Cancel buttons,
    ///   selection toolbar, and a toast/tracking-guidance banner.
    ///
    /// Run order from a clean checkout:
    ///   1. ARSpace → Apply Android AR Build Settings
    ///   2. ARSpace → Setup Plane Grid Assets
    ///   3. ARSpace → Rebuild Furniture Catalogue
    ///   4. ARSpace → Build AR Workspace Scene (Full Rebuild)   ← this tool
    ///   5. ARSpace → Validate Project
    /// </summary>
    public static class ARWorkspaceSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/ARWorkspace.unity";
        const string PlanePrefabPath = "Assets/Prefabs/Plane.prefab";
        const string OutlineMaterialPath = "Assets/Art/Materials/M_PlaneOutline.mat";
        const string DatabasePath = "Assets/ScriptableObjects/FurnitureDatabase.asset";

        // ── Visual theme ───────────────────────────────────────
        static readonly Color PanelBg = new Color(0.07f, 0.08f, 0.10f, 0.86f);
        static readonly Color PrimaryColor = new Color(1.0f, 0.42f, 0.0f, 1f);
        static readonly Color SecondaryColor = new Color(0.22f, 0.23f, 0.26f, 0.95f);
        static readonly Color DangerColor = new Color(0.80f, 0.24f, 0.20f, 1f);
        static readonly Color TextPrimary = Color.white;
        static readonly Color TextMuted = new Color(0.75f, 0.77f, 0.82f, 1f);

        static Sprite s_RoundedSprite;
        static Sprite RoundedSprite => s_RoundedSprite != null
            ? s_RoundedSprite
            : (s_RoundedSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"));

        static readonly HashSet<string> s_KeepRootNames = new HashSet<string>
        {
            "AR Session",
            "Directional Light",
            "XR Origin (Mobile AR)",
        };

        [MenuItem("ARSpace/Build AR Workspace Scene (Full Rebuild)", priority = 5)]
        public static void BuildScene()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            RemoveLegacyObjects(scene);

            GameObject xrOrigin = GameObject.Find("XR Origin (Mobile AR)");
            GameObject arSessionGo = GameObject.Find("AR Session");

            if (xrOrigin == null || arSessionGo == null)
            {
                Debug.LogError("[ARWorkspaceSceneBuilder] Could not find 'XR Origin (Mobile AR)' or 'AR Session' in the scene. " +
                                "The scene's AR Foundation rig appears to have been removed — aborting so nothing is corrupted.");
                return;
            }

            EnsureArSession(arSessionGo);
            EnsureArFoundationManagers(xrOrigin, out ARPlaneManager planeManager);

            Material outlineMat = AssetDatabase.LoadAssetAtPath<Material>(OutlineMaterialPath);
            if (outlineMat == null)
            {
                Debug.LogWarning("[ARWorkspaceSceneBuilder] M_PlaneOutline.mat not found. " +
                                  "Run 'ARSpace → Setup Plane Grid Assets' first for correctly rendered reticle/selection outlines.");
            }

            GameObject reticleGo = BuildPlacementReticle(outlineMat);
            BuildSelectionVisual(outlineMat);
            BuildManagersHierarchy(reticleGo);
            BuildEventSystem();
            BuildCanvas();
            BuildDebugHud();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[ARWorkspaceSceneBuilder] ✓ ARWorkspace scene rebuilt and saved successfully.");
        }

        // ── Cleanup ────────────────────────────────────────────

        static void RemoveLegacyObjects(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root == null || s_KeepRootNames.Contains(root.name))
                    continue;

                Debug.Log($"[ARWorkspaceSceneBuilder] Removing legacy scene object '{root.name}'.");
                Object.DestroyImmediate(root);
            }
        }

        // ── AR Foundation Rig ──────────────────────────────────

        static void EnsureArSession(GameObject arSessionGo)
        {
            if (arSessionGo.GetComponent<ARSession>() == null)
                arSessionGo.AddComponent<ARSession>();
        }

        static void EnsureArFoundationManagers(GameObject xrOrigin, out ARPlaneManager planeManager)
        {
            planeManager = xrOrigin.GetComponent<ARPlaneManager>();
            if (planeManager == null)
                planeManager = xrOrigin.AddComponent<ARPlaneManager>();

            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;

            GameObject planePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlanePrefabPath);
            if (planePrefab != null)
            {
                planeManager.planePrefab = planePrefab;
            }
            else
            {
                Debug.LogWarning("[ARWorkspaceSceneBuilder] Plane.prefab not found — floor surfaces will be " +
                                  "detected but not visualised. Run 'ARSpace → Setup Plane Grid Assets' first.");
            }

            if (xrOrigin.GetComponent<ARRaycastManager>() == null)
                xrOrigin.AddComponent<ARRaycastManager>();

            if (xrOrigin.GetComponent<ARAnchorManager>() == null)
                xrOrigin.AddComponent<ARAnchorManager>();

            Camera cam = xrOrigin.GetComponentInChildren<Camera>(true);
            if (cam == null)
            {
                Debug.LogError("[ARWorkspaceSceneBuilder] No Camera found under XR Origin — the AR camera rig is broken.");
                return;
            }

            cam.tag = "MainCamera";

            if (cam.GetComponent<ARCameraManager>() == null)
                cam.gameObject.AddComponent<ARCameraManager>();

            if (cam.GetComponent<ARCameraBackground>() == null)
                cam.gameObject.AddComponent<ARCameraBackground>();
        }

        // ── World-space helpers (Reticle / Selection Visual) ──

        static GameObject BuildPlacementReticle(Material outlineMat)
        {
            var go = new GameObject("PlacementReticle");
            var reticle = go.AddComponent<PlacementReticle>();

            LineRenderer ring = CreateLineRendererChild(go.transform, "ReticleRing", outlineMat);
            LineRenderer footprint = CreateLineRendererChild(go.transform, "ReticleFootprint", outlineMat);

            AssignSerializedField(reticle, "m_RingRenderer", ring);
            AssignSerializedField(reticle, "m_FootprintRenderer", footprint);

            // Deliberately no collider here: the reticle is confirmed via the "Place" button
            // (ARPlacementManager.PlaceCurrentItem). A collider on a world-locked reticle would
            // sit at its last raycast position even while hidden and could steal taps intended
            // for placed furniture during selection.

            return go;
        }

        static void BuildSelectionVisual(Material outlineMat)
        {
            var go = new GameObject("SelectionVisual");
            var visual = go.AddComponent<SelectionVisual>();

            LineRenderer inner = CreateLineRendererChild(go.transform, "InnerFootprint", outlineMat);
            LineRenderer outer = CreateLineRendererChild(go.transform, "OuterClearance", outlineMat);

            AssignSerializedField(visual, "m_FootprintRenderer", inner);
            AssignSerializedField(visual, "m_ClearanceRenderer", outer);
        }

        static LineRenderer CreateLineRendererChild(Transform parent, string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            if (mat != null)
                lr.sharedMaterial = mat;
            return lr;
        }

        // ── Managers ───────────────────────────────────────────

        static GameObject BuildManagersHierarchy(GameObject reticleGo)
        {
            var root = new GameObject("ARSpaceManagers");
            root.AddComponent<ARSpaceApp>();

            var sessionCtrl = root.AddComponent<ARSessionController>();
            var arSession = Object.FindFirstObjectByType<ARSession>();
            AssignSerializedField(sessionCtrl, "m_ARSession", arSession);

            root.AddComponent<PlaneDetectionManager>();
            root.AddComponent<TrackingQualityMonitor>();
            root.AddComponent<LightEstimationBinder>();

            var catalog = root.AddComponent<CatalogService>();
            var database = AssetDatabase.LoadAssetAtPath<FurnitureDatabase>(DatabasePath);
            if (database != null)
            {
                AssignSerializedField(catalog, "m_Database", database);
            }
            else
            {
                Debug.LogWarning("[ARWorkspaceSceneBuilder] FurnitureDatabase.asset not found — " +
                                  "run 'ARSpace → Rebuild Furniture Catalogue' first so the catalogue drawer has items.");
            }

            root.AddComponent<AnchorService>();
            root.AddComponent<PlacedObjectRegistry>();
            root.AddComponent<ObjectSelectionService>();
            root.AddComponent<GestureRouter>();
            root.AddComponent<ObjectManipulator>();

            var placementMgr = root.AddComponent<ARPlacementManager>();
            var reticleComponent = reticleGo.GetComponent<PlacementReticle>();
            AssignSerializedField(placementMgr, "m_Reticle", reticleComponent);

            return root;
        }

        static void BuildEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        // ── Canvas / UI ────────────────────────────────────────
        //
        // Bottom-up band layout (heights chosen so nothing overlaps):
        //   Catalogue drawer     : 0   – 190
        //   Selection toolbar    : 190 – 370   (hidden unless an object is selected)
        //   Placement controls   : 370 – 500
        //   Toast (floating)     : 520 – 610   (clear of every band above)
        //   Guidance banner      : docked to the top edge

        const float CatalogHeight = 190f;
        const float ToolbarHeight = 180f;
        const float PlacementHeight = 130f;
        const float ToastY = 520f;

        static void BuildCanvas()
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();
            Transform canvasT = canvasGo.transform;

            // Top guidance banner ("Point your phone at the floor", "Too dark", ...)
            TextMeshProUGUI guidanceText = CreateBand(canvasT, "GuidanceBanner", top: true, y: 0, height: 110,
                out RectTransform guidanceBandRect);
            StyleImage(guidanceBandRect.GetComponent<Image>(), new Color(0f, 0f, 0f, 0.6f), rounded: false);
            guidanceText.fontSize = 32;
            guidanceText.fontStyle = FontStyles.Bold;
            guidanceText.alignment = TextAlignmentOptions.Center;
            guidanceText.color = TextPrimary;

            // Floating toast (short-lived confirmations / warnings) — sits clear above every bottom band
            var toastGo = new GameObject("ToastPanel", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            var toastRect = (RectTransform)toastGo.transform;
            toastRect.SetParent(canvasT, false);
            toastRect.anchorMin = new Vector2(0.5f, 0f);
            toastRect.anchorMax = new Vector2(0.5f, 0f);
            toastRect.pivot = new Vector2(0.5f, 0f);
            toastRect.sizeDelta = new Vector2(920, 96);
            toastRect.anchoredPosition = new Vector2(0, ToastY);
            StyleImage(toastGo.GetComponent<Image>(), new Color(0.05f, 0.05f, 0.06f, 0.92f), rounded: true);
            AddShadow(toastGo);
            var toastGroup = toastGo.GetComponent<CanvasGroup>();
            toastGroup.alpha = 0f;
            TextMeshProUGUI toastText = CreateChildText(toastRect, "ToastText", 28, TextAlignmentOptions.Center, TextPrimary);
            StretchFull(toastText.rectTransform, 24, 8);

            // Bottom bands, stacked bottom-up: catalogue / selection toolbar / place-cancel
            BuildCatalogDrawer(canvasT, bottomY: 0, height: CatalogHeight);
            BuildSelectionToolbar(canvasT, bottomY: CatalogHeight, height: ToolbarHeight);
            BuildPlacementControls(canvasT, bottomY: CatalogHeight + ToolbarHeight, height: PlacementHeight);

            // Toast presenter wiring
            var toastPresenterGo = new GameObject("ToastPresenter");
            toastPresenterGo.transform.SetParent(canvasT, false);
            var toastPresenter = toastPresenterGo.AddComponent<ToastPresenter>();
            AssignSerializedField(toastPresenter, "m_ToastGroup", toastGroup);
            AssignSerializedField(toastPresenter, "m_ToastText", toastText);
            AssignSerializedField(toastPresenter, "m_GuidanceText", guidanceText);
        }

        static void BuildDebugHud()
        {
            var canvasGo = new GameObject("DebugHudCanvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var panelGo = new GameObject("DebugHud", typeof(RectTransform), typeof(Image), typeof(DebugHud));
            var rect = (RectTransform)panelGo.transform;
            rect.SetParent(canvasGo.transform, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0, 56);
            rect.anchoredPosition = new Vector2(0, -120);
            StyleImage(panelGo.GetComponent<Image>(), new Color(0f, 0f, 0f, 0.5f), rounded: false);

            TextMeshProUGUI text = CreateChildText(rect, "Text", 20, TextAlignmentOptions.Center, new Color(0.6f, 1f, 0.6f, 1f));
            StretchFull(text.rectTransform, 12, 4);

            var hud = panelGo.GetComponent<DebugHud>();
            AssignSerializedField(hud, "m_Text", text);
        }

        static void BuildCatalogDrawer(Transform canvasT, float bottomY, float height)
        {
            var panelGo = new GameObject("CatalogDrawer", typeof(RectTransform), typeof(Image), typeof(CatalogPanel));
            RectTransform panelRect = ConfigureBottomBand(panelGo, canvasT, bottomY, height);
            StyleImage(panelGo.GetComponent<Image>(), PanelBg, rounded: false);

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            var viewportRect = (RectTransform)viewportGo.transform;
            viewportRect.SetParent(panelRect, false);
            StretchFull(viewportRect, 0, 0);
            viewportGo.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);
            viewportGo.GetComponent<Mask>().showMaskGraphic = false;

            var scrollRectGo = panelGo.AddComponent<ScrollRect>();
            scrollRectGo.horizontal = true;
            scrollRectGo.vertical = false;
            scrollRectGo.movementType = ScrollRect.MovementType.Clamped;
            scrollRectGo.viewport = viewportRect;

            var contentGo = new GameObject("Content", typeof(RectTransform));
            var contentRect = (RectTransform)contentGo.transform;
            contentRect.SetParent(viewportRect, false);
            contentRect.anchorMin = new Vector2(0f, 0f);
            contentRect.anchorMax = new Vector2(0f, 1f);
            contentRect.pivot = new Vector2(0f, 0.5f);
            contentRect.anchoredPosition = Vector2.zero;

            var layout = contentGo.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 20, 20);
            layout.spacing = 18;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;

            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRectGo.content = contentRect;

            var catalogPanel = panelGo.GetComponent<CatalogPanel>();
            AssignSerializedField(catalogPanel, "m_Content", contentRect);
            AssignSerializedField(catalogPanel, "m_CardSprite", RoundedSprite);
        }

        static void BuildSelectionToolbar(Transform canvasT, float bottomY, float height)
        {
            var panelGo = new GameObject("SelectionToolbar", typeof(RectTransform), typeof(Image), typeof(SelectionToolbar));
            RectTransform panelRect = ConfigureBottomBand(panelGo, canvasT, bottomY, height);
            StyleImage(panelGo.GetComponent<Image>(), PanelBg, rounded: false);

            // Info row (top half)
            var infoGo = new GameObject("InfoRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var infoRect = (RectTransform)infoGo.transform;
            infoRect.SetParent(panelRect, false);
            infoRect.anchorMin = new Vector2(0f, 0.55f);
            infoRect.anchorMax = new Vector2(1f, 1f);
            infoRect.offsetMin = new Vector2(28, 0);
            infoRect.offsetMax = new Vector2(-28, 0);
            var infoLayout = infoGo.GetComponent<HorizontalLayoutGroup>();
            infoLayout.childForceExpandWidth = true;
            infoLayout.childForceExpandHeight = true;
            infoLayout.childAlignment = TextAnchor.MiddleCenter;

            TextMeshProUGUI itemName = CreateChildText(infoRect, "ItemNameText", 25, TextAlignmentOptions.Left, TextPrimary);
            itemName.fontStyle = FontStyles.Bold;
            TextMeshProUGUI dims = CreateChildText(infoRect, "DimensionsText", 21, TextAlignmentOptions.Center, TextMuted);
            TextMeshProUGUI seats = CreateChildText(infoRect, "SeatCountText", 21, TextAlignmentOptions.Right, TextMuted);

            // Button row (bottom half)
            var buttonsGo = new GameObject("ButtonsRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var buttonsRect = (RectTransform)buttonsGo.transform;
            buttonsRect.SetParent(panelRect, false);
            buttonsRect.anchorMin = new Vector2(0f, 0f);
            buttonsRect.anchorMax = new Vector2(1f, 0.55f);
            buttonsRect.offsetMin = new Vector2(24, 10);
            buttonsRect.offsetMax = new Vector2(-24, -6);
            var buttonsLayout = buttonsGo.GetComponent<HorizontalLayoutGroup>();
            buttonsLayout.spacing = 14;
            buttonsLayout.childForceExpandWidth = true;
            buttonsLayout.childForceExpandHeight = true;
            buttonsLayout.childAlignment = TextAnchor.MiddleCenter;

            Button duplicateBtn = CreateChildButton(buttonsRect, "DuplicateButton", "Duplicate", SecondaryColor);
            Button rotateBtn = CreateChildButton(buttonsRect, "RotateButton", "Rotate 90°", SecondaryColor);
            Button resetScaleBtn = CreateChildButton(buttonsRect, "ResetScaleButton", "Reset", SecondaryColor);
            Button lockBtn = CreateChildButton(buttonsRect, "LockButton", "Lock", SecondaryColor);
            TextMeshProUGUI lockText = lockBtn.GetComponentInChildren<TextMeshProUGUI>();
            Button deleteBtn = CreateChildButton(buttonsRect, "DeleteButton", "Delete", DangerColor);

            var toolbar = panelGo.GetComponent<SelectionToolbar>();
            AssignSerializedField(toolbar, "m_ToolbarPanel", panelGo);
            AssignSerializedField(toolbar, "m_ItemNameText", itemName);
            AssignSerializedField(toolbar, "m_DimensionsText", dims);
            AssignSerializedField(toolbar, "m_SeatCountText", seats);
            AssignSerializedField(toolbar, "m_DuplicateButton", duplicateBtn);
            AssignSerializedField(toolbar, "m_DeleteButton", deleteBtn);
            AssignSerializedField(toolbar, "m_RotateButton", rotateBtn);
            AssignSerializedField(toolbar, "m_ResetScaleButton", resetScaleBtn);
            AssignSerializedField(toolbar, "m_LockButton", lockBtn);
            AssignSerializedField(toolbar, "m_LockStatusText", lockText);
        }

        static void BuildPlacementControls(Transform canvasT, float bottomY, float height)
        {
            var panelGo = new GameObject("PlacementControls", typeof(RectTransform), typeof(PlacementControls));
            RectTransform panelRect = ConfigureBottomBand(panelGo, canvasT, bottomY, height);

            var layoutGo = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var layoutRect = (RectTransform)layoutGo.transform;
            layoutRect.SetParent(panelRect, false);
            StretchFull(layoutRect, 0, 0);
            layoutRect.offsetMin = new Vector2(36, 18);
            layoutRect.offsetMax = new Vector2(-36, -18);
            var layout = layoutGo.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 22;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleCenter;

            Button cancelBtn = CreateChildButton(layoutRect, "CancelButton", "Cancel", SecondaryColor);

            Button placeBtn = CreateChildButton(layoutRect, "PlaceButton", "Place", PrimaryColor);
            var placeLabel = placeBtn.GetComponentInChildren<TextMeshProUGUI>();
            placeLabel.fontSize = 30;
            placeLabel.fontStyle = FontStyles.Bold;

            var controls = panelGo.GetComponent<PlacementControls>();
            AssignSerializedField(controls, "m_PlaceButton", placeBtn);
            AssignSerializedField(controls, "m_CancelButton", cancelBtn);
        }

        // ── Generic UI construction helpers ───────────────────

        static RectTransform ConfigureBottomBand(GameObject go, Transform canvasT, float bottomY, float height)
        {
            var rect = (RectTransform)go.transform;
            rect.SetParent(canvasT, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(0, height);
            rect.anchoredPosition = new Vector2(0, bottomY);
            return rect;
        }

        static TextMeshProUGUI CreateBand(Transform canvasT, string name, bool top, float y, float height, out RectTransform bandRect)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            bandRect = (RectTransform)go.transform;
            bandRect.SetParent(canvasT, false);

            if (top)
            {
                bandRect.anchorMin = new Vector2(0f, 1f);
                bandRect.anchorMax = new Vector2(1f, 1f);
                bandRect.pivot = new Vector2(0.5f, 1f);
                bandRect.anchoredPosition = new Vector2(0, -y);
            }
            else
            {
                bandRect.anchorMin = new Vector2(0f, 0f);
                bandRect.anchorMax = new Vector2(1f, 0f);
                bandRect.pivot = new Vector2(0.5f, 0f);
                bandRect.anchoredPosition = new Vector2(0, y);
            }
            bandRect.sizeDelta = new Vector2(0, height);

            TextMeshProUGUI text = CreateChildText(bandRect, name + "_Text", 30, TextAlignmentOptions.Center, Color.white);
            StretchFull(text.rectTransform, 24, 8);
            return text;
        }

        static TextMeshProUGUI CreateChildText(Transform parent, string name, float fontSize, TextAlignmentOptions alignment, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.text = string.Empty;
            return text;
        }

        /// <summary>Creates a rounded, flat-color, drop-shadowed button — the shared style for every action button.</summary>
        static Button CreateChildButton(Transform parent, string name, string label, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            StyleImage(go.GetComponent<Image>(), color, rounded: true);
            AddShadow(go);

            TextMeshProUGUI text = CreateChildText(rect, "Label", 24, TextAlignmentOptions.Center, TextPrimary);
            text.text = label;
            text.fontStyle = FontStyles.Bold;
            StretchFull(text.rectTransform, 10, 4);

            return go.GetComponent<Button>();
        }

        /// <summary>Applies the shared rounded-corner sprite (falls back to a flat rect if unavailable) and a color tint.</summary>
        static void StyleImage(Image image, Color color, bool rounded)
        {
            image.color = color;
            if (rounded && RoundedSprite != null)
            {
                image.sprite = RoundedSprite;
                image.type = Image.Type.Sliced;
            }
        }

        static void AddShadow(GameObject go)
        {
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
            shadow.effectDistance = new Vector2(0f, -3f);
        }

        static void StretchFull(RectTransform rect, float horizontalPadding, float verticalPadding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(horizontalPadding, verticalPadding);
            rect.offsetMax = new Vector2(-horizontalPadding, -verticalPadding);
        }

        static void AssignSerializedField(Object target, string fieldName, Object value)
        {
            if (target == null)
                return;

            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogError($"[ARWorkspaceSceneBuilder] Field '{fieldName}' not found on {target.GetType().Name}.");
                return;
            }

            prop.objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }
    }
}
