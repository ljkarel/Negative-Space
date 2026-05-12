using UnityEngine;
using IVLab.MinVR3;

public class WandLengthUI : MonoBehaviour
{
    [SerializeField] private VREventCallbackAny m_JoystickEvent;
    [SerializeField] private MainPaintingAndReframingUI m_PaintingUI;
    [SerializeField] private Transform m_WandCylinder;
    [SerializeField] private float m_LengthSpeed = 1.5f;
    [SerializeField] private float m_RampDuration = 1.5f;
    [SerializeField] private float m_MaxSpeedMultiplier = 8f;
    [SerializeField] private float m_MinScaleY = 1.0f;
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
        if (!m_PaintingUI.IsWandMode) return;

        float input = Mathf.Abs(m_JoystickY) > k_DeadZone ? m_JoystickY : 0f;
        if (Mathf.Approximately(input, 0f))
        {
            m_HeldTime = 0f;
            return;
        }

        m_HeldTime = Mathf.Min(m_HeldTime + Time.deltaTime, m_RampDuration);
        float speedMultiplier = Mathf.Lerp(1f, m_MaxSpeedMultiplier, m_HeldTime / m_RampDuration);

        float ds = input * speedMultiplier * m_LengthSpeed * Time.deltaTime;
        float newScaleY = Mathf.Clamp(m_WandCylinder.localScale.y + ds, m_MinScaleY, m_MaxScaleY);
        float actualDs = newScaleY - m_WandCylinder.localScale.y;
        if (Mathf.Approximately(actualDs, 0f)) return;

        m_WandCylinder.localPosition += m_WandCylinder.localRotation * new Vector3(0f, actualDs, 0f);
        m_WandCylinder.localScale = new Vector3(m_WandCylinder.localScale.x, newScaleY, m_WandCylinder.localScale.z);
    }
}
