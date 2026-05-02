#if UNITY_EDITOR
using System;
using BovineLabs.Spatial.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BovineLabs.Spatial.Authoring.Editor
{
    [CustomEditor(typeof(SpatialMaskAsset))]
    public sealed class SpatialMaskAssetEditor : UnityEditor.Editor
    {
        private const int CellSize = 34;
        private const int CellGap = 3;
        private const int PreviewSize = 5;
        private const int PreviewCellSize = 7;
        private const int PreviewGap = 2;

        private SpatialMaskAsset asset;

        private VisualElement root;
        private VisualElement gridRoot;
        private Label summaryLabel;
        private Label selectedLabel;
        private Label selectedHintLabel;
        private IntegerField selectedValueField;

        private Button replaceModeButton;
        private Button addModeButton;

        private int selectedValue = 1;
        private bool replaceShape = true;

        private bool IsDark => EditorGUIUtility.isProSkin;

        private void OnEnable()
        {
            asset = (SpatialMaskAsset)target;
        }

        public override VisualElement CreateInspectorGUI()
        {
            asset = (SpatialMaskAsset)target;

            root = new VisualElement();
            root.style.paddingTop = 10;
            root.style.paddingBottom = 12;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.backgroundColor = RootBackground();
            root.style.color = Text();

            root.Add(CreateHeader());
            root.Add(CreateBrushCard());
            root.Add(CreateCanvasCard());
            root.Add(CreateShapeCard());
            root.Add(CreateOperationsCard());
            root.Add(CreateFooter());

            RebuildGrid();
            UpdateSelectedLabel();
            UpdateShapeModeButtons();

            return root;
        }

        private VisualElement CreateHeader()
        {
            var card = CreateCard();

            var row = Row();
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.alignItems = Align.Center;

            var titleBox = new VisualElement();

            var title = Label("Spatial Mask", 16, FontStyle.Bold, Text());
            titleBox.Add(title);

            var subtitle = Label("Actor-local signed grid. Top is forward. Center is the anchor.", 11, FontStyle.Normal, SubText());
            subtitle.style.marginTop = 2;
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            titleBox.Add(subtitle);

            summaryLabel = Label("", 11, FontStyle.Bold, SubText());
            summaryLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            summaryLabel.style.marginLeft = 12;

            row.Add(titleBox);
            row.Add(summaryLabel);
            card.Add(row);

            return card;
        }

        private VisualElement CreateBrushCard()
        {
            var card = CreateCard();
            card.style.marginTop = 8;

            card.Add(SectionTitle("Brush"));

            var row = Row();
            row.style.alignItems = Align.Center;

            selectedLabel = Label("", 13, FontStyle.Bold, Text());
            selectedLabel.style.minWidth = 96;
            selectedLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            selectedLabel.style.paddingTop = 5;
            selectedLabel.style.paddingBottom = 5;
            selectedLabel.style.paddingLeft = 10;
            selectedLabel.style.paddingRight = 10;
            selectedLabel.style.borderTopLeftRadius = 999;
            selectedLabel.style.borderTopRightRadius = 999;
            selectedLabel.style.borderBottomLeftRadius = 999;
            selectedLabel.style.borderBottomRightRadius = 999;

            selectedValueField = new IntegerField();
            selectedValueField.value = selectedValue;
            selectedValueField.style.width = 74;
            selectedValueField.style.marginLeft = 8;
            selectedValueField.style.marginRight = 4;
            selectedValueField.style.color = Text();
            selectedValueField.RegisterValueChangedCallback(evt =>
            {
                SetSelectedValue(evt.newValue);
            });

            row.Add(selectedLabel);
            row.Add(selectedValueField);
            row.Add(Chip("-10", () => SetSelectedValue(selectedValue - 10)));
            row.Add(Chip("-5", () => SetSelectedValue(selectedValue - 5)));
            row.Add(Chip("-1", () => SetSelectedValue(selectedValue - 1)));
            row.Add(Chip("0", () => SetSelectedValue(0)));
            row.Add(Chip("+1", () => SetSelectedValue(selectedValue + 1)));
            row.Add(Chip("+5", () => SetSelectedValue(selectedValue + 5)));
            row.Add(Chip("+10", () => SetSelectedValue(selectedValue + 10)));
            row.Add(Chip("Flip", () => SetSelectedValue(-selectedValue)));

            card.Add(row);

            selectedHintLabel = Label("Right-click picks. Left-click pastes. Wheel adjusts. Shift-click diffuses. Alt-click erases.", 11, FontStyle.Normal, Muted());
            selectedHintLabel.style.marginTop = 7;
            selectedHintLabel.style.whiteSpace = WhiteSpace.Normal;
            card.Add(selectedHintLabel);

            return card;
        }

        private VisualElement CreateCanvasCard()
        {
            var card = CreateCard();
            card.style.marginTop = 8;

            var top = Row();
            top.style.justifyContent = Justify.SpaceBetween;
            top.style.alignItems = Align.Center;

            var title = SectionTitle("Canvas");
            var forward = Label("FORWARD ▲", 11, FontStyle.Bold, Accent());
            forward.style.unityTextAlign = TextAnchor.MiddleRight;

            top.Add(title);
            top.Add(forward);
            card.Add(top);

            gridRoot = new VisualElement();
            gridRoot.style.marginTop = 8;
            gridRoot.style.alignSelf = Align.FlexStart;
            card.Add(gridRoot);

            return card;
        }

        private VisualElement CreateShapeCard()
        {
            var card = CreateCard();
            card.style.marginTop = 8;

            var header = Row();
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;

            header.Add(SectionTitle("Shapes"));

            var modeRow = Row();
            modeRow.style.alignItems = Align.Center;

            replaceModeButton = Chip("Replace", () =>
            {
                replaceShape = true;
                UpdateShapeModeButtons();
            });

            addModeButton = Chip("Add", () =>
            {
                replaceShape = false;
                UpdateShapeModeButtons();
            });

            modeRow.Add(replaceModeButton);
            modeRow.Add(addModeButton);
            header.Add(modeRow);

            card.Add(header);

            var hint = Label("Click a shape to stamp it using the selected value. Negative selected values automatically become repel masks.", 11, FontStyle.Normal, Muted());
            hint.style.marginTop = 2;
            hint.style.marginBottom = 8;
            hint.style.whiteSpace = WhiteSpace.Normal;
            card.Add(hint);

            AddShapeGroup(card, "Core", new[]
            {
                new ShapePreset(SpatialMaskShape.Point, "Point"),
                new ShapePreset(SpatialMaskShape.Surround, "Surround"),
                new ShapePreset(SpatialMaskShape.Square, "Square"),
                new ShapePreset(SpatialMaskShape.Diamond, "Diamond"),
                new ShapePreset(SpatialMaskShape.Disc, "Disc"),
                new ShapePreset(SpatialMaskShape.Ring, "Ring"),
            });

            AddShapeGroup(card, "Combat", new[]
            {
                new ShapePreset(SpatialMaskShape.ForwardLine, "Line"),
                new ShapePreset(SpatialMaskShape.ForwardCone, "Cone"),
                new ShapePreset(SpatialMaskShape.ForwardWideCone, "Wide Cone"),
                new ShapePreset(SpatialMaskShape.ForwardArc, "Arc"),
                new ShapePreset(SpatialMaskShape.Corridor, "Corridor"),
                new ShapePreset(SpatialMaskShape.Wall, "Wall"),
            });

            AddShapeGroup(card, "AI", new[]
            {
                new ShapePreset(SpatialMaskShape.RearCone, "Rear"),
                new ShapePreset(SpatialMaskShape.LeftFlank, "Left Flank"),
                new ShapePreset(SpatialMaskShape.RightFlank, "Right Flank"),
                new ShapePreset(SpatialMaskShape.Cross, "Cross"),
                new ShapePreset(SpatialMaskShape.DiagonalCross, "X"),
                new ShapePreset(SpatialMaskShape.RepelRing, "Repel Ring"),
            });

            return card;
        }

        private VisualElement CreateOperationsCard()
        {
            var card = CreateCard();
            card.style.marginTop = 8;

            card.Add(SectionTitle("Operations"));

            var sizeRow = Row();
            sizeRow.style.marginTop = 5;

            sizeRow.Add(Label("Size", 11, FontStyle.Bold, SubText()));
            AddSizeButton(sizeRow, 1);
            AddSizeButton(sizeRow, 3);
            AddSizeButton(sizeRow, 5);
            AddSizeButton(sizeRow, 7);
            AddSizeButton(sizeRow, 9);
            AddSizeButton(sizeRow, 11);
            AddSizeButton(sizeRow, 15);

            card.Add(sizeRow);

            var opsRow = Row();
            opsRow.style.marginTop = 7;

            opsRow.Add(Label("Edit", 11, FontStyle.Bold, SubText()));
            opsRow.Add(Chip("Clear", () => ApplyChange("Clear Spatial Mask", asset.Clear)));
            opsRow.Add(Chip("Mirror X", () => ApplyChange("Mirror Spatial Mask X", asset.MirrorX)));
            opsRow.Add(Chip("Mirror Y", () => ApplyChange("Mirror Spatial Mask Y", asset.MirrorY)));
            opsRow.Add(Chip("Rotate 90°", () =>
            {
                if (asset.Width != asset.Height)
                {
                    Debug.LogWarning("SpatialMaskAsset can only rotate square masks.");
                    return;
                }

                ApplyChange("Rotate Spatial Mask", asset.RotateClockwise);
            }));

            card.Add(opsRow);

            return card;
        }

        private VisualElement CreateFooter()
        {
            var footer = new HelpBox(
                "Minimal rules: Right-click samples a cell. Left-click paints selected value. Mouse wheel changes the cell value. Shift-left spreads/diffuses from the clicked cell. Alt-left erases.",
                HelpBoxMessageType.Info);

            footer.style.marginTop = 8;
            footer.style.color = Text();

            return footer;
        }

        private void RebuildGrid()
        {
            if (gridRoot == null)
                return;

            gridRoot.Clear();

            summaryLabel.text = $"{asset.Width}x{asset.Height}  ·  {asset.NonZeroCount()} active  ·  total {asset.TotalWeight()}";

            for (var y = 0; y < asset.Height; y++)
            {
                var row = Row();
                row.style.marginBottom = CellGap;

                for (var x = 0; x < asset.Width; x++)
                    row.Add(CreateCell(x, y));

                gridRoot.Add(row);
            }

            UpdateSelectedLabel();
        }

        private VisualElement CreateCell(int x, int y)
        {
            var value = asset.Get(x, y);
            var isCenter = x == asset.CenterX && y == asset.CenterY;
            var isAxis = x == asset.CenterX || y == asset.CenterY;

            var cell = new VisualElement();
            cell.style.width = CellSize;
            cell.style.height = CellSize;
            cell.style.marginRight = CellGap;
            cell.style.alignItems = Align.Center;
            cell.style.justifyContent = Justify.Center;
            cell.style.borderTopLeftRadius = 7;
            cell.style.borderTopRightRadius = 7;
            cell.style.borderBottomLeftRadius = 7;
            cell.style.borderBottomRightRadius = 7;
            cell.focusable = true;

            ApplyCellStyle(cell, value, isCenter, isAxis);

            var label = Label(GetCellText(value, isCenter), isCenter && value != 0 ? 10 : 12, isCenter ? FontStyle.Bold : FontStyle.Normal, BestTextForCell(value, isCenter));
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.pickingMode = PickingMode.Ignore;
            cell.Add(label);

            var offset = asset.GetOffset(x, y);
            cell.tooltip = isCenter
                ? $"CENTER / ACTOR ANCHOR\nOffset: ({offset.x}, {offset.y})\nValue: {value}"
                : $"Offset: ({offset.x}, {offset.y})\nValue: {value}";

            cell.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 1)
                {
                    PickCellValue(x, y);
                    evt.StopPropagation();
                    return;
                }

                if (evt.button != 0)
                    return;

                if (evt.altKey)
                {
                    SetCellValue(x, y, 0, "Erase Spatial Mask Cell");
                }
                else if (evt.shiftKey)
                {
                    DiffuseCell(x, y);
                }
                else if (evt.ctrlKey || evt.commandKey)
                {
                    AddCellValue(x, y, selectedValue, "Add Spatial Mask Cell");
                }
                else
                {
                    SetCellValue(x, y, selectedValue, "Paint Spatial Mask Cell");
                }

                evt.StopPropagation();
            });

            cell.RegisterCallback<WheelEvent>(evt =>
            {
                var step = evt.shiftKey ? 5 : 1;
                var delta = evt.delta.y < 0 ? step : -step;

                Undo.RecordObject(asset, "Scroll Spatial Mask Cell");

                asset.Add(x, y, delta);

                selectedValue = asset.Get(x, y);
                selectedValueField?.SetValueWithoutNotify(selectedValue);

                EditorUtility.SetDirty(asset);
                RebuildGrid();

                evt.StopPropagation();
            });

            return cell;
        }

        private void AddShapeGroup(VisualElement parent, string groupName, ShapePreset[] shapes)
        {
            var groupLabel = Label(groupName, 11, FontStyle.Bold, SubText());
            groupLabel.style.marginTop = 8;
            groupLabel.style.marginBottom = 5;
            parent.Add(groupLabel);

            var row = Row();
            row.style.flexWrap = Wrap.Wrap;

            foreach (var shape in shapes)
                row.Add(CreateShapeButton(shape));

            parent.Add(row);
        }

        private VisualElement CreateShapeButton(ShapePreset preset)
        {
            var button = new VisualElement();
            button.style.width = 94;
            button.style.height = 76;
            button.style.marginRight = 6;
            button.style.marginBottom = 6;
            button.style.paddingTop = 6;
            button.style.paddingBottom = 6;
            button.style.paddingLeft = 6;
            button.style.paddingRight = 6;
            button.style.borderTopLeftRadius = 9;
            button.style.borderTopRightRadius = 9;
            button.style.borderBottomLeftRadius = 9;
            button.style.borderBottomRightRadius = 9;
            button.style.backgroundColor = ButtonBackground();
            SetBorder(button, ButtonBorder(), 1);

            var title = Label(preset.Label, 10, FontStyle.Bold, Text());
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            title.style.marginBottom = 5;
            button.Add(title);

            button.Add(CreateShapePreview(preset.Shape));

            button.RegisterCallback<PointerEnterEvent>(_ =>
            {
                button.style.backgroundColor = ButtonHover();
                SetBorder(button, AccentDim(), 1);
            });

            button.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                button.style.backgroundColor = ButtonBackground();
                SetBorder(button, ButtonBorder(), 1);
            });

            button.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;

                StampShape(preset.Shape);
                evt.StopPropagation();
            });

            button.tooltip = $"Stamp {preset.Label} using value {selectedValue}.";
            return button;
        }

        private VisualElement CreateShapePreview(SpatialMaskShape shape)
        {
            var rootPreview = new VisualElement();
            rootPreview.style.alignSelf = Align.Center;

            var radius = PreviewSize / 2;
            var baseValue = selectedValue == 0 ? 1 : selectedValue;

            for (var py = 0; py < PreviewSize; py++)
            {
                var row = Row();
                row.style.marginBottom = PreviewGap;

                for (var px = 0; px < PreviewSize; px++)
                {
                    var dx = px - radius;
                    var dz = radius - py;

                    var isCenter = dx == 0 && dz == 0;
                    var weight = ShapeWeight(shape, dx, dz, radius, baseValue);

                    var dot = new VisualElement();
                    dot.style.width = PreviewCellSize;
                    dot.style.height = PreviewCellSize;
                    dot.style.marginRight = PreviewGap;
                    dot.style.borderTopLeftRadius = 2;
                    dot.style.borderTopRightRadius = 2;
                    dot.style.borderBottomLeftRadius = 2;
                    dot.style.borderBottomRightRadius = 2;

                    if (weight > 0)
                        dot.style.backgroundColor = PositivePreview();
                    else if (weight < 0)
                        dot.style.backgroundColor = NegativePreview();
                    else if (isCenter)
                        dot.style.backgroundColor = CenterPreview();
                    else
                        dot.style.backgroundColor = EmptyPreview();

                    row.Add(dot);
                }

                rootPreview.Add(row);
            }

            return rootPreview;
        }

        private void AddSizeButton(VisualElement parent, int size)
        {
            parent.Add(Chip($"{size}x{size}", () =>
            {
                ApplyChange($"Resize Spatial Mask {size}x{size}", () => asset.ResizeCentered(size, size));
            }));
        }

        private void StampShape(SpatialMaskShape shape)
        {
            Undo.RecordObject(asset, $"Stamp Spatial Mask Shape {shape}");

            if (replaceShape)
                asset.Clear();

            var baseValue = selectedValue == 0 ? 1 : selectedValue;
            var radius = Mathf.Min(asset.Width, asset.Height) / 2;

            for (var y = 0; y < asset.Height; y++)
            {
                for (var x = 0; x < asset.Width; x++)
                {
                    var offset = asset.GetOffset(x, y);
                    var weight = ShapeWeight(shape, offset.x, offset.y, radius, baseValue);

                    if (weight == 0)
                        continue;

                    if (replaceShape)
                        asset.Set(x, y, ClampToSByte(weight));
                    else
                        asset.Add(x, y, weight);
                }
            }

            EditorUtility.SetDirty(asset);
            RebuildGrid();
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
            var value = Mathf.Max(1, Mathf.Abs(baseValue));

            return shape switch
            {
                SpatialMaskShape.Point =>
                    dx == 0 && dz == 0 ? baseValue : 0,

                SpatialMaskShape.Surround =>
                    max == 1 ? value * sign : 0,

                SpatialMaskShape.Square =>
                    max <= radius ? Falloff(value, max, radius) * sign : 0,

                SpatialMaskShape.Diamond =>
                    manhattan <= radius ? Falloff(value, manhattan, radius) * sign : 0,

                SpatialMaskShape.Disc =>
                    sqr <= radius * radius ? Falloff(value, Mathf.RoundToInt(Mathf.Sqrt(sqr)), radius) * sign : 0,

                SpatialMaskShape.Ring =>
                    max == radius ? value * sign : 0,

                SpatialMaskShape.Cross =>
                    dx == 0 || dz == 0 ? Falloff(value, max, radius) * sign : 0,

                SpatialMaskShape.DiagonalCross =>
                    absX == absZ ? Falloff(value, max, radius) * sign : 0,

                SpatialMaskShape.ForwardLine =>
                    dx == 0 && dz > 0 ? Falloff(value, dz, radius) * sign : 0,

                SpatialMaskShape.ForwardCone =>
                    dz > 0 && absX <= dz ? Falloff(value, dz, radius) * sign : 0,

                SpatialMaskShape.ForwardWideCone =>
                    dz > 0 && absX <= dz + 1 ? Falloff(value, dz, radius) * sign : 0,

                SpatialMaskShape.ForwardArc =>
                    dz > 0 && absX <= dz && max >= Mathf.Max(1, radius - 1) ? value * sign : 0,

                SpatialMaskShape.RearCone =>
                    dz < 0 && absX <= -dz ? Falloff(value, -dz, radius) * sign : 0,

                SpatialMaskShape.LeftFlank =>
                    dx < 0 && absZ <= absX ? Falloff(value, absX, radius) * sign : 0,

                SpatialMaskShape.RightFlank =>
                    dx > 0 && absZ <= absX ? Falloff(value, absX, radius) * sign : 0,

                SpatialMaskShape.Corridor =>
                    dz > 0 && absX <= 1 ? Falloff(value, dz, radius) * sign : 0,

                SpatialMaskShape.Wall =>
                    dz == 1 && absX <= radius ? value * sign : 0,

                SpatialMaskShape.RepelRing =>
                    max == radius ? -value : 0,

                _ => 0,
            };
        }

        private static int Falloff(int value, int distance, int radius)
        {
            if (distance <= 0)
                return value;

            var t = 1f - Mathf.Clamp01((distance - 1f) / Mathf.Max(1f, radius));
            return Mathf.Max(1, Mathf.RoundToInt(value * t));
        }

        private void DiffuseCell(int x, int y)
        {
            var source = asset.Get(x, y);

            if (source == 0)
                source = ClampToSByte(selectedValue == 0 ? 1 : selectedValue);

            Undo.RecordObject(asset, "Diffuse Spatial Mask Cell");

            for (var oy = -1; oy <= 1; oy++)
            {
                for (var ox = -1; ox <= 1; ox++)
                {
                    var nx = x + ox;
                    var ny = y + oy;

                    if (!asset.IsInside(nx, ny))
                        continue;

                    var absX = Mathf.Abs(ox);
                    var absY = Mathf.Abs(oy);

                    var factor = absX + absY switch
                    {
                        0 => 1.00f,
                        1 => 0.66f,
                        _ => 0.33f,
                    };

                    var spread = Mathf.RoundToInt(source * factor);

                    if (spread == 0 && source != 0)
                        spread = source > 0 ? 1 : -1;

                    var current = asset.Get(nx, ny);

                    // Diffusion should feel like spreading intent, not vandalizing careful stronger cells.
                    if (Mathf.Abs(spread) >= Mathf.Abs(current))
                        asset.Set(nx, ny, ClampToSByte(spread));
                }
            }

            EditorUtility.SetDirty(asset);
            RebuildGrid();
        }

        private void PickCellValue(int x, int y)
        {
            selectedValue = asset.Get(x, y);
            selectedValueField?.SetValueWithoutNotify(selectedValue);
            UpdateSelectedLabel();
        }

        private void SetSelectedValue(int value)
        {
            selectedValue = Mathf.Clamp(value, sbyte.MinValue, sbyte.MaxValue);
            selectedValueField?.SetValueWithoutNotify(selectedValue);
            UpdateSelectedLabel();
            RepaintShapePreviews();
        }

        private void SetCellValue(int x, int y, int value, string undoName)
        {
            Undo.RecordObject(asset, undoName);
            asset.Set(x, y, ClampToSByte(value));
            EditorUtility.SetDirty(asset);
            RebuildGrid();
        }

        private void AddCellValue(int x, int y, int value, string undoName)
        {
            Undo.RecordObject(asset, undoName);
            asset.Add(x, y, value);
            EditorUtility.SetDirty(asset);
            RebuildGrid();
        }

        private void ApplyChange(string undoName, Action action)
        {
            Undo.RecordObject(asset, undoName);
            action.Invoke();
            EditorUtility.SetDirty(asset);
            RebuildGrid();
        }

        private void RepaintShapePreviews()
        {
            // Full rebuild keeps previews honest with the selected sign/value.
            // It is tiny editor UI, not runtime code.
            if (root == null)
                return;

            root.Clear();
            root.Add(CreateHeader());
            root.Add(CreateBrushCard());
            root.Add(CreateCanvasCard());
            root.Add(CreateShapeCard());
            root.Add(CreateOperationsCard());
            root.Add(CreateFooter());

            RebuildGrid();
            UpdateSelectedLabel();
            UpdateShapeModeButtons();
        }

        private void UpdateSelectedLabel()
        {
            if (selectedLabel == null)
                return;

            selectedLabel.text = selectedValue switch
            {
                > 0 => $"Attract  +{selectedValue}",
                < 0 => $"Repel  {selectedValue}",
                _ => "Erase  0",
            };

            selectedLabel.style.color = selectedValue switch
            {
                > 0 => SelectedPositiveText(),
                < 0 => SelectedNegativeText(),
                _ => Text(),
            };

            selectedLabel.style.backgroundColor = selectedValue switch
            {
                > 0 => SelectedPositiveBackground(),
                < 0 => SelectedNegativeBackground(),
                _ => NeutralPill(),
            };

            SetBorder(selectedLabel, selectedValue switch
            {
                > 0 => PositiveBorder(),
                < 0 => NegativeBorder(),
                _ => ButtonBorder(),
            }, 1);
        }

        private void UpdateShapeModeButtons()
        {
            if (replaceModeButton == null || addModeButton == null)
                return;

            ApplyModeButtonStyle(replaceModeButton, replaceShape);
            ApplyModeButtonStyle(addModeButton, !replaceShape);
        }

        private void ApplyModeButtonStyle(Button button, bool active)
        {
            button.style.backgroundColor = active ? AccentBackground() : ButtonBackground();
            button.style.color = active ? AccentText() : Text();
            SetBorder(button, active ? Accent() : ButtonBorder(), 1);
        }

        private static string GetCellText(sbyte value, bool isCenter)
        {
            if (isCenter)
                return value == 0 ? "◎" : $"◎\n{value}";

            return value == 0 ? string.Empty : value.ToString();
        }

        private void ApplyCellStyle(VisualElement cell, sbyte value, bool isCenter, bool isAxis)
        {
            var bg = CellBackground();
            var border = isAxis ? AxisBorder() : CellBorder();
            var borderWidth = isAxis ? 1.25f : 1f;

            if (value > 0)
            {
                var t = Mathf.InverseLerp(1, 32, Mathf.Abs(value));
                bg = Color.Lerp(PositiveLow(), PositiveHigh(), t);
                border = PositiveBorder();
                borderWidth = 1f;
            }
            else if (value < 0)
            {
                var t = Mathf.InverseLerp(1, 32, Mathf.Abs(value));
                bg = Color.Lerp(NegativeLow(), NegativeHigh(), t);
                border = NegativeBorder();
                borderWidth = 1f;
            }

            if (isCenter)
            {
                border = CenterBorder();
                borderWidth = 2.5f;
            }

            cell.style.backgroundColor = bg;
            SetBorder(cell, border, borderWidth);
        }

        private Color BestTextForCell(sbyte value, bool isCenter)
        {
            if (isCenter && value == 0)
                return CenterBorder();

            if (value > 0)
                return IsDark ? Color.white : new Color(0.12f, 0.08f, 0.02f, 1f);

            if (value < 0)
                return IsDark ? Color.white : new Color(0.02f, 0.06f, 0.12f, 1f);

            return Text();
        }

        private Button Chip(string text, Action action)
        {
            var button = new Button(action)
            {
                text = text,
            };

            button.style.height = 24;
            button.style.marginLeft = 4;
            button.style.marginRight = 0;
            button.style.marginBottom = 4;
            button.style.paddingLeft = 8;
            button.style.paddingRight = 8;
            button.style.borderTopLeftRadius = 6;
            button.style.borderTopRightRadius = 6;
            button.style.borderBottomLeftRadius = 6;
            button.style.borderBottomRightRadius = 6;
            button.style.backgroundColor = ButtonBackground();
            button.style.color = Text();
            SetBorder(button, ButtonBorder(), 1);

            return button;
        }

        private VisualElement CreateCard()
        {
            var card = new VisualElement();
            card.style.paddingTop = 9;
            card.style.paddingBottom = 9;
            card.style.paddingLeft = 10;
            card.style.paddingRight = 10;
            card.style.borderTopLeftRadius = 10;
            card.style.borderTopRightRadius = 10;
            card.style.borderBottomLeftRadius = 10;
            card.style.borderBottomRightRadius = 10;
            card.style.backgroundColor = CardBackground();
            SetBorder(card, CardBorder(), 1);
            return card;
        }

        private static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.alignItems = Align.Center;
            return row;
        }

        private Label SectionTitle(string text)
        {
            var label = Label(text, 12, FontStyle.Bold, Text());
            label.style.marginBottom = 2;
            return label;
        }

        private static Label Label(string text, int size, FontStyle style, Color color)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.unityFontStyleAndWeight = style;
            label.style.color = color;
            return label;
        }

        private static void SetBorder(VisualElement element, Color color, float width)
        {
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;

            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
        }

        private static sbyte ClampToSByte(int value)
        {
            return (sbyte)Mathf.Clamp(value, sbyte.MinValue, sbyte.MaxValue);
        }

        private Color RootBackground() => IsDark
            ? new Color(0.105f, 0.105f, 0.110f, 1f)
            : new Color(0.925f, 0.915f, 0.885f, 1f);

        private Color CardBackground() => IsDark
            ? new Color(0.145f, 0.145f, 0.155f, 1f)
            : new Color(0.985f, 0.975f, 0.940f, 1f);

        private Color CardBorder() => IsDark
            ? new Color(0.245f, 0.245f, 0.260f, 1f)
            : new Color(0.760f, 0.735f, 0.680f, 1f);

        private Color Text() => IsDark
            ? new Color(0.910f, 0.910f, 0.920f, 1f)
            : new Color(0.095f, 0.085f, 0.070f, 1f);

        private Color SubText() => IsDark
            ? new Color(0.680f, 0.680f, 0.700f, 1f)
            : new Color(0.330f, 0.300f, 0.250f, 1f);

        private Color Muted() => IsDark
            ? new Color(0.560f, 0.560f, 0.585f, 1f)
            : new Color(0.450f, 0.410f, 0.340f, 1f);

        private Color ButtonBackground() => IsDark
            ? new Color(0.190f, 0.190f, 0.205f, 1f)
            : new Color(0.910f, 0.890f, 0.830f, 1f);

        private Color ButtonHover() => IsDark
            ? new Color(0.245f, 0.245f, 0.265f, 1f)
            : new Color(0.965f, 0.940f, 0.860f, 1f);

        private Color ButtonBorder() => IsDark
            ? new Color(0.340f, 0.340f, 0.365f, 1f)
            : new Color(0.690f, 0.655f, 0.590f, 1f);

        private Color CellBackground() => IsDark
            ? new Color(0.175f, 0.175f, 0.185f, 1f)
            : new Color(0.880f, 0.855f, 0.790f, 1f);

        private Color CellBorder() => IsDark
            ? new Color(0.255f, 0.255f, 0.270f, 1f)
            : new Color(0.720f, 0.685f, 0.610f, 1f);

        private Color AxisBorder() => IsDark
            ? new Color(0.390f, 0.390f, 0.420f, 1f)
            : new Color(0.555f, 0.510f, 0.420f, 1f);

        private Color CenterBorder() => IsDark
            ? new Color(1.000f, 0.930f, 0.720f, 1f)
            : new Color(0.070f, 0.060f, 0.040f, 1f);

        private Color Accent() => IsDark
            ? new Color(1.000f, 0.660f, 0.220f, 1f)
            : new Color(0.620f, 0.340f, 0.050f, 1f);

        private Color AccentDim() => IsDark
            ? new Color(0.800f, 0.500f, 0.160f, 1f)
            : new Color(0.720f, 0.430f, 0.100f, 1f);

        private Color AccentBackground() => IsDark
            ? new Color(0.330f, 0.220f, 0.100f, 1f)
            : new Color(1.000f, 0.850f, 0.550f, 1f);

        private Color AccentText() => IsDark
            ? new Color(1.000f, 0.940f, 0.820f, 1f)
            : new Color(0.160f, 0.090f, 0.020f, 1f);

        private Color PositiveLow() => IsDark
            ? new Color(0.280f, 0.205f, 0.105f, 1f)
            : new Color(1.000f, 0.875f, 0.580f, 1f);

        private Color PositiveHigh() => IsDark
            ? new Color(0.850f, 0.485f, 0.055f, 1f)
            : new Color(0.930f, 0.520f, 0.085f, 1f);

        private Color PositiveBorder() => IsDark
            ? new Color(1.000f, 0.650f, 0.170f, 1f)
            : new Color(0.530f, 0.290f, 0.040f, 1f);

        private Color NegativeLow() => IsDark
            ? new Color(0.095f, 0.175f, 0.270f, 1f)
            : new Color(0.660f, 0.835f, 0.970f, 1f);

        private Color NegativeHigh() => IsDark
            ? new Color(0.055f, 0.345f, 0.700f, 1f)
            : new Color(0.210f, 0.540f, 0.875f, 1f);

        private Color NegativeBorder() => IsDark
            ? new Color(0.300f, 0.650f, 1.000f, 1f)
            : new Color(0.060f, 0.290f, 0.560f, 1f);

        private Color SelectedPositiveBackground() => IsDark
            ? new Color(0.300f, 0.205f, 0.090f, 1f)
            : new Color(1.000f, 0.855f, 0.575f, 1f);

        private Color SelectedNegativeBackground() => IsDark
            ? new Color(0.095f, 0.200f, 0.330f, 1f)
            : new Color(0.690f, 0.850f, 0.980f, 1f);

        private Color SelectedPositiveText() => IsDark
            ? new Color(1.000f, 0.800f, 0.430f, 1f)
            : new Color(0.250f, 0.130f, 0.020f, 1f);

        private Color SelectedNegativeText() => IsDark
            ? new Color(0.590f, 0.830f, 1.000f, 1f)
            : new Color(0.030f, 0.160f, 0.310f, 1f);

        private Color NeutralPill() => IsDark
            ? new Color(0.220f, 0.220f, 0.235f, 1f)
            : new Color(0.860f, 0.835f, 0.775f, 1f);

        private Color PositivePreview() => IsDark
            ? new Color(1.000f, 0.610f, 0.170f, 1f)
            : new Color(0.820f, 0.420f, 0.065f, 1f);

        private Color NegativePreview() => IsDark
            ? new Color(0.330f, 0.680f, 1.000f, 1f)
            : new Color(0.120f, 0.390f, 0.700f, 1f);

        private Color CenterPreview() => IsDark
            ? new Color(0.880f, 0.820f, 0.620f, 1f)
            : new Color(0.165f, 0.140f, 0.085f, 1f);

        private Color EmptyPreview() => IsDark
            ? new Color(0.265f, 0.265f, 0.280f, 1f)
            : new Color(0.770f, 0.735f, 0.660f, 1f);

        private readonly struct ShapePreset
        {
            public readonly SpatialMaskShape Shape;
            public readonly string Label;

            public ShapePreset(SpatialMaskShape shape, string label)
            {
                Shape = shape;
                Label = label;
            }
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
            ForwardArc,
            Corridor,
            Wall,

            RearCone,
            LeftFlank,
            RightFlank,

            RepelRing,
        }
    }
}
#endif