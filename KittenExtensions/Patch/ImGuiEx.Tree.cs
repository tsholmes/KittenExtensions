
using System;
using System.Collections.Generic;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KittenExtensions.Patch;

public interface ITreeNodeAdapter<N>
{
  public static abstract bool NodesEqual(N node1, N node2);
  public static abstract bool NodeParent(N node, out N parent);
}

public class ImGuiTreeView<N, A>(
  bool clickable = true, bool clickSelectable = true, bool treeSelectHighlight = true,
  int defaultExpandDepth = int.MaxValue
) where A : ITreeNodeAdapter<N>
{
  private static readonly float SIN_60 = MathF.Sqrt(3f) / 2f;
  private static readonly float COS_60 = 0.5f;

  private struct NodeState
  {
    public N Node;
    public ImGuiID Id;
    public int Depth;
    public bool HasChildren;
    public bool Open;
    public bool Clickable;
    public bool OnSelectedPath;
    public bool Selected;
    public bool Hovered;
    public floatRect Arrow;
    public floatRect Text;
    public float2 VertStart;
    public float InlineX;

    public bool Highlighted;
    public float MaxHorizY;
    public float BottomY;
  }

  private readonly bool clickable = clickable;
  private readonly bool clickSelectable = clickSelectable;
  private readonly bool treeSelectHighlight = treeSelectHighlight;
  private readonly int defaultExpandDepth = defaultExpandDepth;

  // frame state
  private bool started;
  private ImDrawListPtr drawList;
  private uint crSelected;
  private uint crHovered;
  private uint crActive;
  private uint crLine;
  private uint crText;
  private float4 crTextF;
  private float spacing;
  private float lineHeight;
  private float indent;
  private ImGuiStoragePtr storage;

  // node state
  private readonly NodeState[] stack = new NodeState[1024];
  private int depth = 0;

  private bool hasSelected = false;
  private N selected = default;
  private readonly List<N> selectedPath = [];
  private bool scrollToSelected = false;
  private bool expandSelected = false;
  private bool inSelected = false;

  public bool GetSelected(out N sel)
  {
    sel = hasSelected ? selected : default;
    return hasSelected;
  }

  public void SetSelected(N sel, bool scrollTo = true, bool expand = true)
  {
    hasSelected = true;
    selected = sel;
    scrollToSelected = scrollTo;
    expandSelected = expand;
    selectedPath.Clear();
    do
    {
      selectedPath.Add(sel);
    } while (A.NodeParent(sel, out sel));
    selectedPath.Reverse();
  }

  public void ClearSelected()
  {
    hasSelected = false;
    selectedPath.Clear();
  }

  public void Begin()
  {
    if (started)
      throw new InvalidOperationException($"Last frame not ended");
    started = true;
    drawList = ImGui.GetWindowDrawList();
    drawList.ChannelsSplit(2);
    crSelected = ImGui.ColorConvertFloat4ToU32(ImGui.GetStyleColorVec4(ImGuiCol.FrameBg));
    crHovered = ImGui.ColorConvertFloat4ToU32(ImGui.GetStyleColorVec4(ImGuiCol.FrameBg));
    crActive = ImGui.ColorConvertFloat4ToU32(ImGui.GetStyleColorVec4(ImGuiCol.ButtonActive));
    crLine = ImGui.ColorConvertFloat4ToU32(ImGui.GetStyleColorVec4(ImGuiCol.TreeLines));
    crTextF = ImGui.GetStyleColorVec4(ImGuiCol.Text);
    crText = ImGui.ColorConvertFloat4ToU32(crTextF);
    spacing = ImGui.GetStyle().ItemSpacing.X;
    indent = lineHeight = ImGui.GetTextLineHeight();
    storage = ImGui.GetStateStorage();
    inSelected = false;

    drawList.ChannelsSetCurrent(1);
  }

  public void End()
  {
    if (!started)
      throw new InvalidOperationException($"Frame not started");
    if (depth > 0)
      throw new InvalidOperationException($"Tree nodes not closed");
    drawList.ChannelsMerge();
    started = false;
    drawList = null;
    storage = null;
    expandSelected = false;
    scrollToSelected = false;
  }

  public NodeScope TreeNode(
    N nodeVal, ImString text, out bool open, out bool clicked, bool? clickable = null,
    bool hasChildren = true, float4? highlightCr = null, float4? textCr = null)
  {
    var nodeID = ImGui.GetID(text);
    ref var node = ref Push(nodeID);

    var fullRect = new floatRect()
    {
      Min = ImGui.GetCursorScreenPos(),
      Extent = new(ImGui.GetContentRegionAvail().X, lineHeight),
    };

    node.Node = nodeVal;
    node.HasChildren = hasChildren;
    node.OnSelectedPath = hasSelected
      && (node.Depth == 0 || stack[node.Depth - 1].OnSelectedPath)
      && node.Depth < selectedPath.Count
      && A.NodesEqual(nodeVal, selectedPath[node.Depth]);
    node.Selected = node.OnSelectedPath && node.Depth == selectedPath.Count - 1;
    node.Clickable = clickable ?? this.clickable;
    (node.Arrow, node.Text) = fullRect.CutWidth(indent);
    node.InlineX = node.Text.R;
    node.MaxHorizY = node.Arrow.B;
    node.VertStart = node.Arrow.CB;

    if (text.AsSpan().Contains((byte)'\n'))
    {
      var textHeight = ImGui.CalcTextSize(text).Y;
      node.Text.Max = new(node.Text.L, node.Text.T + textHeight);
    }
    node.BottomY = node.Text.B;

    inSelected = inSelected || node.Selected;

    ImGui.PushID(text);

    if (hasChildren)
      DrawOpenButton(ref node);

    open = node.Open;
    if (expandSelected)
    {
      var shouldExpand = node.OnSelectedPath ||
        node.Depth < defaultExpandDepth ||
        (inSelected && (node.Depth - selectedPath.Count) < defaultExpandDepth);
      if (shouldExpand != node.Open)
        storage.SetBool(node.Id, open = node.Open = shouldExpand);
    }
    clicked = DrawSelectButton(ref node);

    ImGui.SetCursorScreenPos(node.Text.TL + new float2(spacing, 0));

    ImGui.TextColored(textCr ?? highlightCr ?? crTextF, text);

    if (node.Selected && scrollToSelected)
      ImGui.SetScrollHereY();

    if (node.Depth > 0)
    {
      ref var pnode = ref stack[node.Depth - 1];
      pnode.MaxHorizY = node.Arrow.CY;
    }
    if (highlightCr != null)
      Highlight(highlightCr.Value);

    ImGui.PopID();
    return new(this, nodeID, indent);
  }

  public void TreeNodeExtra(ImString text, float4? textCr = null)
  {
    if (!started || depth == 0)
      throw new InvalidOperationException("Not in tree node");

    var cursor = ImGui.GetCursorScreenPos();
    var hei = lineHeight;
    if (text.AsSpan().Contains((byte)'\n'))
      hei = ImGui.CalcTextSize(text).Y;

    ImGui.TextColored(textCr ?? crTextF, text);
    stack[depth - 1].BottomY = cursor.Y + hei;
  }

  public bool InlineButton(ImString text, float width = 0, bool hoverOnly = true)
  {
    if (!started || depth == 0)
      throw new InvalidOperationException($"Not in tree node");

    var cursor = ImGui.GetCursorScreenPos();

    ref var node = ref stack[depth - 1];
    if (hoverOnly && !node.Hovered)
      return false;
    if (width == 0)
      width = ImGui.CalcTextSize(text).X + spacing * 2;

    var start = new float2(node.InlineX - width, node.Text.T);
    ImGui.SetCursorScreenPos(start);
    ImGui.SetNextItemWidth(width);
    var clicked = ImGui.SmallButton(text);
    node.InlineX = start.X - spacing;

    ImGui.SetCursorScreenPos(cursor);
    ImGui.Dummy(float2.Zero);
    ImGui.SetCursorScreenPos(cursor);

    return clicked;
  }

  private ref NodeState Push(ImGuiID id)
  {
    if (!started)
      throw new InvalidOperationException($"Tree not started");
    ref var node = ref stack[depth];
    node = new() { Id = id, Depth = depth++ };
    return ref node;
  }

  private ref NodeState Pop(ImGuiID id)
  {
    if (!started)
      throw new InvalidOperationException($"Tree not started");
    if (depth == 0 || stack[depth - 1].Id != id)
      throw new InvalidOperationException("Invalid tree state");
    return ref stack[--depth];
  }

  private void Finalize(ref NodeState node)
  {
    DrawVert(ref node, crLine);

    if (!node.Highlighted)
      DrawArrow(ref node, crText);

    if (node.Selected)
    {
      drawList.ChannelsSetCurrent(0);

      var end = new float2(node.Text.R, node.BottomY);
      drawList.AddRectFilled(node.Arrow.TL, end, crSelected);

      drawList.ChannelsSetCurrent(1);

      inSelected = false;
    }

    if (node.Depth > 0 && treeSelectHighlight)
      stack[node.Depth - 1].BottomY = node.BottomY;

    if (node.Depth == 0 || node.Highlighted)
      return;

    DrawHoriz(ref node, crLine);
  }

  private void DrawOpenButton(ref NodeState node)
  {
    node.Open = storage.GetBool(node.Id, node.Depth < defaultExpandDepth);

    ImGui.SetCursorScreenPos(node.Arrow.TL);
    if (ImGui.InvisibleButton("open", node.Arrow.Extent))
    {
      node.Open = !node.Open;
      storage.SetBool(node.Id, node.Open);
    }
    if (ImGui.IsItemActive())
      drawList.AddRectFilled(node.Arrow.TL, node.Arrow.BR, crActive);
    else if (ImGui.IsItemHovered())
      drawList.AddRectFilled(node.Arrow.TL, node.Arrow.BR, crHovered);
  }

  private bool DrawSelectButton(ref NodeState node)
  {
    ImGui.SetCursorScreenPos(node.Text.TL);
    var clicked = false;
    ImGui.SetNextItemAllowOverlap();
    if (node.Clickable)
      clicked = ImGui.InvisibleButton("text", node.Text.Extent);
    else
      ImGui.Dummy(node.Text.Extent);
    if (clicked && clickSelectable)
    {
      if (hasSelected && A.NodesEqual(node.Node, selected))
        ClearSelected();
      else
        SetSelected(node.Node, false, false);
    }
    node.Hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenOverlappedByItem
      | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
    if (ImGui.IsItemActive())
      drawList.AddRectFilled(node.Text.TL, node.Text.BR, crActive);
    else if (node.Hovered)
      drawList.AddRectFilled(node.Text.TL, node.Text.BR, crHovered);
    return clicked;
  }

  private void DrawVert(ref NodeState node, uint cr)
  {
    if (node.MaxHorizY <= node.VertStart.Y)
      return;
    var end = new float2(node.VertStart.X, node.MaxHorizY);
    drawList.AddLine(node.VertStart, end, cr);
    node.VertStart = end;
  }

  private void DrawHoriz(ref NodeState node, uint cr)
  {
    if (node.Depth == 0)
      return;
    var start = new float2(stack[node.Depth - 1].Arrow.CX, node.Arrow.CY);
    drawList.AddLine(start, node.Arrow.LC, cr);
  }

  private void Highlight(float4 color)
  {
    var cr = ImGui.ColorConvertFloat4ToU32(color);
    for (var i = depth; --i >= 0;)
    {
      ref var node = ref stack[i];
      if (node.Highlighted)
        break;

      node.Highlighted = true;
      DrawArrow(ref node, cr);

      if (i == 0)
        break;

      DrawHoriz(ref node, cr);
      DrawVert(ref stack[i - 1], cr);
    }
  }

  private static void DrawArrow(ref NodeState node, uint cr)
  {
    var drawList = ImGui.GetWindowDrawList();
    var arrowD = node.Arrow.Extent.Y * 0.3f;
    var arrowSin = arrowD * SIN_60;
    var arrowCos = arrowD * COS_60;
    var arrowCenter = node.Arrow.Center;
    if (node.HasChildren)
      if (node.Open)
        drawList.AddTriangleFilled(
          arrowCenter + new float2(0, arrowD),
          arrowCenter + new float2(-arrowSin, -arrowCos),
          arrowCenter + new float2(arrowSin, -arrowCos),
          cr
        );
      else
        drawList.AddTriangleFilled(
          arrowCenter + new float2(arrowD, 0),
          arrowCenter + new float2(-arrowCos, arrowSin),
          arrowCenter + new float2(-arrowCos, -arrowSin),
          cr
        );
    else
      drawList.AddCircleFilled(arrowCenter, arrowD * 0.5f, cr);
  }

  public readonly struct NodeScope(ImGuiTreeView<N, A> view, ImGuiID id, float indent) : IDisposable
  {
    private readonly ImGuiTreeView<N, A> view = view;
    private readonly ImGuiID id = id;
    private readonly ImGuiEx.IndentScope indent = new(indent);

    public void Dispose()
    {
      indent.Dispose();
      view.Finalize(ref view.Pop(id));
    }
  }
}

public static partial class Extensions
{
  extension(floatRect rect)
  {
    public float L => rect.Min.X;
    public float R => rect.Max.X;
    public float T => rect.Min.Y;
    public float B => rect.Max.Y;
    public float CX => (rect.L + rect.R) / 2f;
    public float CY => (rect.T + rect.B) / 2f;
    public float2 TL => rect.Min;
    public float2 TR => new(rect.R, rect.T);
    public float2 BL => new(rect.L, rect.B);
    public float2 BR => rect.Max;
    public float2 LC => new(rect.L, rect.CY);
    public float2 RC => new(rect.R, rect.CY);
    public float2 CT => new(rect.CX, rect.T);
    public float2 CB => new(rect.CX, rect.B);

    public (floatRect, floatRect) CutWidth(float width)
    {
      var x = rect.Min.X + width;
      if (x < rect.Min.X || x > rect.Max.X)
        throw new InvalidOperationException();
      return (
        new() { Min = rect.TL, Max = new(x, rect.B) },
        new() { Min = new(x, rect.T), Max = rect.BR }
      );
    }
  }
}