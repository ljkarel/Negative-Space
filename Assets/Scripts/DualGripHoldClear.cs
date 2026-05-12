using UnityEngine;
using IVLab.MinVR3;

public class DualGripHoldClear : MonoBehaviour
{
    [SerializeField] private VREventCallbackAny m_LeftGripEvent;
    [SerializeField] private VREventCallbackAny m_RightGripEvent;
    [SerializeField] private MainPaintingAndReframingUI m_PaintingUI;
    [SerializeField] private float m_HoldDuration = 3f;

    private const float k_GripThreshold = 0.5f;

    private float m_LeftGrip;
    private float m_RightGrip;
    private float m_HeldTime;

    private void Awake()
    {
        m_LeftGripEvent.AddRuntimeListener<float>(v  => m_LeftGrip  = v);
        m_RightGripEvent.AddRuntimeListener<float>(v => m_RightGrip = v);
    }

    private void OnEnable()  { m_LeftGripEvent.StartListening();  m_RightGripEvent.StartListening(); }
    private void OnDisable() { m_LeftGripEvent.StopListening();   m_RightGripEvent.StopListening(); }

    private void Update()
    {
        if (m_LeftGrip > k_GripThreshold && m_RightGrip > k_GripThreshold)
        {
            m_HeldTime += Time.deltaTime;
            if (m_HeldTime >= m_HoldDuration)
            {
                m_PaintingUI.ClearArtworkUndoable();
                m_HeldTime = 0f;
            }
        }
        else
        {
            m_HeldTime = 0f;
        }
    }
}
