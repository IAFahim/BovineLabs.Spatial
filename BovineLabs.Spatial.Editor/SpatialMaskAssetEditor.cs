#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BovineLabs.Spatial.Authoring.Editor
{
    [CustomEditor(typeof(SpatialMaskAsset))]
    public sealed class SpatialMaskAssetEditor : UnityEditor.Editor
    {
        private const float CellGap = 1f;
        private const int CellMin = 18;
        private const int CellMax = 24;
        private const float LabelWidth = 48f;
        private const float Gap = 3f;
        private const string ClipboardHeader = "SPATIAL_MASK";

        private static readonly int[] Sizes = { 1, 3, 5, 7, 9, 11, 15 };
        private static readonly string[] WriteModeNames = Enum.GetNames(typeof(SpatialMaskWriteMode));
        private static readonly string[] SizeNames = { "1x1", "3x3", "5x5", "7x7", "9x9", "11x11", "15x15" };
        private readonly List<int> orderedSelection = new();

        private readonly HashSet<int> selection = new();
        private int anchorX;
        private int anchorY;
        private SpatialMaskAsset asset;
        private int brush = 1;
        private bool dataExpanded = true;
        private Vector2 dataScroll;
        private Rect gridRect;
        private SpatialMaskShape shape = SpatialMaskShape.ForwardCone;
        private SpatialMaskWriteMode writeMode = SpatialMaskWriteMode.Replace;

        private static bool IsDark => EditorGUIUtility.isProSkin;
        private bool HasSelection => selection.Count != 0;

        private void OnEnable()
        {
            asset = (SpatialMaskAsset)target;
            SelectOnly(asset.CenterX, asset.CenterY);
        }

        public override void OnInspectorGUI()
        {
            asset = (SpatialMaskAsset)target;
            serializedObject.Update();

            DrawUnityProperties();
            DrawStatus();
            DrawBrush();
            DrawShape();
            DrawGrid(Event.current);
            DrawSelectionEdit();
            DrawTransform();
            DrawSize();
            DrawData();

            if (TryHandleKeyboard(Event.current))
                Repaint();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawUnityProperties()
        {
            var id = serializedObject.FindProperty("id");
            if (id != null)
                EditorGUILayout.PropertyField(id);
        }

        private void DrawStatus()
        {
            var stats = SpatialMaskStats.From(asset);
            var selected = HasSelection ? $"Sel {selection.Count}  Sum {FormatSigned(SelectionSum())}" : "Sel none";
            EditorGUILayout.LabelField(
                $"{asset.Width}x{asset.Height}  Center ({asset.CenterX},{asset.CenterY})  Active {stats.Active}/{stats.Count}  Sum {FormatSigned(stats.Sum)}  Min {FormatSigned(stats.Min)}  Max {FormatSigned(stats.Max)}",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"{selected}  Brush {FormatSigned(brush)}  Shape {shape}  {writeMode}",
                EditorStyles.miniLabel);
        }

        private void DrawBrush()
        {
            var row = Row();
            Label(ref row, "Brush");

            EditorGUI.BeginChangeCheck();
            brush = EditorGUI.IntField(Slice(ref row, 50f), brush);
            if (EditorGUI.EndChangeCheck())
                brush = ClampToSByte(brush);

            if (Button(ref row, "-5", 28f, EditorStyles.miniButtonLeft))
                brush = ClampToSByte(brush - 5);

            if (Button(ref row, "-1", 28f, EditorStyles.miniButtonMid))
                brush = ClampToSByte(brush - 1);

            if (Button(ref row, "0", 24f, EditorStyles.miniButtonMid))
                brush = 0;

            if (Button(ref row, "+1", 28f, EditorStyles.miniButtonMid))
                brush = ClampToSByte(brush + 1);

            if (Button(ref row, "+5", 28f, EditorStyles.miniButtonMid))
                brush = ClampToSByte(brush + 5);

            if (Button(ref row, "Flip", 36f, EditorStyles.miniButtonRight))
                brush = ClampToSByte(-brush);
        }

        private void DrawShape()
        {
            var row = Row();
            Label(ref row, "Shape");

            var buttonWidth = 48f;
            var modeWidth = 104f;
            var popupWidth = Mathf.Max(72f, row.width - modeWidth - buttonWidth - Gap * 2f);

            EditorGUI.BeginChangeCheck();
            shape = (SpatialMaskShape)EditorGUI.EnumPopup(Slice(ref row, popupWidth), shape);
            if (EditorGUI.EndChangeCheck())
                Repaint();

            writeMode = (SpatialMaskWriteMode)GUI.Toolbar(Slice(ref row, modeWidth), (int)writeMode, WriteModeNames,
                EditorStyles.miniButton);

            if (Button(ref row, "Apply", buttonWidth, EditorStyles.miniButton))
                ApplyShape();
        }

        private void DrawGrid(Event evt)
        {
            var cellSize = CellSize();
            var width = asset.Width * cellSize + Mathf.Max(0, asset.Width - 1) * CellGap;
            var height = asset.Height * cellSize + Mathf.Max(0, asset.Height - 1) * CellGap;

            var forward = GUILayoutUtility.GetRect(width, 14f, GUILayout.ExpandWidth(true));
            forward.x = Mathf.Floor((EditorGUIUtility.currentViewWidth - width) * 0.5f);
            forward.width = width;
            GUI.Label(forward, "Forward ▲", EditorStyles.centeredGreyMiniLabel);

            gridRect = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(true));
            gridRect.x = Mathf.Floor((EditorGUIUtility.currentViewWidth - width) * 0.5f);
            gridRect.width = width;
            gridRect.height = height;

            if (TryHandleGridScroll(evt))
                return;

            for (var y = 0; y < asset.Height; y++)
            for (var x = 0; x < asset.Width; x++)
            {
                var rect = CellRect(x, y, cellSize);
                DrawCell(rect, x, y, evt);
            }
        }

        private void DrawCell(Rect rect, int x, int y, Event evt)
        {
            var value = asset.Get(x, y);
            var center = x == asset.CenterX && y == asset.CenterY;
            var axis = x == asset.CenterX || y == asset.CenterY;
            var selected = IsSelected(x, y);
            var hover = rect.Contains(evt.mousePosition);
            var preview = TryGetShapeWeight(x, y, out var previewValue) && previewValue != 0;

            EditorGUI.DrawRect(rect, CellBackground(value, preview));

            if (preview)
                DrawBorder(rect, PreviewBorder(), 1);

            if (axis)
                DrawBorder(rect, AxisBorder(), 1);

            if (center)
                DrawCenter(rect);

            if (hover)
                DrawBorder(rect, HoverBorder(), 2);

            if (selected)
                DrawBorder(rect, SelectionBorder(), 2);

            GUI.Label(rect, FormatSigned(value), CellTextStyle(value, rect.height));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Arrow);

            if (evt.type != EventType.MouseDown || !hover)
                return;

            if (evt.button == 0)
            {
                HandleSelectionClick(x, y, evt);
                evt.Use();
                return;
            }

            if (evt.button == 1)
            {
                if (!selected)
                    SelectOnly(x, y);

                ShowContextMenu(x, y);
                evt.Use();
            }
        }

        private void DrawSelectionEdit()
        {
            EditorGUILayout.LabelField("Selection", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(!HasSelection))
            {
                var row = Row();
                Label(ref row, "Value");

                EditorGUI.BeginChangeCheck();
                var value = EditorGUI.IntField(Slice(ref row, 54f), FirstSelectedValue());
                if (EditorGUI.EndChangeCheck())
                    SetSelection(value, "Set Spatial Mask Selection");

                if (Button(ref row, "Brush", 48f, EditorStyles.miniButtonLeft))
                    SetSelection(brush, "Paint Spatial Mask Selection");

                if (Button(ref row, "+", 26f, EditorStyles.miniButtonMid))
                    AddSelection(1, "Increment Spatial Mask Selection");

                if (Button(ref row, "-", 26f, EditorStyles.miniButtonMid))
                    AddSelection(-1, "Decrement Spatial Mask Selection");

                if (Button(ref row, "Clear", 44f, EditorStyles.miniButtonMid))
                    SetSelection(0, "Clear Spatial Mask Selection");

                if (Button(ref row, "Copy", 42f, EditorStyles.miniButtonMid))
                    CopySelection();

                if (Button(ref row, "Paste", 44f, EditorStyles.miniButtonRight))
                    PasteFromClipboard();
            }

            var all = Row();
            Label(ref all, "All");

            if (Button(ref all, "Select All", 70f, EditorStyles.miniButtonLeft))
                SelectAll();

            if (Button(ref all, "Clear All", 70f, EditorStyles.miniButtonRight))
                ApplyChange("Clear Spatial Mask", asset.Clear);
        }

        private void DrawTransform()
        {
            var row = Row();
            Label(ref row, "Move");

            if (Button(ref row, "Mirror X", 68f, EditorStyles.miniButtonLeft))
                ApplyChange("Mirror Spatial Mask X", asset.MirrorX);

            if (Button(ref row, "Mirror Y", 68f, EditorStyles.miniButtonMid))
                ApplyChange("Mirror Spatial Mask Y", asset.MirrorY);

            using (new EditorGUI.DisabledScope(asset.Width != asset.Height))
            {
                if (Button(ref row, "Rotate 90°", 78f, EditorStyles.miniButtonRight))
                    ApplyChange("Rotate Spatial Mask", asset.RotateClockwise);
            }
        }

        private void DrawSize()
        {
            var row = Row();
            Label(ref row, "Size");

            var index = SizeIndex(asset.Width, asset.Height);
            var next = GUI.Toolbar(row, index, SizeNames, EditorStyles.miniButton);

            if (next == index || next < 0 || next >= Sizes.Length)
                return;

            var size = Sizes[next];
            ApplyChange($"Resize Spatial Mask {size}x{size}", () => asset.ResizeCentered(size, size));
        }

        private void DrawData()
        {
            dataExpanded = EditorGUILayout.Foldout(dataExpanded, "Data", true);
            if (!dataExpanded)
                return;

            var cellWidth = 42f;
            var rowHeight = EditorGUIUtility.singleLineHeight + 2f;
            var totalWidth = LabelWidth + asset.Width * cellWidth + 8f;
            var totalHeight = (asset.Height + 1) * rowHeight;
            var viewHeight = Mathf.Min(180f, totalHeight + 4f);
            var outer = GUILayoutUtility.GetRect(0f, viewHeight, GUILayout.ExpandWidth(true));
            var view = new Rect(0f, 0f, totalWidth, totalHeight);

            dataScroll = GUI.BeginScrollView(outer, dataScroll, view, false, true);

            var header = new Rect(0f, 0f, LabelWidth, rowHeight);
            GUI.Label(header, "x/y", EditorStyles.miniBoldLabel);

            for (var x = 0; x < asset.Width; x++)
            {
                var rect = new Rect(LabelWidth + x * cellWidth, 0f, cellWidth, rowHeight);
                GUI.Label(rect, (x - asset.CenterX).ToString(), EditorStyles.centeredGreyMiniLabel);
            }

            for (var y = 0; y < asset.Height; y++)
            {
                var yRect = new Rect(0f, (y + 1) * rowHeight, LabelWidth, rowHeight);
                GUI.Label(yRect, (asset.CenterY - y).ToString(), EditorStyles.centeredGreyMiniLabel);

                for (var x = 0; x < asset.Width; x++)
                {
                    var rect = new Rect(LabelWidth + x * cellWidth, (y + 1) * rowHeight, cellWidth - 2f, rowHeight);
                    DrawDataCell(rect, x, y);
                }
            }

            GUI.EndScrollView();
        }

        private void DrawDataCell(Rect rect, int x, int y)
        {
            if (IsSelected(x, y))
                EditorGUI.DrawRect(rect, SelectionFill());

            EditorGUI.BeginChangeCheck();
            var value = EditorGUI.IntField(rect, asset.Get(x, y));
            if (EditorGUI.EndChangeCheck())
            {
                SelectOnly(x, y);
                SetCell(x, y, value, "Set Spatial Mask Cell");
            }
        }

        private bool TryHandleKeyboard(Event evt)
        {
            if (evt.type != EventType.KeyDown)
                return false;

            if (evt.control || evt.command)
            {
                if (evt.keyCode == KeyCode.C)
                {
                    CopySelection();
                    evt.Use();
                    return true;
                }

                if (evt.keyCode == KeyCode.V)
                {
                    PasteFromClipboard();
                    evt.Use();
                    return true;
                }

                if (evt.keyCode == KeyCode.A)
                {
                    SelectAll();
                    evt.Use();
                    return true;
                }
            }

            if (!HasSelection)
                return false;

            switch (evt.keyCode)
            {
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    SetSelection(0, "Clear Spatial Mask Selection");
                    evt.Use();
                    return true;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    SetSelection(brush, "Paint Spatial Mask Selection");
                    evt.Use();
                    return true;

                case KeyCode.LeftArrow:
                    MoveSelection(-1, 0, evt.shift);
                    evt.Use();
                    return true;

                case KeyCode.RightArrow:
                    MoveSelection(1, 0, evt.shift);
                    evt.Use();
                    return true;

                case KeyCode.UpArrow:
                    MoveSelection(0, -1, evt.shift);
                    evt.Use();
                    return true;

                case KeyCode.DownArrow:
                    MoveSelection(0, 1, evt.shift);
                    evt.Use();
                    return true;

                default:
                    return false;
            }
        }

        private bool TryHandleGridScroll(Event evt)
        {
            if (evt.type != EventType.ScrollWheel || !gridRect.Contains(evt.mousePosition))
                return false;

            if (!TryGetCellAt(evt.mousePosition, out var x, out var y))
                return false;

            var delta = evt.delta.y < 0f ? 1 : -1;
            if (evt.shift)
                delta *= 5;
            if (evt.control || evt.command)
                delta *= 10;

            if (!IsSelected(x, y))
                SelectOnly(x, y);

            AddSelection(delta, "Scroll Spatial Mask Selection");
            evt.Use();
            return true;
        }

        private void HandleSelectionClick(int x, int y, Event evt)
        {
            if (evt.shift)
            {
                SelectRange(anchorX, anchorY, x, y);
                return;
            }

            if (evt.control || evt.command)
            {
                ToggleSelection(x, y);
                anchorX = x;
                anchorY = y;
                return;
            }

            SelectOnly(x, y);
        }

        private void ShowContextMenu(int x, int y)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Set Brush"), false, () => SetSelection(brush, "Paint Spatial Mask Selection"));
            menu.AddItem(new GUIContent("Add Brush"), false, () => AddSelection(brush, "Add Spatial Mask Brush"));
            menu.AddItem(new GUIContent("Clear"), false, () => SetSelection(0, "Clear Spatial Mask Selection"));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Pick Brush"), false, () => PickBrush(x, y));
            menu.AddItem(new GUIContent("Copy"), false, CopySelection);
            menu.AddItem(new GUIContent("Paste"), false, PasteFromClipboard);
            menu.ShowAsContext();
        }

        private void PickBrush(int x, int y)
        {
            brush = asset.Get(x, y);
            Repaint();
        }

        private void CopySelection()
        {
            if (!HasSelection)
                return;

            SyncOrderedSelection();
            var bounds = SelectionBounds();
            var lines = new List<string>
            {
                $"{ClipboardHeader}\t{bounds.width}\t{bounds.height}"
            };

            for (var i = 0; i < orderedSelection.Count; i++)
            {
                Decode(orderedSelection[i], out var x, out var y);
                lines.Add($"{x - bounds.x}\t{y - bounds.y}\t{asset.Get(x, y)}");
            }

            EditorGUIUtility.systemCopyBuffer = string.Join("\n", lines);
        }

        private void PasteFromClipboard()
        {
            var text = EditorGUIUtility.systemCopyBuffer;
            if (string.IsNullOrWhiteSpace(text))
                return;

            if (int.TryParse(text.Trim(), out var scalar))
            {
                SetSelection(scalar, "Paste Spatial Mask Value");
                return;
            }

            var lines = text.Replace("\r", string.Empty).Split('\n');
            if (lines.Length == 0 || !lines[0].StartsWith(ClipboardHeader, StringComparison.Ordinal))
                return;

            var anchor = HasSelection ? SelectionBounds() : new RectInt(asset.CenterX, asset.CenterY, 1, 1);
            Undo.RecordObject(asset, "Paste Spatial Mask Values");
            selection.Clear();

            for (var i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split('\t');
                if (parts.Length < 3)
                    continue;

                if (!int.TryParse(parts[0], out var rx) || !int.TryParse(parts[1], out var ry) ||
                    !int.TryParse(parts[2], out var value))
                    continue;

                var x = anchor.x + rx;
                var y = anchor.y + ry;
                if (!asset.IsInside(x, y))
                    continue;

                asset.Set(x, y, ClampToSByte(value));
                selection.Add(Key(x, y));
                anchorX = x;
                anchorY = y;
            }

            EditorUtility.SetDirty(asset);
            Repaint();
        }

        private void MoveSelection(int dx, int dy, bool extend)
        {
            var bounds = SelectionBounds();
            var x = Mathf.Clamp(bounds.x + dx, 0, asset.Width - 1);
            var y = Mathf.Clamp(bounds.y + dy, 0, asset.Height - 1);

            if (extend)
                SelectRange(anchorX, anchorY, x, y);
            else
                SelectOnly(x, y);
        }

        private void SelectOnly(int x, int y)
        {
            if (!asset.IsInside(x, y))
                return;

            selection.Clear();
            selection.Add(Key(x, y));
            anchorX = x;
            anchorY = y;
        }

        private void SelectAll()
        {
            selection.Clear();
            for (var y = 0; y < asset.Height; y++)
            for (var x = 0; x < asset.Width; x++)
                selection.Add(Key(x, y));

            anchorX = 0;
            anchorY = 0;
        }

        private void SelectRange(int ax, int ay, int bx, int by)
        {
            selection.Clear();

            var minX = Mathf.Min(ax, bx);
            var maxX = Mathf.Max(ax, bx);
            var minY = Mathf.Min(ay, by);
            var maxY = Mathf.Max(ay, by);

            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
                if (asset.IsInside(x, y))
                    selection.Add(Key(x, y));
        }

        private void ToggleSelection(int x, int y)
        {
            var key = Key(x, y);
            if (!selection.Remove(key))
                selection.Add(key);
        }

        private bool IsSelected(int x, int y)
        {
            return selection.Contains(Key(x, y));
        }

        private int FirstSelectedValue()
        {
            if (!TryGetFirstSelected(out var x, out var y))
                return 0;

            return asset.Get(x, y);
        }

        private int SelectionSum()
        {
            var sum = 0;
            foreach (var key in selection)
            {
                Decode(key, out var x, out var y);
                sum += asset.Get(x, y);
            }

            return sum;
        }

        private bool TryGetFirstSelected(out int x, out int y)
        {
            foreach (var key in selection)
            {
                Decode(key, out x, out y);
                return true;
            }

            x = 0;
            y = 0;
            return false;
        }

        private void SetSelection(int value, string undoName)
        {
            if (!HasSelection)
                return;

            Undo.RecordObject(asset, undoName);
            foreach (var key in selection)
            {
                Decode(key, out var x, out var y);
                asset.Set(x, y, ClampToSByte(value));
            }

            EditorUtility.SetDirty(asset);
            Repaint();
        }

        private void AddSelection(int delta, string undoName)
        {
            if (!HasSelection)
                return;

            Undo.RecordObject(asset, undoName);
            foreach (var key in selection)
            {
                Decode(key, out var x, out var y);
                asset.Add(x, y, delta);
            }

            EditorUtility.SetDirty(asset);
            Repaint();
        }

        private void SetCell(int x, int y, int value, string undoName)
        {
            if (!asset.IsInside(x, y))
                return;

            Undo.RecordObject(asset, undoName);
            asset.Set(x, y, ClampToSByte(value));
            EditorUtility.SetDirty(asset);
            Repaint();
        }

        private void ApplyShape()
        {
            Undo.RecordObject(asset, $"Apply Spatial Mask Shape {shape}");

            if (writeMode == SpatialMaskWriteMode.Replace)
                asset.Clear();

            for (var y = 0; y < asset.Height; y++)
            for (var x = 0; x < asset.Width; x++)
            {
                if (!TryGetShapeWeight(x, y, out var weight) || weight == 0)
                    continue;

                if (writeMode == SpatialMaskWriteMode.Add)
                    asset.Add(x, y, weight);
                else
                    asset.Set(x, y, ClampToSByte(weight));
            }

            EditorUtility.SetDirty(asset);
            Repaint();
        }

        private void ApplyChange(string undoName, Action change)
        {
            Undo.RecordObject(asset, undoName);
            change.Invoke();
            ClampSelectionToAsset();
            EditorUtility.SetDirty(asset);
            Repaint();
        }

        private void ClampSelectionToAsset()
        {
            orderedSelection.Clear();
            foreach (var key in selection)
            {
                Decode(key, out var x, out var y);
                x = Mathf.Clamp(x, 0, Mathf.Max(0, asset.Width - 1));
                y = Mathf.Clamp(y, 0, Mathf.Max(0, asset.Height - 1));
                orderedSelection.Add(Key(x, y));
            }

            selection.Clear();
            for (var i = 0; i < orderedSelection.Count; i++)
                selection.Add(orderedSelection[i]);

            if (!HasSelection)
                SelectOnly(asset.CenterX, asset.CenterY);
        }

        private bool TryGetShapeWeight(int x, int y, out int weight)
        {
            weight = 0;

            if (!asset.IsInside(x, y))
                return false;

            var offset = asset.GetOffset(x, y);
            var radius = Mathf.Min(asset.Width, asset.Height) / 2;
            weight = ShapeWeight(shape, offset.x, offset.y, radius, brush);
            return true;
        }

        private bool TryGetCellAt(Vector2 point, out int x, out int y)
        {
            var cellSize = CellSize();
            x = Mathf.FloorToInt((point.x - gridRect.x) / (cellSize + CellGap));
            y = Mathf.FloorToInt((point.y - gridRect.y) / (cellSize + CellGap));
            return asset.IsInside(x, y) && CellRect(x, y, cellSize).Contains(point);
        }

        private Rect CellRect(int x, int y, int cellSize)
        {
            return new Rect(
                gridRect.x + x * (cellSize + CellGap),
                gridRect.y + y * (cellSize + CellGap),
                cellSize,
                cellSize);
        }

        private RectInt SelectionBounds()
        {
            if (!TryGetFirstSelected(out var firstX, out var firstY))
                return new RectInt(asset.CenterX, asset.CenterY, 1, 1);

            var minX = firstX;
            var maxX = firstX;
            var minY = firstY;
            var maxY = firstY;

            foreach (var key in selection)
            {
                Decode(key, out var x, out var y);
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }

            return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private void SyncOrderedSelection()
        {
            orderedSelection.Clear();
            orderedSelection.AddRange(selection);
            orderedSelection.Sort();
        }

        private static int ShapeWeight(SpatialMaskShape shape, int dx, int dz, int radius, int baseValue)
        {
            radius = Mathf.Max(1, radius);

            var absX = Mathf.Abs(dx);
            var absZ = Mathf.Abs(dz);
            var max = Mathf.Max(absX, absZ);
            var manhattan = absX + absZ;
            var sqr = dx * dx + dz * dz;
            var sign = baseValue < 0 ? -1 : 1;
            var value = Mathf.Abs(baseValue);

            return shape switch
            {
                SpatialMaskShape.Point => dx == 0 && dz == 0 ? baseValue : 0,
                SpatialMaskShape.Surround => max == 1 ? value * sign : 0,
                SpatialMaskShape.Square => max <= radius ? Falloff(value, max, radius) * sign : 0,
                SpatialMaskShape.Diamond => manhattan <= radius ? Falloff(value, manhattan, radius) * sign : 0,
                SpatialMaskShape.Disc => sqr <= radius * radius
                    ? Falloff(value, Mathf.RoundToInt(Mathf.Sqrt(sqr)), radius) * sign
                    : 0,
                SpatialMaskShape.Ring => max == radius ? value * sign : 0,
                SpatialMaskShape.Cross => dx == 0 || dz == 0 ? Falloff(value, max, radius) * sign : 0,
                SpatialMaskShape.DiagonalCross => absX == absZ ? Falloff(value, max, radius) * sign : 0,
                SpatialMaskShape.ForwardLine => dx == 0 && dz > 0 ? Falloff(value, dz, radius) * sign : 0,
                SpatialMaskShape.ForwardCone => dz > 0 && absX <= dz ? Falloff(value, dz, radius) * sign : 0,
                SpatialMaskShape.ForwardWideCone => dz > 0 && absX <= dz + 1 ? Falloff(value, dz, radius) * sign : 0,
                SpatialMaskShape.Corridor => dz > 0 && absX <= 1 ? Falloff(value, dz, radius) * sign : 0,
                SpatialMaskShape.Wall => dz == 1 && absX <= radius ? value * sign : 0,
                SpatialMaskShape.RearCone => dz < 0 && absX <= -dz ? Falloff(value, -dz, radius) * sign : 0,
                SpatialMaskShape.LeftFlank => dx < 0 && absZ <= absX ? Falloff(value, absX, radius) * sign : 0,
                SpatialMaskShape.RightFlank => dx > 0 && absZ <= absX ? Falloff(value, absX, radius) * sign : 0,
                SpatialMaskShape.RepelRing => max == radius ? -value : 0,
                _ => 0
            };
        }

        private static int Falloff(int value, int distance, int radius)
        {
            if (value == 0)
                return 0;

            if (distance <= 0)
                return value;

            var t = 1f - Mathf.Clamp01((distance - 1f) / Mathf.Max(1f, radius));
            return Mathf.Max(1, Mathf.RoundToInt(value * t));
        }

        private int CellSize()
        {
            var available = Mathf.Max(100f, EditorGUIUtility.currentViewWidth - 26f);
            var fit = Mathf.FloorToInt(
                (available - Mathf.Max(0, asset.Width - 1) * CellGap) / Mathf.Max(1, asset.Width));
            return Mathf.Clamp(fit, CellMin, CellMax);
        }

        private Rect Row()
        {
            return EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
        }

        private static void Label(ref Rect row, string text)
        {
            var rect = Slice(ref row, LabelWidth);
            EditorGUI.LabelField(rect, text);
        }

        private static bool Button(ref Rect row, string text, float width, GUIStyle style)
        {
            return GUI.Button(Slice(ref row, width), text, style);
        }

        private static Rect Slice(ref Rect row, float width)
        {
            width = Mathf.Min(width, row.width);
            var rect = new Rect(row.x, row.y, width, row.height);
            row.x += width + Gap;
            row.width = Mathf.Max(0f, row.width - width - Gap);
            return rect;
        }

        private static int SizeIndex(int width, int height)
        {
            if (width != height)
                return -1;

            for (var i = 0; i < Sizes.Length; i++)
                if (Sizes[i] == width)
                    return i;

            return -1;
        }

        private static GUIStyle CellTextStyle(sbyte value, float height)
        {
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                wordWrap = false,
                fontSize = height <= 18 ? 8 : 9
            };

            style.normal.textColor = TextColor(value);
            return style;
        }

        private static string FormatSigned(int value)
        {
            return value > 0 ? $"+{value}" : value.ToString();
        }

        private static sbyte ClampToSByte(int value)
        {
            return (sbyte)Mathf.Clamp(value, sbyte.MinValue, sbyte.MaxValue);
        }

        private static int Key(int x, int y)
        {
            return x + y * 256;
        }

        private static void Decode(int key, out int x, out int y)
        {
            x = key & 255;
            y = key >> 8;
        }

        private static void DrawCenter(Rect rect)
        {
            DrawBorder(rect, CenterBorder(), 2);
            var size = Mathf.Max(3f, Mathf.Floor(rect.width * 0.2f));
            var dot = new Rect(rect.xMin + 3f, rect.yMin + 3f, size, size);
            EditorGUI.DrawRect(dot, CenterBorder());
        }

        private static void DrawBorder(Rect rect, Color color, int thickness)
        {
            for (var i = 0; i < thickness; i++)
            {
                EditorGUI.DrawRect(new Rect(rect.xMin + i, rect.yMin + i, rect.width - i * 2, 1), color);
                EditorGUI.DrawRect(new Rect(rect.xMin + i, rect.yMax - i - 1, rect.width - i * 2, 1), color);
                EditorGUI.DrawRect(new Rect(rect.xMin + i, rect.yMin + i, 1, rect.height - i * 2), color);
                EditorGUI.DrawRect(new Rect(rect.xMax - i - 1, rect.yMin + i, 1, rect.height - i * 2), color);
            }
        }

        private static Color CellBackground(sbyte value, bool preview)
        {
            if (value == 0)
                return preview ? PreviewFill() : EmptyFill();

            var band = Mathf.Clamp(Mathf.Abs(value) / 3, 0, 3);

            if (IsDark)
                return value > 0 ? Gray(3 + band) : Gray(2);

            return value > 0 ? Gray(8 - band) : Gray(9);
        }

        private static Color TextColor(sbyte value)
        {
            if (IsDark)
                return value == 0 ? Color.gray6 : Color.gray9;

            return value == 0 ? Color.gray4 : Color.black;
        }

        private static Color EmptyFill()
        {
            return IsDark ? Color.gray2 : Color.gray8;
        }

        private static Color PreviewFill()
        {
            return IsDark ? Color.gray3 : Color.gray7;
        }

        private static Color AxisBorder()
        {
            return IsDark ? Color.gray4 : Color.gray6;
        }

        private static Color CenterBorder()
        {
            return IsDark ? Color.white : Color.black;
        }

        private static Color HoverBorder()
        {
            return IsDark ? Color.gray9 : Color.gray1;
        }

        private static Color SelectionBorder()
        {
            return IsDark ? Color.white : Color.black;
        }

        private static Color SelectionFill()
        {
            return IsDark ? new Color(0.22f, 0.32f, 0.48f, 0.55f) : new Color(0.55f, 0.72f, 1f, 0.45f);
        }

        private static Color PreviewBorder()
        {
            return IsDark ? Color.gray6 : Color.gray4;
        }

        private static Color Gray(int value)
        {
            return value switch
            {
                <= 1 => Color.gray1,
                2 => Color.gray2,
                3 => Color.gray3,
                4 => Color.gray4,
                5 => Color.gray5,
                6 => Color.gray6,
                7 => Color.gray7,
                8 => Color.gray8,
                _ => Color.gray9
            };
        }

        private readonly struct SpatialMaskStats
        {
            public readonly int Count;
            public readonly int Active;
            public readonly int Sum;
            public readonly int Min;
            public readonly int Max;

            private SpatialMaskStats(int count, int active, int sum, int min, int max)
            {
                Count = count;
                Active = active;
                Sum = sum;
                Min = min;
                Max = max;
            }

            public static SpatialMaskStats From(SpatialMaskAsset asset)
            {
                var count = asset.Width * asset.Height;
                var active = 0;
                var sum = 0;
                var min = 0;
                var max = 0;
                var initialized = false;

                for (var y = 0; y < asset.Height; y++)
                for (var x = 0; x < asset.Width; x++)
                {
                    var value = asset.Get(x, y);
                    sum += value;

                    if (value != 0)
                        active++;

                    if (!initialized)
                    {
                        min = value;
                        max = value;
                        initialized = true;
                        continue;
                    }

                    min = Mathf.Min(min, value);
                    max = Mathf.Max(max, value);
                }

                return new SpatialMaskStats(count, active, sum, min, max);
            }
        }

        private enum SpatialMaskWriteMode
        {
            Replace,
            Add
        }

        private enum SpatialMaskShape
        {
            Point,
            Surround,
            Square,
            Diamond,
            Disc,
            Ring,
            Cross,
            DiagonalCross,
            ForwardLine,
            ForwardCone,
            ForwardWideCone,
            Corridor,
            Wall,
            RearCone,
            LeftFlank,
            RightFlank,
            RepelRing
        }
    }
}
#endif