using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using IVLab.MinVR3;

public class MainMenu : MonoBehaviour
{
    public MainPaintingAndReframingUI m_PaintingUI;

    public void OnMenuItemSelected(int itemId)
    {
        // clear artwork
        if (itemId == 0) {
            Debug.Assert(m_PaintingUI != null);
            m_PaintingUI.ClearArtworkUndoable();
        }
    }
}
