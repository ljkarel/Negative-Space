using System;
using System.Collections.Generic;
using UnityEngine;

namespace IVLab.MinVR3
{

    public class MainPaintingAndReframingUI : MonoBehaviour
    {
        public Color brushColor {
            get { return m_BrushColor; }
            set { SetBrushColor(value); }
        }

        public void SetBrushColor(Color c)
        {
            m_BrushColor = c;
            if (!m_EraserModeActive)
            {
                m_BrushCursorMeshRenderer.sharedMaterial.color = c;
            }
        }

        public void SetBrushColor(Vector4 c)
        {
            SetBrushColor(new Color(c[0], c[1], c[2], c[3]));
        }

        public void ToggleEraserMode()
        {
            m_EraserModeActive = !m_EraserModeActive;
            if (m_EraserModeActive) {
                m_SavedBrushCursorMaterial = m_BrushCursorMeshRenderer.sharedMaterial;
                m_BrushCursorMeshRenderer.sharedMaterial = m_EraserCursorMaterial;
            } else {
                m_BrushCursorMeshRenderer.sharedMaterial = m_SavedBrushCursorMaterial;
                m_SavedBrushCursorMaterial.color = m_BrushColor; // sync color in case it changed while erasing
            }
        }

        public void ToggleEraserStrokeVisibility()
        {
            m_EraserStrokesVisible = !m_EraserStrokesVisible;
            m_EraserMaterial.SetFloat("_ShowVisible", m_EraserStrokesVisible ? 1f : 0f);
        }

        public void Undo()
        {
            if (m_UndoStack.Count == 0) return;
            var op = m_UndoStack.Pop();
            op.Undo();
            m_RedoStack.Push(op);
        }

        public void Redo()
        {
            if (m_RedoStack.Count == 0) return;
            var op = m_RedoStack.Pop();
            op.Redo();
            m_UndoStack.Push(op);
        }

        private void PushUndo(IUndoable op)
        {
            m_UndoStack.Push(op);
            m_RedoStack.Clear();
        }

        private void Reset()
        {
            m_ArtworkParentTransform = null;
            m_BrushCursorTransform = null;
            m_HandCursorTransform = null;
        }

        private void Start()
        {
            Debug.Assert(m_ArtworkParentTransform != null);
            Debug.Assert(m_BrushCursorTransform != null);
            Debug.Assert(m_BrushCursorMeshRenderer != null);
            Debug.Assert(m_HandCursorTransform != null);
            Debug.Assert(m_PaintMaterial != null);
            Debug.Assert(m_EraserMaterial != null);
            Debug.Assert(m_EraserCursorMaterial != null);

            m_NumStrokes = 0;
        }


        // PAINTING STATE CALLBACKS

        public void Painting_OnEnter()
        {
            // create a new GameObject to hold the new paint stroke
            m_CurrentStrokeObj = new GameObject("Stroke " + m_NumStrokes);
            m_CurrentStrokeObj.transform.SetParent(m_ArtworkParentTransform, false);

            // normals can get weird when using two-sided rendering, and Unity's standard shaders do not support it.
            // but we would like to see both sides of the ribbons we paint.  so, the solution is to create two meshes
            // one to draw the "front" side of the ribbon and one to draw the "back" side of the ribbon.  the only
            // change between front and back is swapping the vertex ordering of each triangle.
            GameObject frontMeshObj = new GameObject("FrontMesh", typeof(MeshFilter), typeof(MeshRenderer));
            frontMeshObj.transform.SetParent(m_CurrentStrokeObj.transform, false);
            MeshRenderer frontMeshRenderer = frontMeshObj.GetComponent<MeshRenderer>();
            Material strokeMaterial;
            if (m_EraserModeActive) {
                strokeMaterial = m_EraserMaterial; // shared instance -- no per-stroke properties needed
            } else {
                strokeMaterial = new Material(m_PaintMaterial);
                strokeMaterial.color = m_BrushColor;
            }
            frontMeshRenderer.sharedMaterial = strokeMaterial;
            m_CurrentStrokeFrontMesh = frontMeshRenderer.GetComponent<MeshFilter>().mesh;
            m_CurrentStrokeFrontMesh.MarkDynamic();
            m_CurrentStrokeFrontVertices = new List<Vector3>();
            m_CurrentStrokeFrontIndices = new List<int>();

            GameObject backMeshObj = new GameObject("BackMesh", typeof(MeshFilter), typeof(MeshRenderer));
            backMeshObj.transform.SetParent(m_CurrentStrokeObj.transform, false);
            MeshRenderer backMeshRenderer = backMeshObj.GetComponent<MeshRenderer>();
            backMeshRenderer.sharedMaterial = strokeMaterial;
            m_CurrentStrokeBackMesh = backMeshRenderer.GetComponent<MeshFilter>().mesh;
            m_CurrentStrokeBackMesh.MarkDynamic();
            m_CurrentStrokeBackVertices = new List<Vector3>();
            m_CurrentStrokeBackIndices = new List<int>();
        }

        public void Painting_OnUpdate()
        {
            // find the points at the left edge and right edge of the brush bristles.  In the raw CavePainting brush
            // model, these vertices are at (-0.5, 0, 0) and (0.5, 0, 0)
            Vector3 leftBrushPtWorld = m_BrushCursorTransform.LocalPointToWorldSpace(new Vector3(-0.5f, 0, 0));
            Vector3 rightBrushPtWorld = m_BrushCursorTransform.LocalPointToWorldSpace(new Vector3(0.5f, 0, 0));

            // convert these into the local space of the stroke, which has already been added to the artwork parent
            Vector3 leftBrushPtArtwork = m_CurrentStrokeObj.transform.WorldPointToLocalSpace(leftBrushPtWorld);
            Vector3 rightBrushPtArtwork = m_CurrentStrokeObj.transform.WorldPointToLocalSpace(rightBrushPtWorld);


            // ADD TO THE FRONT MESH

            // push two new vertices for these points to back of the vertex list
            m_CurrentStrokeFrontVertices.Add(leftBrushPtArtwork);
            m_CurrentStrokeFrontVertices.Add(rightBrushPtArtwork);

            // add two triangles to the stroke mesh to connect the last left/right points to the current ones
            if (m_CurrentStrokeFrontVertices.Count >= 4) {
                // construct two traingles with the last four vertices added
                int v0 = m_CurrentStrokeFrontVertices.Count - 4; // last left
                int v1 = m_CurrentStrokeFrontVertices.Count - 3; // last right
                int v2 = m_CurrentStrokeFrontVertices.Count - 2; // cur left
                int v3 = m_CurrentStrokeFrontVertices.Count - 1; // cur right

                // tri #1 (note: Unity uses clockwise ordering)
                m_CurrentStrokeFrontIndices.Add(v0);
                m_CurrentStrokeFrontIndices.Add(v2);
                m_CurrentStrokeFrontIndices.Add(v3);

                // tri #2
                m_CurrentStrokeFrontIndices.Add(v0);
                m_CurrentStrokeFrontIndices.Add(v3);
                m_CurrentStrokeFrontIndices.Add(v1);

                // update the mesh
                m_CurrentStrokeFrontMesh.Clear();
                m_CurrentStrokeFrontMesh.SetVertices(m_CurrentStrokeFrontVertices);
                m_CurrentStrokeFrontMesh.SetIndices(m_CurrentStrokeFrontIndices, MeshTopology.Triangles, 0);
                m_CurrentStrokeFrontMesh.RecalculateNormals();
            }


            // ADD TO THE BACK MESH (only difference is the ordering of the vertices in the two triangles)

            // push two new vertices for these points to back of the vertex list
            m_CurrentStrokeBackVertices.Add(leftBrushPtArtwork);
            m_CurrentStrokeBackVertices.Add(rightBrushPtArtwork);

            // add two triangles to the stroke mesh to connect the last left/right points to the current ones
            if (m_CurrentStrokeBackVertices.Count >= 4) {

                // construct two traingles with the last four vertices added
                int v0 = m_CurrentStrokeBackVertices.Count - 4; // last left
                int v1 = m_CurrentStrokeBackVertices.Count - 3; // last right
                int v2 = m_CurrentStrokeBackVertices.Count - 2; // cur left
                int v3 = m_CurrentStrokeBackVertices.Count - 1; // cur right

                // tri #1 (note: Unity uses clockwise ordering)
                m_CurrentStrokeBackIndices.Add(v0);
                m_CurrentStrokeBackIndices.Add(v3);
                m_CurrentStrokeBackIndices.Add(v2);

                // tri #2
                m_CurrentStrokeBackIndices.Add(v0);
                m_CurrentStrokeBackIndices.Add(v1);
                m_CurrentStrokeBackIndices.Add(v3);

                // update the mesh
                m_CurrentStrokeBackMesh.Clear();
                m_CurrentStrokeBackMesh.SetVertices(m_CurrentStrokeBackVertices);
                m_CurrentStrokeBackMesh.SetIndices(m_CurrentStrokeBackIndices, MeshTopology.Triangles, 0);
                m_CurrentStrokeBackMesh.RecalculateNormals();
            }
        }


        public void Painting_OnExit()
        {
            PushUndo(new StrokeRecord(m_CurrentStrokeObj));
            m_NumStrokes++;
        }


        // TRANS-ROT-ARTWORK STATE CALLBACKS

        public void TransRotArtwork_OnEnter()
        {
            m_ArtworkSnapshotBefore = TransformSnapshot.Capture(m_ArtworkParentTransform);
            m_LastHandPos = m_HandCursorTransform.position;
            m_LastHandRot = m_HandCursorTransform.rotation;
        }

        public void TransRotArtwork_OnUpdate()
        {
            Vector3 handPosWorld = m_HandCursorTransform.position;
            Vector3 deltaPosWorld = handPosWorld - m_LastHandPos;

            Quaternion handRotWorld = m_HandCursorTransform.rotation;
            Quaternion deltaRotWorld = handRotWorld * Quaternion.Inverse(m_LastHandRot);

            m_ArtworkParentTransform.TranslateByWorldVector(deltaPosWorld);
            m_ArtworkParentTransform.RotateAroundWorldPoint(handPosWorld, deltaRotWorld);

            m_LastHandPos = handPosWorld;
            m_LastHandRot = handRotWorld;
        }

        public void TransRotArtwork_OnExit()
        {
            PushUndo(new TransformRecord(m_ArtworkParentTransform, m_ArtworkSnapshotBefore, TransformSnapshot.Capture(m_ArtworkParentTransform)));
        }


        // SCALE-ARTWORK STATE CALLBACKS

        public void ScaleArtwork_OnEnter()
        {
            m_ArtworkSnapshotBefore = TransformSnapshot.Capture(m_ArtworkParentTransform);
            m_LastBrushPos = m_BrushCursorTransform.position;
        }

        public void ScaleArtwork_OnUpdate()
        {
            Vector3 handPosWorld = m_HandCursorTransform.position;
            Vector3 brushPosWorld = m_BrushCursorTransform.position;
            Vector3 curSpan = handPosWorld - brushPosWorld;
            Vector3 lastSpan = m_LastHandPos - m_LastBrushPos;

            float deltaScale = curSpan.magnitude / lastSpan.magnitude;
            m_ArtworkParentTransform.ScaleAroundWorldPoint(handPosWorld, deltaScale);

            m_LastHandPos = handPosWorld;
            m_LastBrushPos = brushPosWorld;
        }

        public void ScaleArtwork_OnExit()
        {
            PushUndo(new TransformRecord(m_ArtworkParentTransform, m_ArtworkSnapshotBefore, TransformSnapshot.Capture(m_ArtworkParentTransform)));
        }


        [Tooltip("Parent Transform for any 3D geometry produced by painting.")]
        [SerializeField] private Transform m_ArtworkParentTransform;
        [Tooltip("The brush cursor mesh renderer.")]
        [SerializeField] private MeshRenderer m_BrushCursorMeshRenderer;
        [Tooltip("The transform of the brush cursor.")]
        [SerializeField] private Transform m_BrushCursorTransform;
        [Tooltip("The transform of the hand cursor.")]
        [SerializeField] private Transform m_HandCursorTransform;

        [Tooltip("The base material for the paint -- color is added to this.")]
        [SerializeField] private Material m_PaintMaterial;

        [Tooltip("Depth-mask material for eraser strokes -- invisible but occludes geometry behind it.")]
        [SerializeField] private Material m_EraserMaterial;

        [Tooltip("Glow material applied to the brush cursor while in eraser mode.")]
        [SerializeField] private Material m_EraserCursorMaterial;

        [Tooltip("The current brush color.")]
        [SerializeField] private Color m_BrushColor;


        // runtime only

        // for eraser mode
        private bool m_EraserModeActive = false;
        private bool m_EraserStrokesVisible = false;
        private Material m_SavedBrushCursorMaterial;

        // for painting ribbon strokes
        private int m_NumStrokes;
        private GameObject m_CurrentStrokeObj;

        private Mesh m_CurrentStrokeFrontMesh;
        private List<Vector3> m_CurrentStrokeFrontVertices;
        private List<int> m_CurrentStrokeFrontIndices;

        private Mesh m_CurrentStrokeBackMesh;
        private List<Vector3> m_CurrentStrokeBackVertices;
        private List<int> m_CurrentStrokeBackIndices;

        // for other interactions
        private Vector3 m_LastHandPos;
        private Quaternion m_LastHandRot;
        private Vector3 m_LastBrushPos;

        // for undo/redo
        private readonly Stack<IUndoable> m_UndoStack = new Stack<IUndoable>();
        private readonly Stack<IUndoable> m_RedoStack = new Stack<IUndoable>();
        private TransformSnapshot m_ArtworkSnapshotBefore;


        // UNDO/REDO TYPES

        private interface IUndoable
        {
            void Undo();
            void Redo();
        }

        private class StrokeRecord : IUndoable
        {
            readonly GameObject m_Stroke;
            internal StrokeRecord(GameObject stroke) { m_Stroke = stroke; }
            public void Undo() { m_Stroke.SetActive(false); }
            public void Redo() { m_Stroke.SetActive(true); }
        }

        private struct TransformSnapshot
        {
            Vector3 localPosition;
            Quaternion localRotation;
            Vector3 localScale;

            internal static TransformSnapshot Capture(Transform t) => new() {
                localPosition = t.localPosition,
                localRotation = t.localRotation,
                localScale    = t.localScale
            };

            internal void ApplyTo(Transform t)
            {
                t.localPosition = localPosition;
                t.localRotation = localRotation;
                t.localScale    = localScale;
            }
        }

        private class TransformRecord : IUndoable
        {
            readonly Transform m_Target;
            readonly TransformSnapshot m_Before, m_After;

            internal TransformRecord(Transform target, TransformSnapshot before, TransformSnapshot after)
            {
                m_Target = target;
                m_Before = before;
                m_After  = after;
            }

            public void Undo() { m_Before.ApplyTo(m_Target); }
            public void Redo() { m_After.ApplyTo(m_Target); }
        }
    }

} // namespace