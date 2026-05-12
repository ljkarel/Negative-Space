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
            if (m_WandDrawMat != null)
                m_WandDrawMat.color = c;
            if (!m_UseWand && !m_EraseMode)
                m_BrushCursorMeshRenderer.sharedMaterial.color = c;
            else if (m_UseWand && !m_EraseMode)
                m_WandMeshRenderer.sharedMaterial.color = c;
        }

        public void SetBrushColor(Vector4 c)
        {
            SetBrushColor(new Color(c[0], c[1], c[2], c[3]));
        }

        public void ToggleWandMode()
        {
            m_UseWand = !m_UseWand;
            if (m_BrushModeCondition != null)
                m_BrushModeCondition.isTrue = !m_UseWand;
            ApplyToolMode();
        }

        public void ToggleEraseMode()
        {
            ClearWandState();
            m_EraseMode = !m_EraseMode;
            ApplyToolMode();
        }

private Material WandVisualMaterial => m_EraseMode ? m_EraserCursorMaterial : m_WandDrawMat;

        private void ApplyToolMode()
        {
            if (!m_UseWand)
            {
                ClearWandState();
                m_BrushCursorMeshRenderer.enabled = true;
                if (m_EraseMode)
                    m_BrushCursorMeshRenderer.sharedMaterial = m_EraserCursorMaterial;
                else
                {
                    m_BrushCursorMeshRenderer.sharedMaterial = m_SavedBrushCursorMaterial;
                    m_BrushCursorMeshRenderer.sharedMaterial.color = m_BrushColor;
                }
                m_WandMeshRenderer.enabled = false;
            }
            else
            {
                m_BrushCursorMeshRenderer.enabled = false;
                m_WandMeshRenderer.sharedMaterial = WandVisualMaterial;
                m_WandMeshRenderer.enabled = true;
            }
        }

        public void ToggleEraserStrokeVisibility()
        {
            m_EraserStrokesVisible = !m_EraserStrokesVisible;
            m_EraserMaterial.SetFloat("_ShowVisible", m_EraserStrokesVisible ? 1f : 0f);
        }

        public void Undo()
        {
            if (m_UseWand && m_WandPoints.Count > 0)
            {
                // Tear down snap highlight so Update() recomputes fresh next frame.
                if (m_WandSnap == WandSnapState.NearFirstDot && m_WandDotObjects.Count > 0)
                    m_WandDotObjects[0].GetComponent<MeshRenderer>().sharedMaterial = WandVisualMaterial;
                else if (m_WandSnap == WandSnapState.NearExistingVertex)
                    m_WandSnapHighlight.SetActive(false);
                m_WandSnap = WandSnapState.None;
                m_WandSnapVertexIdx = -1;

                Vector3 removedPos = m_WandPoints[m_WandPoints.Count - 1];
                m_WandPoints.RemoveAt(m_WandPoints.Count - 1);
                Destroy(m_WandDotObjects[m_WandDotObjects.Count - 1]);
                m_WandDotObjects.RemoveAt(m_WandDotObjects.Count - 1);
                m_WandRedoPoints.Push(removedPos);

                if (m_WandPoints.Count == 0 && m_WandPreviewLine is not null)
                    m_WandPreviewLine.enabled = false;
                return;
            }
            // Moving to the main undo stack — discard wand dot redo history.
            m_WandRedoPoints.Clear();
            if (m_UndoStack.Count == 0) return;
            var op = m_UndoStack.Pop();
            op.Undo();
            m_RedoStack.Push(op);
        }

        public void Redo()
        {
            if (m_UseWand && m_WandRedoPoints.Count > 0)
            {
                Vector3 pos = m_WandRedoPoints.Pop();
                m_WandPoints.Add(pos);
                PlaceWandDot(pos);
                EnsureWandPreviewLine();
                return;
            }
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

        public void ClearArtworkUndoable()
        {
            ClearWandState();

            var clearedPaint = new List<GameObject>();
            for (int i = 0; i < m_ArtworkParentTransform.childCount; i++) {
                var child = m_ArtworkParentTransform.GetChild(i).gameObject;
                if (child.activeSelf) clearedPaint.Add(child);
            }

            var clearedEraser = new List<GameObject>();
            for (int i = 0; i < m_EraserParentTransform.childCount; i++) {
                var child = m_EraserParentTransform.GetChild(i).gameObject;
                if (child.activeSelf) clearedEraser.Add(child);
            }

            var savedPaintVerts  = new List<Vector3>(m_PolygonVerticesLocal);
            var savedEraserVerts = new List<Vector3>(m_EraserPolygonVerticesLocal);

            foreach (var obj in clearedPaint)  obj.SetActive(false);
            foreach (var obj in clearedEraser) obj.SetActive(false);
            m_PolygonVerticesLocal.Clear();
            m_EraserPolygonVerticesLocal.Clear();

            PushUndo(new ClearRecord(clearedPaint,  savedPaintVerts,  m_PolygonVerticesLocal,
                                     clearedEraser, savedEraserVerts, m_EraserPolygonVerticesLocal));
        }

        private void Update()
        {
            if (!m_UseWand) return;

            Vector3 tipWorld = m_WandTipTransform.position;

            // Keep the preview line showing the full outline so far, plus a live edge to the tip.
            if (m_WandPreviewLine is not null && m_WandPoints.Count > 0)
            {
                m_WandPreviewLine.positionCount = m_WandPoints.Count + 1;
                for (int i = 0; i < m_WandPoints.Count; i++)
                    m_WandPreviewLine.SetPosition(i, m_WandPoints[i]);
                m_WandPreviewLine.SetPosition(m_WandPoints.Count, tipWorld);
            }

            // Find the closest existing polygon vertex (paint or eraser) within threshold.
            int closestVertIdx = -1;
            bool closestFromEraser = false;
            float minDist = m_WandCloseThreshold;
            for (int i = 0; i < m_PolygonVerticesLocal.Count; i++)
            {
                float d = Vector3.Distance(tipWorld, m_ArtworkParentTransform.TransformPoint(m_PolygonVerticesLocal[i]));
                if (d < minDist) { minDist = d; closestVertIdx = i; closestFromEraser = false; }
            }
            for (int i = 0; i < m_EraserPolygonVerticesLocal.Count; i++)
            {
                float d = Vector3.Distance(tipWorld, m_EraserParentTransform.TransformPoint(m_EraserPolygonVerticesLocal[i]));
                if (d < minDist) { minDist = d; closestVertIdx = i; closestFromEraser = true; }
            }

            // Determine snap state. NearFirstDot takes priority when 3+ points are placed.
            WandSnapState newSnap = closestVertIdx >= 0 ? WandSnapState.NearExistingVertex : WandSnapState.None;
            int newSnapIdx = closestVertIdx;
            bool newSnapFromEraser = closestFromEraser;
            if (m_WandPoints.Count >= 3 && Vector3.Distance(tipWorld, m_WandPoints[0]) < m_WandCloseThreshold)
            {
                newSnap = WandSnapState.NearFirstDot;
                newSnapIdx = -1;
                newSnapFromEraser = false;
            }

            if (newSnap == m_WandSnap && newSnapIdx == m_WandSnapVertexIdx && newSnapFromEraser == m_WandSnapFromEraser)
            {
                // Keep snap highlight sphere in sync as artwork moves.
                if (m_WandSnap == WandSnapState.NearExistingVertex)
                    m_WandSnapHighlight.transform.position = SnapVertexWorldPos(m_WandSnapVertexIdx, m_WandSnapFromEraser);
                return;
            }

            // Tear down old highlight.
            if (m_WandSnap == WandSnapState.NearFirstDot && m_WandDotObjects.Count > 0)
                m_WandDotObjects[0].GetComponent<MeshRenderer>().sharedMaterial = WandVisualMaterial;
            else if (m_WandSnap == WandSnapState.NearExistingVertex)
                m_WandSnapHighlight.SetActive(false);

            m_WandSnap = newSnap;
            m_WandSnapVertexIdx = newSnapIdx;
            m_WandSnapFromEraser = newSnapFromEraser;

            // Apply new highlight.
            if (m_WandSnap == WandSnapState.NearFirstDot && m_WandDotObjects.Count > 0)
                m_WandDotObjects[0].GetComponent<MeshRenderer>().sharedMaterial = m_WandCloseHighlightMat;
            else if (m_WandSnap == WandSnapState.NearExistingVertex)
            {
                m_WandSnapHighlight.transform.position = SnapVertexWorldPos(m_WandSnapVertexIdx, m_WandSnapFromEraser);
                m_WandSnapHighlight.SetActive(true);
            }
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
            Debug.Assert(m_EraserParentTransform != null);
            Debug.Assert(m_BrushCursorTransform != null);
            Debug.Assert(m_BrushCursorMeshRenderer != null);
            Debug.Assert(m_HandCursorTransform != null);
            Debug.Assert(m_PaintMaterial != null);
            Debug.Assert(m_EraserMaterial != null);
            Debug.Assert(m_EraserCursorMaterial != null);
            Debug.Assert(m_WandMeshRenderer != null);
            Debug.Assert(m_WandTipTransform != null);

            m_SavedBrushCursorMaterial = m_BrushCursorMeshRenderer.sharedMaterial;
            m_WandDrawMat = new Material(m_SavedBrushCursorMaterial);
            m_WandDrawMat.color = m_BrushColor;
            m_WandMeshRenderer.sharedMaterial = m_EraserCursorMaterial;
            m_WandMeshRenderer.enabled = false;
            m_WandCloseHighlightMat = new Material(m_EraserCursorMaterial);
            m_WandCloseHighlightMat.SetColor("_GlowColor", Color.white);

            m_WandSnapHighlight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m_WandSnapHighlight.transform.localScale = Vector3.one * 0.025f;
            m_WandSnapHighlight.GetComponent<MeshRenderer>().sharedMaterial = m_WandCloseHighlightMat;
            Destroy(m_WandSnapHighlight.GetComponent<SphereCollider>());
            m_WandSnapHighlight.SetActive(false);

            if (m_BrushModeCondition != null)
                m_BrushModeCondition.isTrue = true;
            m_NumStrokes = 0;
        }


        // PAINTING STATE CALLBACKS

        public void Painting_OnEnter()
        {
            if (m_UseWand)
            {
                switch (m_WandSnap)
                {
                    case WandSnapState.NearFirstDot:
                        CommitWandPolygon();
                        ClearWandState();
                        break;

                    case WandSnapState.NearExistingVertex:
                        Vector3 snapPos = SnapVertexWorldPos(m_WandSnapVertexIdx, m_WandSnapFromEraser);
                        if (m_WandPoints.Count >= 2)
                        {
                            // Close shape to this existing vertex.
                            m_WandPoints.Add(snapPos);
                            CommitWandPolygon();
                            ClearWandState();
                        }
                        else
                        {
                            // Start a new shape anchored at this existing vertex.
                            m_WandRedoPoints.Clear();
                            m_WandPoints.Add(snapPos);
                            PlaceWandDot(snapPos);
                            EnsureWandPreviewLine();
                        }
                        break;

                    default:
                        m_WandRedoPoints.Clear();
                        if (m_WandPoints.Count == 0) m_RedoStack.Clear();
                        m_WandPoints.Add(m_WandTipTransform.position);
                        PlaceWandDot(m_WandTipTransform.position);
                        EnsureWandPreviewLine();
                        break;
                }
                return;
            }

            // create a new GameObject to hold the new paint stroke
            m_CurrentStrokeObj = new GameObject("Stroke " + m_NumStrokes);
            m_CurrentStrokeObj.transform.SetParent(m_EraseMode ? m_EraserParentTransform : m_ArtworkParentTransform, false);

            // normals can get weird when using two-sided rendering, and Unity's standard shaders do not support it.
            // but we would like to see both sides of the ribbons we paint.  so, the solution is to create two meshes
            // one to draw the "front" side of the ribbon and one to draw the "back" side of the ribbon.  the only
            // change between front and back is swapping the vertex ordering of each triangle.
            GameObject frontMeshObj = new GameObject("FrontMesh", typeof(MeshFilter), typeof(MeshRenderer));
            frontMeshObj.transform.SetParent(m_CurrentStrokeObj.transform, false);
            MeshRenderer frontMeshRenderer = frontMeshObj.GetComponent<MeshRenderer>();
            Material strokeMaterial;
            if (m_EraseMode) {
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
            if (m_UseWand) return;

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
            if (m_UseWand) return;
            PushUndo(new StrokeRecord(m_CurrentStrokeObj));
            m_NumStrokes++;
        }


        // TRANS-ROT-ARTWORK STATE CALLBACKS

        public void TransRotArtwork_OnEnter()
        {
            ClearWandState();
            m_ArtworkSnapshotBefore = TransformSnapshot.Capture(m_ArtworkParentTransform);
            m_EraserSnapshotBefore  = TransformSnapshot.Capture(m_EraserParentTransform);
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
            if (m_EraseMode)
            {
                m_EraserParentTransform.TranslateByWorldVector(deltaPosWorld);
                m_EraserParentTransform.RotateAroundWorldPoint(handPosWorld, deltaRotWorld);
            }

            m_LastHandPos = handPosWorld;
            m_LastHandRot = handRotWorld;
        }

        public void TransRotArtwork_OnExit()
        {
            if (m_EraseMode)
                PushUndo(new TransformRecord(m_ArtworkParentTransform, m_ArtworkSnapshotBefore, TransformSnapshot.Capture(m_ArtworkParentTransform),
                                             m_EraserParentTransform,  m_EraserSnapshotBefore,  TransformSnapshot.Capture(m_EraserParentTransform)));
            else
                PushUndo(new TransformRecord(m_ArtworkParentTransform, m_ArtworkSnapshotBefore, TransformSnapshot.Capture(m_ArtworkParentTransform)));
        }


        // SCALE-ARTWORK STATE CALLBACKS

        public void ScaleArtwork_OnEnter()
        {
            ClearWandState();
            m_ArtworkSnapshotBefore = TransformSnapshot.Capture(m_ArtworkParentTransform);
            m_EraserSnapshotBefore  = TransformSnapshot.Capture(m_EraserParentTransform);
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
            if (m_EraseMode)
                m_EraserParentTransform.ScaleAroundWorldPoint(handPosWorld, deltaScale);

            m_LastHandPos = handPosWorld;
            m_LastBrushPos = brushPosWorld;
        }

        public void ScaleArtwork_OnExit()
        {
            if (m_EraseMode)
                PushUndo(new TransformRecord(m_ArtworkParentTransform, m_ArtworkSnapshotBefore, TransformSnapshot.Capture(m_ArtworkParentTransform),
                                             m_EraserParentTransform,  m_EraserSnapshotBefore,  TransformSnapshot.Capture(m_EraserParentTransform)));
            else
                PushUndo(new TransformRecord(m_ArtworkParentTransform, m_ArtworkSnapshotBefore, TransformSnapshot.Capture(m_ArtworkParentTransform)));
        }


        [Tooltip("Parent Transform for any 3D geometry produced by painting.")]
        [SerializeField] private Transform m_ArtworkParentTransform;
        [Tooltip("Parent Transform for eraser geometry — stays fixed when only paint is repositioned.")]
        [SerializeField] private Transform m_EraserParentTransform;
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

        [Tooltip("Mesh renderer for the wand cylinder shown in eraser-wand mode.")]
        [SerializeField] private MeshRenderer m_WandMeshRenderer;
        [Tooltip("Empty transform at the tip of the wand used to read the tip position.")]
        [SerializeField] private Transform m_WandTipTransform;

        [Tooltip("World-space radius within which the wand tip snaps to the first dot to close the shape.")]
        [SerializeField] private float m_WandCloseThreshold = 0.05f;

        [Tooltip("The current brush color.")]
        [SerializeField] private Color m_BrushColor;

        [Tooltip("Condition to set false when in wand mode, for use as an FSM arc guard.")]
        [SerializeField] private Condition m_BrushModeCondition;


        // runtime only

        // for tool mode
        private bool m_UseWand = false;
        private bool m_EraseMode = false;
        private bool m_EraserStrokesVisible = false;
        private Material m_SavedBrushCursorMaterial;
        private Material m_WandDrawMat;

        // for eraser wand
        private readonly List<Vector3> m_WandPoints = new List<Vector3>();
        private readonly List<GameObject> m_WandDotObjects = new List<GameObject>();
        private readonly List<Vector3> m_PolygonVerticesLocal = new List<Vector3>();
        private readonly List<Vector3> m_EraserPolygonVerticesLocal = new List<Vector3>();
        private readonly Stack<Vector3> m_WandRedoPoints = new Stack<Vector3>();
        private LineRenderer m_WandPreviewLine;
        private Material m_WandCloseHighlightMat;
        private GameObject m_WandSnapHighlight;
        private WandSnapState m_WandSnap = WandSnapState.None;
        private int m_WandSnapVertexIdx = -1;
        private bool m_WandSnapFromEraser = false;

        public bool IsWandMode => m_UseWand;

        public Vector3 WandTipPosition => m_WandTipTransform.position;

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
        private TransformSnapshot m_EraserSnapshotBefore;


        // ERASER WAND HELPERS

        private void PlaceWandDot(Vector3 worldPos)
        {
            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.transform.SetParent(m_ArtworkParentTransform, true);
            dot.transform.position = worldPos;
            dot.transform.localScale = Vector3.one * 0.02f;
            dot.GetComponent<MeshRenderer>().sharedMaterial = WandVisualMaterial;
            Destroy(dot.GetComponent<SphereCollider>());
            m_WandDotObjects.Add(dot);
        }

        private void EnsureWandPreviewLine()
        {
            if (m_WandPreviewLine == null)
            {
                var lineObj = new GameObject("WandPreviewLine");
                lineObj.transform.SetParent(transform, false);
                m_WandPreviewLine = lineObj.AddComponent<LineRenderer>();
                m_WandPreviewLine.startWidth = 0.003f;
                m_WandPreviewLine.endWidth = 0.003f;
                m_WandPreviewLine.positionCount = 2;
                m_WandPreviewLine.useWorldSpace = true;
                m_WandPreviewLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                m_WandPreviewLine.receiveShadows = false;
            }
            m_WandPreviewLine.material = WandVisualMaterial;
            m_WandPreviewLine.enabled = true;
        }

        private Vector3 SnapVertexWorldPos(int idx, bool fromEraser) =>
            fromEraser
                ? m_EraserParentTransform.TransformPoint(m_EraserPolygonVerticesLocal[idx])
                : m_ArtworkParentTransform.TransformPoint(m_PolygonVerticesLocal[idx]);

        private void CommitWandPolygon()
        {
            var polyObj = new GameObject((m_EraseMode ? "EraserPolygon " : "PaintPolygon ") + m_NumStrokes);
            Transform polyParent = m_EraseMode ? m_EraserParentTransform : m_ArtworkParentTransform;
            polyObj.transform.SetParent(polyParent, false);

            Vector3[] verts = new Vector3[m_WandPoints.Count];
            for (int i = 0; i < m_WandPoints.Count; i++)
                verts[i] = polyObj.transform.InverseTransformPoint(m_WandPoints[i]);

            // Fan triangulation from vertex 0: works correctly for convex polygons.
            int triCount = m_WandPoints.Count - 2;
            int[] frontTris = new int[triCount * 3];
            int[] backTris  = new int[triCount * 3];
            for (int i = 0; i < triCount; i++)
            {
                frontTris[i * 3]     = 0;
                frontTris[i * 3 + 1] = i + 1;
                frontTris[i * 3 + 2] = i + 2;
                backTris[i * 3]      = 0;
                backTris[i * 3 + 1]  = i + 2;
                backTris[i * 3 + 2]  = i + 1;
            }

            Material polygonMat;
            if (m_EraseMode)
                polygonMat = m_EraserMaterial;
            else
            {
                polygonMat = new Material(m_PaintMaterial);
                polygonMat.color = m_BrushColor;
            }
            BuildPolygonMesh(polyObj, "FrontMesh", verts, frontTris, polygonMat);
            BuildPolygonMesh(polyObj, "BackMesh",  verts, backTris,  polygonMat);

            // Store vertices in parent-local space so future snapping can find them.
            var vertList = m_EraseMode ? m_EraserPolygonVerticesLocal : m_PolygonVerticesLocal;
            var addedVerts = new List<Vector3>(m_WandPoints.Count);
            foreach (var pt in m_WandPoints)
            {
                var local = polyParent.InverseTransformPoint(pt);
                addedVerts.Add(local);
                vertList.Add(local);
            }

            PushUndo(new PolygonRecord(polyObj, vertList, addedVerts));
            m_NumStrokes++;
        }

        private void BuildPolygonMesh(GameObject parent, string name, Vector3[] verts, int[] tris, Material mat)
        {
            var obj = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            obj.transform.SetParent(parent.transform, false);
            obj.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var mesh = obj.GetComponent<MeshFilter>().mesh;
            mesh.vertices  = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private void ClearWandState()
        {
            m_WandRedoPoints.Clear();
            if (m_WandSnap == WandSnapState.NearFirstDot && m_WandDotObjects.Count > 0)
                m_WandDotObjects[0].GetComponent<MeshRenderer>().sharedMaterial = WandVisualMaterial;
            else if (m_WandSnap == WandSnapState.NearExistingVertex && m_WandSnapHighlight != null)
                m_WandSnapHighlight.SetActive(false);

            m_WandSnap = WandSnapState.None;
            m_WandSnapVertexIdx = -1;
            m_WandPoints.Clear();
            foreach (var dot in m_WandDotObjects)
                Destroy(dot);
            m_WandDotObjects.Clear();
            if (m_WandPreviewLine is not null)
                m_WandPreviewLine.enabled = false;
        }


        // TYPES

        private enum WandSnapState { None, NearFirstDot, NearExistingVertex }


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

        private class PolygonRecord : IUndoable
        {
            readonly GameObject m_Polygon;
            readonly List<Vector3> m_SharedList;
            readonly List<Vector3> m_AddedVerts;

            internal PolygonRecord(GameObject polygon, List<Vector3> sharedList, List<Vector3> addedVerts)
            {
                m_Polygon    = polygon;
                m_SharedList = sharedList;
                m_AddedVerts = addedVerts;
            }

            public void Undo()
            {
                m_Polygon.SetActive(false);
                m_SharedList.RemoveRange(m_SharedList.Count - m_AddedVerts.Count, m_AddedVerts.Count);
            }

            public void Redo()
            {
                m_Polygon.SetActive(true);
                m_SharedList.AddRange(m_AddedVerts);
            }
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
            readonly Transform m_Target2;
            readonly TransformSnapshot m_Before2, m_After2;

            internal TransformRecord(Transform target, TransformSnapshot before, TransformSnapshot after,
                                     Transform target2 = null, TransformSnapshot before2 = default, TransformSnapshot after2 = default)
            {
                m_Target  = target;  m_Before  = before;  m_After  = after;
                m_Target2 = target2; m_Before2 = before2; m_After2 = after2;
            }

            public void Undo()
            {
                m_Before.ApplyTo(m_Target);
                if (m_Target2 != null) m_Before2.ApplyTo(m_Target2);
            }
            public void Redo()
            {
                m_After.ApplyTo(m_Target);
                if (m_Target2 != null) m_After2.ApplyTo(m_Target2);
            }
        }

        private class ClearRecord : IUndoable
        {
            readonly List<GameObject> m_ClearedPaint;
            readonly List<Vector3> m_SavedPaintVerts;
            readonly List<Vector3> m_SharedPaintVerts;
            readonly List<GameObject> m_ClearedEraser;
            readonly List<Vector3> m_SavedEraserVerts;
            readonly List<Vector3> m_SharedEraserVerts;

            internal ClearRecord(List<GameObject> clearedPaint,  List<Vector3> savedPaintVerts,  List<Vector3> sharedPaintVerts,
                                  List<GameObject> clearedEraser, List<Vector3> savedEraserVerts, List<Vector3> sharedEraserVerts)
            {
                m_ClearedPaint = clearedPaint;  m_SavedPaintVerts  = savedPaintVerts;  m_SharedPaintVerts  = sharedPaintVerts;
                m_ClearedEraser = clearedEraser; m_SavedEraserVerts = savedEraserVerts; m_SharedEraserVerts = sharedEraserVerts;
            }

            public void Undo()
            {
                foreach (var obj in m_ClearedPaint)  if (obj) obj.SetActive(true);
                foreach (var obj in m_ClearedEraser) if (obj) obj.SetActive(true);
                m_SharedPaintVerts.InsertRange(0, m_SavedPaintVerts);
                m_SharedEraserVerts.InsertRange(0, m_SavedEraserVerts);
            }

            public void Redo()
            {
                foreach (var obj in m_ClearedPaint)  if (obj) obj.SetActive(false);
                foreach (var obj in m_ClearedEraser) if (obj) obj.SetActive(false);
                m_SharedPaintVerts.RemoveRange(0, m_SavedPaintVerts.Count);
                m_SharedEraserVerts.RemoveRange(0, m_SavedEraserVerts.Count);
            }
        }
    }

} // namespace