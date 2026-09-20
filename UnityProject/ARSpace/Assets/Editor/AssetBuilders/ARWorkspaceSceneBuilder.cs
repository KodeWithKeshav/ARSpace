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

            UiAssetBuilder.EnsureContactShadowMaterial();
            Material lineMat = UiAssetBuilder.EnsureReticleLineMaterial();
            if (lineMat != null)
                outlineMat = lineMat;

            GameObject reticleGo = BuildPlacementReticle(outlineMat);
            BuildSelectionVisual(outlineMat);
            BuildManagersHierarchy(reticleGo);
            BuildEventSystem();
            s_LineMaterial = outlineMat;
            BuildCanvas();

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

#if ENABLE_INPUT_SYSTEM
            // The template binds the pose driver's tracking-state input to an XR headset device that does not exist
            // on a phone. Ignore that input so the camera always follows the AR device pose.
            var poseDriver = cam.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            if (poseDriver != null)
                AssignSerializedBool(poseDriver, "m_IgnoreTrackingState", true);
#endif

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
            root.AddComponent<PlaneVisibilityController>();
            root.AddComponent<ManualFloor>();
            root.AddComponent<UndoService>();
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
            var eventSystem = go.AddComponent<EventSystem>();
            eventSystem.pixelDragThreshold = 24; // forgiving on high-density phone screens so card rows scroll instead of mis-tapping
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        // ── Canvas / UI ────────────────────────────────────────
        //
        // Layout (reference 1080x1920, all inside the device safe area):
        //   top      : hint pill (what to do next)
        //   bottom   : catalogue sheet (title, category chips, item cards)
        //   floating : selection card OR place/cancel bar, just above the sheet; toast above those

        const float SheetHeight = 440f;
        const float SheetHiddenExtra = 80f;   // sheet is drawn this far below the screen so its bottom corners are never visible
        const float FloatingGap = 18f;
        const float ActionBarHeight = 104f;
        const float SelectionCardHeight = 310f;
        const float MoveCardHeight = 224f;
        static Material s_LineMaterial;
        const float SideMargin = 32f;

        static Sprite s_Rounded;

        static void BuildCanvas()
        {
            s_Rounded = UiAssetBuilder.EnsureRoundedSprite();

            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            var safeGo = new GameObject("SafeArea", typeof(RectTransform), typeof(SafeAreaFitter));
            var safeRect = (RectTransform)safeGo.transform;
            safeRect.SetParent(canvasGo.transform, false);
            safeRect.anchorMin = Vector2.zero;
            safeRect.anchorMax = Vector2.one;
            safeRect.offsetMin = Vector2.zero;
            safeRect.offsetMax = Vector2.zero;
            Transform safe = safeRect;

            BuildTopHint(safe);
            BuildToast(safe);
            BuildCatalogSheet(safe);
            BuildSelectionCard(safe);
            BuildPlacementBar(safe);
            BuildFloorAdjust(safe);
            BuildMoveCard(safe);
            BuildStatusRow(safe);
        }

        static void BuildTopHint(Transform parent)
        {
            var go = new GameObject("HintPill", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(Button), typeof(CoachHints));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(980, 88);
            rect.anchoredPosition = new Vector2(0, -24);

            var image = go.GetComponent<Image>();
            UiStyle.Round(image, s_Rounded, 44f);
            image.color = new Color(0.05f, 0.06f, 0.08f, 0.72f);
            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            var group = go.GetComponent<CanvasGroup>();
            group.alpha = 0f;

            TextMeshProUGUI text = CreateText(rect, "Text", 28, FontStyles.Bold, TextAlignmentOptions.Center, UiStyle.TextPrimary);
            Stretch(text.rectTransform, 36, 8);
            text.enableAutoSizing = true;
            text.fontSizeMin = 20;
            text.fontSizeMax = 28;
            text.raycastTarget = false;

            // Diagnostic overlay (hidden until the hint pill is tapped five times).
            var hudGo = new GameObject("DebugHud", typeof(RectTransform), typeof(Image), typeof(DebugHud));
            var hudRect = (RectTransform)hudGo.transform;
            hudRect.SetParent(parent, false);
            hudRect.anchorMin = new Vector2(0.5f, 1f);
            hudRect.anchorMax = new Vector2(0.5f, 1f);
            hudRect.pivot = new Vector2(0.5f, 1f);
            hudRect.sizeDelta = new Vector2(900, 112);
            hudRect.anchoredPosition = new Vector2(0, -206);
            var hudImage = hudGo.GetComponent<Image>();
            UiStyle.Round(hudImage, s_Rounded, 26f);
            hudImage.color = new Color(0f, 0f, 0f, 0.6f);
            hudImage.raycastTarget = false;
            TextMeshProUGUI hudText = CreateText(hudRect, "Text", 20, FontStyles.Normal, TextAlignmentOptions.Center, new Color(0.6f, 1f, 0.6f, 1f));
            Stretch(hudText.rectTransform, 14, 4);
            hudText.raycastTarget = false;
            AssignSerializedField(hudGo.GetComponent<DebugHud>(), "m_Text", hudText);

            var hints = go.GetComponent<CoachHints>();
            AssignSerializedField(hints, "m_Group", group);
            AssignSerializedField(hints, "m_Text", text);
            AssignSerializedField(hints, "m_SecretToggle", button);
            AssignSerializedField(hints, "m_DebugHudRoot", hudGo);
        }

        static void BuildToast(Transform parent)
        {
            var go = new GameObject("ToastPanel", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(900, 88);
            rect.anchoredPosition = new Vector2(0, SheetHeight + FloatingGap + SelectionCardHeight + FloatingGap + MoveCardHeight + FloatingGap);

            var image = go.GetComponent<Image>();
            UiStyle.Round(image, s_Rounded, 44f);
            image.color = new Color(0.05f, 0.06f, 0.08f, 0.94f);
            image.raycastTarget = false;
            AddShadow(go);

            var group = go.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            TextMeshProUGUI text = CreateText(rect, "ToastText", 27, FontStyles.Bold, TextAlignmentOptions.Center, UiStyle.TextPrimary);
            Stretch(text.rectTransform, 32, 8);
            text.enableAutoSizing = true;
            text.fontSizeMin = 20;
            text.fontSizeMax = 27;
            text.raycastTarget = false;

            var presenterGo = new GameObject("ToastPresenter");
            presenterGo.transform.SetParent(parent, false);
            var presenter = presenterGo.AddComponent<ToastPresenter>();
            AssignSerializedField(presenter, "m_ToastGroup", group);
            AssignSerializedField(presenter, "m_ToastText", text);
        }

        static void BuildCatalogSheet(Transform parent)
        {
            var go = new GameObject("CatalogSheet", typeof(RectTransform), typeof(Image), typeof(CatalogPanel));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(0, SheetHeight + SheetHiddenExtra);
            rect.anchoredPosition = new Vector2(0, -SheetHiddenExtra);

            var image = go.GetComponent<Image>();
            UiStyle.Round(image, s_Rounded, 44f);
            image.color = UiStyle.Panel;
            AddShadow(go, new Vector2(0f, 6f));

            // Grabber
            var grab = new GameObject("Grabber", typeof(RectTransform), typeof(Image));
            var grabRect = (RectTransform)grab.transform;
            grabRect.SetParent(rect, false);
            grabRect.anchorMin = new Vector2(0.5f, 1f);
            grabRect.anchorMax = new Vector2(0.5f, 1f);
            grabRect.pivot = new Vector2(0.5f, 1f);
            grabRect.sizeDelta = new Vector2(84, 8);
            grabRect.anchoredPosition = new Vector2(0, -14);
            var grabImage = grab.GetComponent<Image>();
            UiStyle.Round(grabImage, s_Rounded, 4f);
            grabImage.color = new Color(1f, 1f, 1f, 0.28f);
            grabImage.raycastTarget = false;

            // Title + count
            TextMeshProUGUI title = CreateText(rect, "Title", 40, FontStyles.Bold, TextAlignmentOptions.Left, UiStyle.TextPrimary);
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(0f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.sizeDelta = new Vector2(520, 52);
            titleRect.anchoredPosition = new Vector2(SideMargin + 8, -32);
            title.text = "Catalogue";
            title.raycastTarget = false;

            TextMeshProUGUI count = CreateText(rect, "Count", 26, FontStyles.Normal, TextAlignmentOptions.Right, UiStyle.TextMuted);
            var countRect = count.rectTransform;
            countRect.anchorMin = new Vector2(1f, 1f);
            countRect.anchorMax = new Vector2(1f, 1f);
            countRect.pivot = new Vector2(1f, 1f);
            countRect.sizeDelta = new Vector2(320, 40);
            countRect.anchoredPosition = new Vector2(-(SideMargin + 8), -40);
            count.raycastTarget = false;

            // Category chips (horizontal scroller)
            RectTransform chipContent = CreateHorizontalScroller(rect, "Chips", 64f, -100f, 14f, new RectOffset((int)SideMargin, (int)SideMargin, 2, 2), out ScrollRect chipScroll);

            // Item cards (horizontal scroller)
            RectTransform cardContent = CreateHorizontalScroller(rect, "Cards", 250f, -176f, 18f, new RectOffset((int)SideMargin, (int)SideMargin, 8, 8), out ScrollRect cardScroll);

            var panel = go.GetComponent<CatalogPanel>();
            AssignSerializedField(panel, "m_Content", cardContent);
            AssignSerializedField(panel, "m_ChipContent", chipContent);
            AssignSerializedField(panel, "m_CardScroll", cardScroll);
            AssignSerializedField(panel, "m_CountText", count);
            AssignSerializedField(panel, "m_RoundedSprite", s_Rounded);
        }

        static RectTransform CreateHorizontalScroller(Transform parent, string name, float height, float topY, float spacing, RectOffset padding, out ScrollRect scroll)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(ScrollRect));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0, height);
            rect.anchoredPosition = new Vector2(0, topY);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.SetParent(rect, false);
            Stretch(viewportRect, 0, 0);
            viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.004f); // invisible, but receives drags

            var content = new GameObject("Content", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            var contentRect = (RectTransform)content.transform;
            contentRect.SetParent(viewportRect, false);
            contentRect.anchorMin = new Vector2(0f, 0f);
            contentRect.anchorMax = new Vector2(0f, 1f);
            contentRect.pivot = new Vector2(0f, 0.5f);
            contentRect.anchoredPosition = Vector2.zero;

            var layout = content.GetComponent<HorizontalLayoutGroup>();
            layout.padding = padding;
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleLeft;

            content.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll = go.GetComponent<ScrollRect>();
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 30f;
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            return contentRect;
        }

        static void BuildSelectionCard(Transform parent)
        {
            // The script lives on an always-active holder: SelectionToolbar hides its panel by deactivating it,
            // and an inactive object can no longer receive the selection events that re-show it.
            var holder = new GameObject("SelectionToolbar", typeof(RectTransform), typeof(SelectionToolbar));
            var holderRect = (RectTransform)holder.transform;
            holderRect.SetParent(parent, false);
            Stretch(holderRect, 0, 0);

            var go = new GameObject("Card", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(holderRect, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(SideMargin, SheetHeight + FloatingGap);
            rect.offsetMax = new Vector2(-SideMargin, SheetHeight + FloatingGap + SelectionCardHeight);

            var image = go.GetComponent<Image>();
            UiStyle.Round(image, s_Rounded, 40f);
            image.color = UiStyle.Panel;
            AddShadow(go);

            // Header: name / dimensions / seats
            var infoGo = new GameObject("InfoRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var infoRect = (RectTransform)infoGo.transform;
            infoRect.SetParent(rect, false);
            infoRect.anchorMin = new Vector2(0f, 1f);
            infoRect.anchorMax = new Vector2(1f, 1f);
            infoRect.pivot = new Vector2(0.5f, 1f);
            infoRect.offsetMin = new Vector2(32, -80);
            infoRect.offsetMax = new Vector2(-32, -20);
            var infoLayout = infoGo.GetComponent<HorizontalLayoutGroup>();
            infoLayout.childControlWidth = true;
            infoLayout.childControlHeight = true;
            infoLayout.childForceExpandWidth = true;
            infoLayout.childForceExpandHeight = true;
            infoLayout.childAlignment = TextAnchor.MiddleCenter;

            TextMeshProUGUI itemName = CreateText(infoRect, "ItemNameText", 30, FontStyles.Bold, TextAlignmentOptions.Left, UiStyle.TextPrimary);
            itemName.enableWordWrapping = false;
            itemName.overflowMode = TextOverflowModes.Ellipsis;
            TextMeshProUGUI dims = CreateText(infoRect, "DimensionsText", 22, FontStyles.Normal, TextAlignmentOptions.Center, UiStyle.TextMuted);
            TextMeshProUGUI seats = CreateText(infoRect, "SeatCountText", 22, FontStyles.Normal, TextAlignmentOptions.Right, UiStyle.TextMuted);
            itemName.raycastTarget = false; dims.raycastTarget = false; seats.raycastTarget = false;

            // Actions
            var buttonsGo = new GameObject("ButtonsRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var buttonsRect = (RectTransform)buttonsGo.transform;
            buttonsRect.SetParent(rect, false);
            buttonsRect.anchorMin = new Vector2(0f, 0f);
            buttonsRect.anchorMax = new Vector2(1f, 0f);
            buttonsRect.pivot = new Vector2(0.5f, 0f);
            buttonsRect.offsetMin = new Vector2(24, 122);
            buttonsRect.offsetMax = new Vector2(-24, 206);
            var buttonsLayout = buttonsGo.GetComponent<HorizontalLayoutGroup>();
            buttonsLayout.spacing = 12;
            buttonsLayout.childControlWidth = true;
            buttonsLayout.childControlHeight = true;
            buttonsLayout.childForceExpandWidth = true;
            buttonsLayout.childForceExpandHeight = true;
            buttonsLayout.childAlignment = TextAnchor.MiddleCenter;

            Button duplicateBtn = CreateButton(buttonsRect, "DuplicateButton", "Duplicate", UiStyle.Secondary, 24f, 23);
            Button rotateBtn = CreateButton(buttonsRect, "RotateButton", "Rotate", UiStyle.Secondary, 24f, 23);
            Button resetScaleBtn = CreateButton(buttonsRect, "ResetScaleButton", "Reset", UiStyle.Secondary, 24f, 23);
            Button lockBtn = CreateButton(buttonsRect, "LockButton", "Lock", UiStyle.Secondary, 24f, 23);
            TextMeshProUGUI lockText = lockBtn.GetComponentInChildren<TextMeshProUGUI>();
            Button deleteBtn = CreateButton(buttonsRect, "DeleteButton", "Delete", UiStyle.Danger, 24f, 23);

            // Height row: press and hold to lift the object up or lower it back down.
            var heightGo = new GameObject("HeightRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var heightRect = (RectTransform)heightGo.transform;
            heightRect.SetParent(rect, false);
            heightRect.anchorMin = new Vector2(0f, 0f);
            heightRect.anchorMax = new Vector2(1f, 0f);
            heightRect.pivot = new Vector2(0.5f, 0f);
            heightRect.offsetMin = new Vector2(24, 22);
            heightRect.offsetMax = new Vector2(-24, 106);
            var heightLayout = heightGo.GetComponent<HorizontalLayoutGroup>();
            heightLayout.spacing = 12;
            heightLayout.childControlWidth = true;
            heightLayout.childControlHeight = true;
            heightLayout.childForceExpandWidth = true;
            heightLayout.childForceExpandHeight = true;

            HoldButton upBtn = CreateHoldButton(heightRect, "UpButton", "Hold to move UP");
            HoldButton downBtn = CreateHoldButton(heightRect, "DownButton", "Hold to move DOWN");
            foreach (var hb in new[] { upBtn, downBtn })
            {
                var le = hb.GetComponent<LayoutElement>();
                le.preferredWidth = -1;
                le.flexibleWidth = 1;
            }

            var toolbar = holder.GetComponent<SelectionToolbar>();
            AssignSerializedField(toolbar, "m_UpButton", upBtn);
            AssignSerializedField(toolbar, "m_DownButton", downBtn);
            AssignSerializedField(toolbar, "m_ToolbarPanel", go);
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

        static void BuildPlacementBar(Transform parent)
        {
            // The script lives on a stretch-all holder so it keeps receiving events while the visible bar is hidden.
            var holder = new GameObject("PlacementControls", typeof(RectTransform), typeof(PlacementControls));
            var holderRect = (RectTransform)holder.transform;
            holderRect.SetParent(parent, false);
            Stretch(holderRect, 0, 0);

            var bar = new GameObject("Bar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var barRect = (RectTransform)bar.transform;
            barRect.SetParent(holderRect, false);
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(1f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.offsetMin = new Vector2(SideMargin, SheetHeight + FloatingGap);
            barRect.offsetMax = new Vector2(-SideMargin, SheetHeight + FloatingGap + ActionBarHeight);

            var layout = bar.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 20;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleCenter;

            Button cancelBtn = CreateButton(barRect, "CancelButton", "Cancel", UiStyle.Secondary, 52f, 30);
            var cancelLayout = cancelBtn.gameObject.AddComponent<LayoutElement>();
            cancelLayout.preferredWidth = 290;
            cancelLayout.flexibleWidth = 0;

            Button placeBtn = CreateButton(barRect, "PlaceButton", "Place here", UiStyle.Primary, 52f, 32);
            var placeLayout = placeBtn.gameObject.AddComponent<LayoutElement>();
            placeLayout.flexibleWidth = 1;
            TextMeshProUGUI placeLabel = placeBtn.GetComponentInChildren<TextMeshProUGUI>();
            placeLabel.enableWordWrapping = false;
            placeLabel.overflowMode = TextOverflowModes.Ellipsis;

            var controls = holder.GetComponent<PlacementControls>();
            AssignSerializedField(controls, "m_BarRoot", bar);
            AssignSerializedField(controls, "m_PlaceButton", placeBtn);
            AssignSerializedField(controls, "m_CancelButton", cancelBtn);
            AssignSerializedField(controls, "m_PlaceLabel", placeLabel);
        }

        static void BuildFloorAdjust(Transform parent)
        {
            const float rowHeight = 84f;
            float rowBottom = SheetHeight + FloatingGap + ActionBarHeight + 14f;

            // Script on an always-active holder; only the row is shown/hidden.
            var holder = new GameObject("FloorAdjust", typeof(RectTransform), typeof(FloorAdjustControls));
            var holderRect = (RectTransform)holder.transform;
            holderRect.SetParent(parent, false);
            Stretch(holderRect, 0, 0);

            var row = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            var rowRect = (RectTransform)row.transform;
            rowRect.SetParent(holderRect, false);
            rowRect.anchorMin = new Vector2(0f, 0f);
            rowRect.anchorMax = new Vector2(1f, 0f);
            rowRect.pivot = new Vector2(0.5f, 0f);
            rowRect.offsetMin = new Vector2(SideMargin, rowBottom);
            rowRect.offsetMax = new Vector2(-SideMargin, rowBottom + rowHeight);

            var rowImage = row.GetComponent<Image>();
            UiStyle.Round(rowImage, s_Rounded, 42f);
            rowImage.color = new Color(0.05f, 0.06f, 0.08f, 0.78f);
            rowImage.raycastTarget = false;

            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(28, 12, 10, 10);
            layout.spacing = 12;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleCenter;

            TextMeshProUGUI status = CreateText(rowRect, "Status", 22, FontStyles.Normal, TextAlignmentOptions.Left, UiStyle.TextMuted);
            status.enableWordWrapping = false;
            status.overflowMode = TextOverflowModes.Ellipsis;
            status.raycastTarget = false;
            var statusLayout = status.gameObject.AddComponent<LayoutElement>();
            statusLayout.flexibleWidth = 1;

            HoldButton lower = CreateHoldButton(rowRect, "LowerButton", "Lower floor");
            HoldButton raise = CreateHoldButton(rowRect, "RaiseButton", "Raise floor");

            var controls = holder.GetComponent<FloorAdjustControls>();
            AssignSerializedField(controls, "m_Row", row);
            AssignSerializedField(controls, "m_LowerButton", lower);
            AssignSerializedField(controls, "m_RaiseButton", raise);
            AssignSerializedField(controls, "m_Status", status);
        }

        static void BuildMoveCard(Transform parent)
        {
            float bottom = SheetHeight + FloatingGap + SelectionCardHeight + FloatingGap;

            // Script on an always-active holder; only the card is shown/hidden with the selection.
            var holder = new GameObject("MoveControls", typeof(RectTransform), typeof(ObjectNudgeControls));
            var holderRect = (RectTransform)holder.transform;
            holderRect.SetParent(parent, false);
            Stretch(holderRect, 0, 0);

            var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            var cardRect = (RectTransform)card.transform;
            cardRect.SetParent(holderRect, false);
            cardRect.anchorMin = new Vector2(0f, 0f);
            cardRect.anchorMax = new Vector2(1f, 0f);
            cardRect.pivot = new Vector2(0.5f, 0f);
            cardRect.offsetMin = new Vector2(SideMargin, bottom);
            cardRect.offsetMax = new Vector2(-SideMargin, bottom + MoveCardHeight);

            var image = card.GetComponent<Image>();
            UiStyle.Round(image, s_Rounded, 40f);
            image.color = UiStyle.Panel;
            AddShadow(card);

            HoldButton left = null, right = null, away = null, closer = null, turnLeft = null, turnRight = null;
            RectTransform rowA = CreateMoveRow(cardRect, "MoveRow", 118f, 202f);
            left = CreateFlexibleHoldButton(rowA, "LeftButton", "Left");
            right = CreateFlexibleHoldButton(rowA, "RightButton", "Right");
            away = CreateFlexibleHoldButton(rowA, "AwayButton", "Away");
            closer = CreateFlexibleHoldButton(rowA, "CloserButton", "Closer");

            RectTransform rowB = CreateMoveRow(cardRect, "TurnRow", 22f, 106f);
            turnLeft = CreateFlexibleHoldButton(rowB, "TurnLeftButton", "Turn left");
            turnRight = CreateFlexibleHoldButton(rowB, "TurnRightButton", "Turn right");

            var controls = holder.GetComponent<ObjectNudgeControls>();
            AssignSerializedField(controls, "m_Card", card);
            AssignSerializedField(controls, "m_LeftButton", left);
            AssignSerializedField(controls, "m_RightButton", right);
            AssignSerializedField(controls, "m_AwayButton", away);
            AssignSerializedField(controls, "m_CloserButton", closer);
            AssignSerializedField(controls, "m_TurnLeftButton", turnLeft);
            AssignSerializedField(controls, "m_TurnRightButton", turnRight);
        }

        static RectTransform CreateMoveRow(RectTransform card, string name, float bottomOffset, float topOffset)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var rect = (RectTransform)go.transform;
            rect.SetParent(card, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(24, bottomOffset);
            rect.offsetMax = new Vector2(-24, topOffset);

            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            return rect;
        }

        static HoldButton CreateFlexibleHoldButton(Transform parent, string name, string label)
        {
            HoldButton button = CreateHoldButton(parent, name, label);
            var le = button.GetComponent<LayoutElement>();
            le.preferredWidth = -1;
            le.flexibleWidth = 1;
            return button;
        }

        static void BuildStatusRow(Transform parent)
        {
            const float rowTop = 124f;
            const float rowHeight = 72f;

            var holder = new GameObject("StatusRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(StatusChips), typeof(MeasureTool));
            var rect = (RectTransform)holder.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(SideMargin, -(rowTop + rowHeight));
            rect.offsetMax = new Vector2(-SideMargin, -rowTop);

            var layout = holder.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;

            // Live layout summary
            var stats = new GameObject("StatsChip", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(LayoutElement));
            var statsRect = (RectTransform)stats.transform;
            statsRect.SetParent(rect, false);
            var statsImage = stats.GetComponent<Image>();
            UiStyle.Round(statsImage, s_Rounded, 36f);
            statsImage.color = new Color(0.05f, 0.06f, 0.08f, 0.78f);
            statsImage.raycastTarget = false;
            stats.GetComponent<LayoutElement>().flexibleWidth = 1;
            var statsGroup = stats.GetComponent<CanvasGroup>();
            statsGroup.blocksRaycasts = false;
            statsGroup.interactable = false;

            TextMeshProUGUI statsText = CreateText(statsRect, "Text", 22, FontStyles.Bold, TextAlignmentOptions.Left, UiStyle.TextPrimary);
            Stretch(statsText.rectTransform, 24, 4);
            statsText.enableWordWrapping = false;
            statsText.overflowMode = TextOverflowModes.Ellipsis;
            statsText.raycastTarget = false;

            Button measureBtn = CreateButton(rect, "MeasureButton", "Measure", UiStyle.Secondary, 36f, 24);
            measureBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 200;
            TextMeshProUGUI measureLabel = measureBtn.GetComponentInChildren<TextMeshProUGUI>();

            Button undoBtn = CreateButton(rect, "UndoButton", "Undo", UiStyle.Secondary, 36f, 24);
            undoBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 150;

            var chips = holder.GetComponent<StatusChips>();
            AssignSerializedField(chips, "m_StatsGroup", statsGroup);
            AssignSerializedField(chips, "m_StatsText", statsText);
            AssignSerializedField(chips, "m_MeasureButton", measureBtn);
            AssignSerializedField(chips, "m_MeasureLabel", measureLabel);
            AssignSerializedField(chips, "m_UndoButton", undoBtn);

            // Floating distance label shown over the measured line
            var label = new GameObject("MeasureLabel", typeof(RectTransform), typeof(Image));
            var labelRect = (RectTransform)label.transform;
            labelRect.SetParent(parent, false);
            labelRect.anchorMin = new Vector2(0.5f, 0.5f);
            labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.sizeDelta = new Vector2(440, 70);
            var labelImage = label.GetComponent<Image>();
            UiStyle.Round(labelImage, s_Rounded, 34f);
            labelImage.color = new Color(0.05f, 0.06f, 0.08f, 0.88f);
            labelImage.raycastTarget = false;

            TextMeshProUGUI labelText = CreateText(labelRect, "Text", 30, FontStyles.Bold, TextAlignmentOptions.Center, new Color(0.55f, 0.9f, 1f, 1f));
            Stretch(labelText.rectTransform, 16, 4);
            labelText.raycastTarget = false;
            label.SetActive(false);

            var tool = holder.GetComponent<MeasureTool>();
            AssignSerializedField(tool, "m_LineMaterial", s_LineMaterial);
            AssignSerializedField(tool, "m_Label", labelText);
            AssignSerializedField(tool, "m_LabelRoot", labelRect);
            AssignSerializedField(tool, "m_LabelParent", (RectTransform)parent);
        }

        static HoldButton CreateHoldButton(Transform parent, string name, string label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            UiStyle.Round(image, s_Rounded, 26f);
            image.color = UiStyle.Secondary;

            var layoutElement = go.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = 200;

            TextMeshProUGUI text = CreateText(rect, "Label", 22, FontStyles.Bold, TextAlignmentOptions.Center, UiStyle.TextPrimary);
            text.text = label;
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            Stretch(text.rectTransform, 8, 4);

            return go.AddComponent<HoldButton>();
        }

        // ── Generic UI construction helpers ───────────────────

        static TextMeshProUGUI CreateText(Transform parent, string name, float fontSize, FontStyles style, TextAlignmentOptions alignment, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.text = string.Empty;
            return text;
        }

        /// <summary>Rounded, flat-colour, softly shadowed button — the shared style for every action button.</summary>
        static Button CreateButton(Transform parent, string name, string label, Color color, float radius, float fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            UiStyle.Round(image, s_Rounded, radius);
            image.color = color;
            AddShadow(go);

            TextMeshProUGUI text = CreateText(rect, "Label", fontSize, FontStyles.Bold, TextAlignmentOptions.Center, UiStyle.TextPrimary);
            text.text = label;
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            Stretch(text.rectTransform, 8, 4);

            return go.GetComponent<Button>();
        }

        static void AddShadow(GameObject go) => AddShadow(go, new Vector2(0f, -4f));

        static void AddShadow(GameObject go, Vector2 distance)
        {
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.32f);
            shadow.effectDistance = distance;
        }

        static void Stretch(RectTransform rect, float horizontalPadding, float verticalPadding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(horizontalPadding, verticalPadding);
            rect.offsetMax = new Vector2(-horizontalPadding, -verticalPadding);
        }

        static void AssignSerializedBool(Object target, string fieldName, bool value)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null)
                return;

            prop.boolValue = value;
            so.ApplyModifiedProperties();
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
