using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using ARSpace.Core;

namespace ARSpace.Placement
{
    /// <summary>
    /// Service that handles selecting and deselecting placed furniture items in the world.
    /// Ensures:
    /// - Only one object is selected at any time.
    /// - Pointer-over-UI checks prevent world taps when interacting with UI.
    /// - Tapping an object selects it; tapping an empty detected floor surface deselects.
    /// - Fires <see cref="GameEvents.ObjectSelectionChanged"/>.
    /// </summary>
    public class ObjectSelectionService : MonoBehaviour
    {
        [Header("Selection Layer")]
        [Tooltip("Layer mask for placed object selection raycasts.")]
        [SerializeField]
        LayerMask m_ObjectLayerMask = ~0;

        PlacedObject m_SelectedObject;
        Camera m_MainCamera;

        public PlacedObject SelectedObject => m_SelectedObject;
        public bool HasSelection => m_SelectedObject != null;

        void Awake()
        {
            ServiceLocator.Register(this);
            m_MainCamera = Camera.main;
        }

        void OnDestroy()
        {
            ServiceLocator.Unregister<ObjectSelectionService>();
        }

        void OnEnable()
        {
            GameEvents.ObjectRemoved += OnObjectRemoved;
        }

        void OnDisable()
        {
            GameEvents.ObjectRemoved -= OnObjectRemoved;
        }

        public void Select(PlacedObject obj)
        {
            if (obj == m_SelectedObject)
                return;

            if (m_SelectedObject != null)
            {
                m_SelectedObject.SetSelected(false);
            }

            m_SelectedObject = obj;

            if (m_SelectedObject != null)
            {
                m_SelectedObject.SetSelected(true);
                var app = ServiceLocator.Get<ARSpaceApp>();
                if (app != null)
                {
                    app.RequestStateChange(AppState.ObjectSelected);
                }
                GameEvents.RaiseObjectSelectionChanged(m_SelectedObject.gameObject);
                Debug.Log($"[ObjectSelectionService] Selected '{m_SelectedObject.name}'");
            }
            else
            {
                Deselect();
            }
        }

        public void Deselect()
        {
            if (m_SelectedObject == null)
                return;

            m_SelectedObject.SetSelected(false);
            m_SelectedObject = null;

            var app = ServiceLocator.Get<ARSpaceApp>();
            if (app != null && app.CurrentState == AppState.ObjectSelected)
            {
                app.RequestStateChange(AppState.Browsing);
            }

            GameEvents.RaiseObjectSelectionChanged(null);
            Debug.Log("[ObjectSelectionService] Deselected object.");
        }

        /// <summary>
        /// Evaluates a screen tap for object selection or deselection.
        /// Returns true if a tap was handled.
        /// </summary>
        public bool ProcessTap(Vector2 screenPosition, int fingerId = 0)
        {
            // 1. Guard against UI clicks
            if (UiPointer.IsOverUI(screenPosition))
                return false;

            if (m_MainCamera == null)
                m_MainCamera = Camera.main;

            if (m_MainCamera == null)
                return false;

            Ray ray = m_MainCamera.ScreenPointToRay(screenPosition);

            // 2. Raycast for PlacedObject
            // RaycastAll so AR plane mesh colliders sitting at the furniture's feet can't shadow the furniture itself.
            RaycastHit[] hits = Physics.RaycastAll(ray, 20f, m_ObjectLayerMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                var placedObj = hits[i].collider.GetComponentInParent<PlacedObject>();
                if (placedObj != null)
                {
                    Select(placedObj);
                    return true;
                }
            }

            // 3. Tapping empty floor deselects active selection
            if (m_SelectedObject != null)
            {
                Deselect();
                return true;
            }

            return false;
        }

        /// <summary>True when the first placed object under the screen position is <paramref name="obj"/>.</summary>
        public bool IsPointerOnObject(Vector2 screenPosition, PlacedObject obj)
        {
            if (obj == null)
                return false;

            if (m_MainCamera == null)
                m_MainCamera = Camera.main;
            if (m_MainCamera == null)
                return false;

            Ray ray = m_MainCamera.ScreenPointToRay(screenPosition);
            RaycastHit[] hits = Physics.RaycastAll(ray, 20f, m_ObjectLayerMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                var placed = hits[i].collider.GetComponentInParent<PlacedObject>();
                if (placed != null)
                    return placed == obj;
            }
            return false;
        }

        void OnObjectRemoved(GameObject removedGo)
        {
            if (m_SelectedObject != null && m_SelectedObject.gameObject == removedGo)
            {
                Deselect();
            }
        }
    }
}
