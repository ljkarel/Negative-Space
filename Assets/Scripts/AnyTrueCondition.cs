using UnityEngine;
using IVLab.MinVR3;

public class AnyTrueCondition : Condition
{
    [System.Serializable]
    private struct Entry { public Condition condition; public bool negate; }

    [SerializeField] private Entry[] m_Conditions;

    private void Update()
    {
        foreach (var e in m_Conditions)
        {
            bool val = e.condition != null && e.condition.isTrue;
            if (e.negate) val = !val;
            if (val) { isTrue = true; return; }
        }
        isTrue = false;
    }
}
