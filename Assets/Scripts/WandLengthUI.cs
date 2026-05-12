using UnityEngine;
using IVLab.MinVR3;

// SCENE SETUP:
// Drag "Brush Cursor" to be a child of "Wand" (reversing the old parent-child relationship).
// Set Brush Cursor's localPosition to (0, 1, 0) so it sits at the tip alongside Wand Tip.
//
// Final hierarchy:
//   [Hand Controller]
//     └── Wand                   ← the stick for both modes
//          ├── Wand Tip           ← local (0,1,0), used for eraser-wand detection
//          └── Brush Cursor       ← local (0,1,0), the painting brush

public class WandLengthUI : MonoBehaviour
{
    [SerializeField] private VREventCallbackAny m_JoystickEvent;
    [SerializeField] private MainPaintingAndReframingUI m_PaintingUI;
    [SerializeField] private Transform m_WandCylinder;
    [SerializeField] private Transform m_BrushCursor;
    [SerializeField] private float m_LengthSpeed = 1.5f;
    [SerializeField] private float m_RampDuration = 1.5f;
    [SerializeField] private float m_MaxSpeedMultiplier = 8f;
    [SerializeField] private float m_MinWandScaleY = 1.0f;
    [SerializeField] private float m_MinBrushScaleY = 0f;
    [SerializeField] private float m_MaxScaleY = 20.0f;

    private const float k_DeadZone = 0.1f;
    private float m_JoystickY;
    private float m_HeldTime;

    private void Awake()
    {
        m_JoystickEvent.AddRuntimeListener<Vector2>(OnJoystick);
    }

    private void OnEnable()  { m_JoystickEvent.StartListening(); }
    private void OnDisable() { m_JoystickEvent.StopListening(); }

    private void OnJoystick(Vector2 value) { m_JoystickY = value.y; }

    private void Update()
    {
        float input = Mathf.Abs(m_JoystickY) > k_DeadZone ? m_JoystickY : 0f;
        if (Mathf.Approximately(input, 0f))
        {
            m_HeldTime = 0f;
            return;
        }

        m_HeldTime = Mathf.Min(m_HeldTime + Time.deltaTime, m_RampDuration);
        float speedMultiplier = Mathf.Lerp(1f, m_MaxSpeedMultiplier, m_HeldTime / m_RampDuration);

        float minScale = m_PaintingUI.IsWandMode ? m_MinWandScaleY : m_MinBrushScaleY;
        float oldScaleY = m_WandCylinder.localScale.y;
        ExtendCylinder(m_WandCylinder, input * speedMultiplier, minScale, m_MaxScaleY);
        float newScaleY = m_WandCylinder.localScale.y;

        // The brush cursor is a child of the wand, so it rides the tip automatically.
        // Counter-scale its Y to cancel the parent scale propagation and keep it round.
        if (m_PaintingUI.IsBrushMode && !Mathf.Approximately(newScaleY, oldScaleY) && oldScaleY > 1e-6f)
        {
            float factor = newScaleY / oldScaleY;
            m_BrushCursor.localScale = new Vector3(
                m_BrushCursor.localScale.x,
                m_BrushCursor.localScale.y / factor,
                m_BrushCursor.localScale.z);
        }
    }

    // Extends only the far end (local +Y) while the near end (local -Y) stays fixed.
    // The cylinder mesh spans -1..+1 in local Y, so the near-end offset from center is
    // -localScale.y. Shifting the center by localRotation * up * actualDs keeps it in place.
    private void ExtendCylinder(Transform cylinder, float scaledInput, float minScale, float maxScale)
    {
        float ds = scaledInput * m_LengthSpeed * Time.deltaTime;
        float newScaleY = Mathf.Clamp(cylinder.localScale.y + ds, minScale, maxScale);
        float actualDs = newScaleY - cylinder.localScale.y;
        if (Mathf.Approximately(actualDs, 0f)) return;

        cylinder.localPosition += cylinder.localRotation * new Vector3(0f, actualDs, 0f);
        cylinder.localScale = new Vector3(cylinder.localScale.x, newScaleY, cylinder.localScale.z);
    }
}
