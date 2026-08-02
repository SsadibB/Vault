using UnityEngine;
using UnityEngine.EventSystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
#endif

namespace Vault.AI
{
    /// <summary>
    /// Super-optimized Clash of Clans style top-down camera controller.
    /// Fully supports both the new Unity Input System package and legacy Input Manager.
    /// Features 1:1 ground-plane dragging, smooth inertia momentum, pinch-to-zoom & scroll zoom,
    /// map bounds clamping, and UI raycast safety with 0 GC allocations per frame.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class TopDownCameraController : MonoBehaviour
    {
        [Header("Camera Perspective Setup")]
        [Tooltip("Target rotation pitch angle (X-axis). Clash of Clans uses ~50 degrees.")]
        [SerializeField] private float pitchAngle = 50f;
        [Tooltip("Target rotation yaw angle (Y-axis). 45 for isometric, 0 for north-up.")]
        [SerializeField] private float yawAngle = 0f;
        [Tooltip("Ground plane height (Y-axis) used for 1:1 screen-to-world drag calculations.")]
        [SerializeField] private float groundHeight = 0f;

        [Header("Pan & Inertia Settings")]
        [Tooltip("Smoothness speed when interpolating position.")]
        [SerializeField] private float smoothSpeed = 15f;
        [Tooltip("Inertia momentum decay factor after releasing drag.")]
        [SerializeField] private float inertiaDamping = 8f;
        [Tooltip("Enable smooth momentum glide on drag release.")]
        [SerializeField] private bool enableInertia = true;

        [Header("Zoom Settings")]
        [Tooltip("Default zoom distance at start. 13.05 corresponds to camera Y position of exactly 10.0 at 50 degrees pitch.")]
        [SerializeField] private float defaultZoom = 13.05f;
        [Tooltip("Minimum zoom distance allowed (13.05 = Y height of 10.0).")]
        [SerializeField] private float minZoom = 13.05f;
        [SerializeField] private float maxZoom = 40f;
        [SerializeField] private float zoomSpeedMouse = 10f;
        [SerializeField] private float zoomSpeedTouch = 0.05f;
        [SerializeField] private float zoomSmoothness = 12f;

        [Header("Map Bounds (World X & Z)")]
        [SerializeField] private bool useBounds = true;
        [SerializeField] private Vector2 minBounds = new Vector2(-50f, -50f);
        [SerializeField] private Vector2 maxBounds = new Vector2(50f, 50f);

        // Cached components & math state
        private Camera cam;
        private Plane groundPlane;
        private Vector3 targetFocusPosition;
        private Vector3 dragVelocity;
        private Vector3 lastDragGroundPos;
        private float targetZoom;
        private float currentZoom;
        private bool isDragging;
        private int lastTouchCount;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            groundPlane = new Plane(Vector3.up, new Vector3(0f, groundHeight, 0f));

            // Set initial Clash of Clans orientation
            transform.rotation = Quaternion.Euler(pitchAngle, yawAngle, 0f);

            // Initialize target position focused on ground
            Ray ray = new Ray(transform.position, transform.forward);
            if (groundPlane.Raycast(ray, out float enter))
            {
                targetFocusPosition = ray.GetPoint(enter);
            }
            else
            {
                targetFocusPosition = transform.position;
            }

            // Initialize zoom
            if (cam.orthographic)
            {
                currentZoom = targetZoom = Mathf.Clamp(defaultZoom, minZoom, maxZoom);
                cam.orthographicSize = currentZoom;
            }
            else
            {
                currentZoom = targetZoom = Mathf.Clamp(defaultZoom, minZoom, maxZoom);
            }
        }

        private void OnEnable()
        {
#if ENABLE_INPUT_SYSTEM
            EnhancedTouchSupport.Enable();
#endif
        }

        private void OnDisable()
        {
#if ENABLE_INPUT_SYSTEM
            EnhancedTouchSupport.Disable();
#endif
        }

        private void Start()
        {
            UpdateCameraTransform(true);
        }

        private void LateUpdate()
        {
            HandleInput();
            ApplyMovementAndZoom();
        }

        private void HandleInput()
        {
            // Ignore input if pointer is over UI element
            if (IsPointerOverUI()) return;

#if ENABLE_INPUT_SYSTEM
            HandleNewInputSystem();
#elif ENABLE_LEGACY_INPUT_MANAGER
            HandleLegacyInputSystem();
#endif
        }

        private bool IsPointerOverUI()
        {
            if (EventSystem.current == null) return false;

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && EventSystem.current.IsPointerOverGameObject())
                return true;
            if (Touchscreen.current != null && Touch.activeTouches.Count > 0)
            {
                if (EventSystem.current.IsPointerOverGameObject(Touch.activeTouches[0].touchId))
                    return true;
            }
            return false;
#else
            if (Input.touchCount > 0 && EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId))
                return true;
            if (Input.touchCount == 0 && EventSystem.current.IsPointerOverGameObject())
                return true;
            return false;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private void HandleNewInputSystem()
        {
            // Mouse Scroll Zoom
            if (Mouse.current != null)
            {
                float scrollDelta = Mouse.current.scroll.ReadValue().y * 0.01f;
                if (Mathf.Abs(scrollDelta) > 0.001f)
                {
                    targetZoom -= scrollDelta * zoomSpeedMouse * (cam.orthographic ? 2f : (targetZoom * 0.2f));
                    targetZoom = Mathf.Clamp(targetZoom, minZoom, maxZoom);
                }

                // Mouse Drag Pan
                Vector2 mousePos = Mouse.current.position.ReadValue();
                if (Mouse.current.leftButton.wasPressedThisFrame)
                {
                    StartDrag(mousePos);
                }
                else if (Mouse.current.leftButton.isPressed && isDragging)
                {
                    UpdateDrag(mousePos);
                }
                else if (Mouse.current.leftButton.wasReleasedThisFrame && isDragging)
                {
                    EndDrag();
                }
            }

            // Touch Handling (Enhanced Touch)
            var activeTouches = Touch.activeTouches;
            if (activeTouches.Count == 1)
            {
                var touch = activeTouches[0];
                if (touch.phase == UnityEngine.InputSystem.TouchPhase.Began || lastTouchCount != 1)
                {
                    StartDrag(touch.screenPosition);
                }
                else if (touch.phase == UnityEngine.InputSystem.TouchPhase.Moved && isDragging)
                {
                    UpdateDrag(touch.screenPosition);
                }
                else if (touch.phase == UnityEngine.InputSystem.TouchPhase.Ended || touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                {
                    EndDrag();
                }
            }
            else if (activeTouches.Count == 2)
            {
                isDragging = false;
                var t0 = activeTouches[0];
                var t1 = activeTouches[1];

                Vector2 prevPos0 = t0.screenPosition - t0.delta;
                Vector2 prevPos1 = t1.screenPosition - t1.delta;

                float prevDist = (prevPos0 - prevPos1).magnitude;
                float currentDist = (t0.screenPosition - t1.screenPosition).magnitude;

                float difference = currentDist - prevDist;
                targetZoom -= difference * zoomSpeedTouch * (cam.orthographic ? 1f : (targetZoom * 0.05f));
                targetZoom = Mathf.Clamp(targetZoom, minZoom, maxZoom);
            }

            lastTouchCount = activeTouches.Count;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        private void HandleLegacyInputSystem()
        {
            // Mouse Scroll Zoom
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
            {
                targetZoom -= scroll * zoomSpeedMouse * (cam.orthographic ? 2f : (targetZoom * 0.2f));
                targetZoom = Mathf.Clamp(targetZoom, minZoom, maxZoom);
            }

            // Mouse Drag Pan
            if (Input.GetMouseButtonDown(0))
            {
                StartDrag(Input.mousePosition);
            }
            else if (Input.GetMouseButton(0) && isDragging)
            {
                UpdateDrag(Input.mousePosition);
            }
            else if (Input.GetMouseButtonUp(0) && isDragging)
            {
                EndDrag();
            }

            // Touch Handling
            if (Input.touchSupported && Input.touchCount > 0)
            {
                if (Input.touchCount == 1)
                {
                    UnityEngine.Touch touch = Input.GetTouch(0);
                    if (touch.phase == UnityEngine.TouchPhase.Began || lastTouchCount != 1)
                    {
                        StartDrag(touch.position);
                    }
                    else if (touch.phase == UnityEngine.TouchPhase.Moved && isDragging)
                    {
                        UpdateDrag(touch.position);
                    }
                    else if (touch.phase == UnityEngine.TouchPhase.Ended || touch.phase == UnityEngine.TouchPhase.Canceled)
                    {
                        EndDrag();
                    }
                }
                else if (Input.touchCount == 2)
                {
                    isDragging = false;
                    UnityEngine.Touch touch0 = Input.GetTouch(0);
                    UnityEngine.Touch touch1 = Input.GetTouch(1);

                    Vector2 prevPos0 = touch0.position - touch0.deltaPosition;
                    Vector2 prevPos1 = touch1.position - touch1.deltaPosition;

                    float prevMagnitude = (prevPos0 - prevPos1).magnitude;
                    float currentMagnitude = (touch0.position - touch1.position).magnitude;

                    float difference = currentMagnitude - prevMagnitude;
                    targetZoom -= difference * zoomSpeedTouch * (cam.orthographic ? 1f : (targetZoom * 0.05f));
                    targetZoom = Mathf.Clamp(targetZoom, minZoom, maxZoom);
                }

                lastTouchCount = Input.touchCount;
            }
        }
#endif

        private void StartDrag(Vector3 screenPos)
        {
            Ray ray = cam.ScreenPointToRay(screenPos);
            if (groundPlane.Raycast(ray, out float enter))
            {
                lastDragGroundPos = ray.GetPoint(enter);
                isDragging = true;
                dragVelocity = Vector3.zero;
            }
        }

        private void UpdateDrag(Vector3 screenPos)
        {
            Ray ray = cam.ScreenPointToRay(screenPos);
            if (groundPlane.Raycast(ray, out float enter))
            {
                Vector3 currentGroundPos = ray.GetPoint(enter);
                Vector3 delta = lastDragGroundPos - currentGroundPos;

                Vector3 newTargetPos = targetFocusPosition + delta;

                if (useBounds)
                {
                    newTargetPos.x = Mathf.Clamp(newTargetPos.x, minBounds.x, maxBounds.x);
                    newTargetPos.z = Mathf.Clamp(newTargetPos.z, minBounds.y, maxBounds.y);
                }

                if (enableInertia && Time.deltaTime > 0f)
                {
                    dragVelocity = (newTargetPos - targetFocusPosition) / Time.deltaTime;
                }

                targetFocusPosition = newTargetPos;

                Ray newRay = cam.ScreenPointToRay(screenPos);
                if (groundPlane.Raycast(newRay, out float newEnter))
                {
                    lastDragGroundPos = newRay.GetPoint(newEnter);
                }
            }
        }

        private void EndDrag()
        {
            isDragging = false;
        }

        private void ApplyMovementAndZoom()
        {
            if (!isDragging && enableInertia && dragVelocity.sqrMagnitude > 0.001f)
            {
                targetFocusPosition += dragVelocity * Time.deltaTime;
                dragVelocity = Vector3.Lerp(dragVelocity, Vector3.zero, inertiaDamping * Time.deltaTime);
            }

            if (useBounds)
            {
                targetFocusPosition.x = Mathf.Clamp(targetFocusPosition.x, minBounds.x, maxBounds.x);
                targetFocusPosition.z = Mathf.Clamp(targetFocusPosition.z, minBounds.y, maxBounds.y);
            }

            currentZoom = Mathf.Lerp(currentZoom, targetZoom, Time.deltaTime * zoomSmoothness);

            if (cam.orthographic)
            {
                cam.orthographicSize = currentZoom;
            }

            UpdateCameraTransform(false);
        }

        private void UpdateCameraTransform(bool immediate)
        {
            Quaternion targetRotation = Quaternion.Euler(pitchAngle, yawAngle, 0f);
            transform.rotation = targetRotation;

            float distance = cam.orthographic ? 50f : currentZoom;
            Vector3 desiredCamPos = targetFocusPosition - (transform.forward * distance);

            if (immediate)
            {
                transform.position = desiredCamPos;
            }
            else
            {
                transform.position = Vector3.Lerp(transform.position, desiredCamPos, Time.deltaTime * smoothSpeed);
            }
        }

        public void FocusOn(Vector3 worldPosition, bool immediate = false)
        {
            targetFocusPosition = worldPosition;
            targetFocusPosition.y = groundHeight;
            if (useBounds)
            {
                targetFocusPosition.x = Mathf.Clamp(targetFocusPosition.x, minBounds.x, maxBounds.x);
                targetFocusPosition.z = Mathf.Clamp(targetFocusPosition.z, minBounds.y, maxBounds.y);
            }
            UpdateCameraTransform(immediate);
        }

        private void OnDrawGizmosSelected()
        {
            if (!useBounds) return;

            Gizmos.color = Color.cyan;
            Vector3 center = new Vector3((minBounds.x + maxBounds.x) * 0.5f, groundHeight, (minBounds.y + maxBounds.y) * 0.5f);
            Vector3 size = new Vector3(maxBounds.x - minBounds.x, 0.1f, maxBounds.y - minBounds.y);
            Gizmos.DrawWireCube(center, size);
        }
    }
}
