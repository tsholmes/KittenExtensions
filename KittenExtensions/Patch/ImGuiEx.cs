
using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KittenExtensions.Patch;

[Flags]
public enum OutlineType
{
  None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8, All = Left | Top | Right | Bottom,
}

public static class ImGuiEx
{
  public static Space AvailableSpace() => new()
  {
    Start = ImGui.GetCursorScreenPos(),
    Size = ImGui.GetContentRegionAvail(),
  };

  public static void CenterTextCursor(Space space, ReadOnlySpan<char> text)
  {
    var x = (space.Start.X + space.End.X) / 2 - ImGui.CalcTextSize(text).X / 2;
    ImGui.SetCursorScreenPos(new float2(x, space.Start.Y));
  }

  public static DrawListSplitter SplitBg()
  {
    var dl = ImGui.GetWindowDrawList();
    dl.ChannelsSplit(2);
    dl.ChannelsSetCurrent(1);
    return new(dl);
  }

  public static void BgHighlight(Space space, ImColor8? cr = null, ImGuiCol? styleCr = null)
  {
    var dl = ImGui.GetWindowDrawList();
    dl.ChannelsSetCurrent(0);

    dl.AddRectFilled(space.Start, space.End, ToCol(cr, styleCr));

    dl.ChannelsSetCurrent(1);
  }

  public static void BgOutline(
    Space space, OutlineType type, float width, ImColor8? cr = null, ImGuiCol? styleCr = null)
  {
    if (type == 0)
      return;
    var dl = ImGui.GetWindowDrawList();
    dl.ChannelsSetCurrent(0);

    var col = ToCol(cr, styleCr);

    Space outline;
    if ((type & OutlineType.Left) != 0)
    {
      (outline, space) = space.CutX(width);
      dl.AddRectFilled(outline.Start, outline.End, col);
    }
    if ((type & OutlineType.Right) != 0)
    {
      (space, outline) = space.CutX(-width);
      dl.AddRectFilled(outline.Start, outline.End, col);
    }
    if ((type & OutlineType.Top) != 0)
    {
      (outline, space) = space.CutY(width);
      dl.AddRectFilled(outline.Start, outline.End, col);
    }
    if ((type & OutlineType.Bottom) != 0)
    {
      (_, outline) = space.CutY(-width);
      dl.AddRectFilled(outline.Start, outline.End, col);
    }

    dl.ChannelsSetCurrent(1);
  }

  private static ImColor8 ToCol(ImColor8? cr, ImGuiCol? styleCr) =>
     cr ?? ImGui.ColorConvertFloat4ToU32(ImGui.GetStyleColorVec4(styleCr ?? ImGuiCol.Button));

  public struct Space
  {
    public float2 Start;
    public float2 Size;
    public float2 End => Start + Size;

    public static Space StartSize(float2 start, float2 size) => new() { Start = start, Size = size };
    public static Space StartEnd(float2 start, float2 end) => new() { Start = start, Size = end - start };

    public (Space, Space) SplitX(bool useSpacing = true)
    {
      var spacing = useSpacing ? ImGui.GetStyle().ItemSpacing.X : 0f;

      var halfSz = new float2((Size.X - spacing) / 2f, Size.Y);
      var left = StartSize(Start, halfSz);
      var right = StartSize(new(Start.X + halfSz.X + spacing, Start.Y), halfSz);
      return (left, right);
    }

    public (Space, Space) CutX(float? size = null)
    {
      var leftWid = size ?? (ImGui.GetCursorScreenPos().X - Start.X);
      if (leftWid < 0)
        leftWid = Size.X + leftWid;
      var prev = StartSize(Start, new float2(leftWid, Size.Y));
      var next = StartEnd(Start + new float2(leftWid, 0), End);
      return (prev, next);
    }

    public (Space, Space) CutY(float? size = null)
    {
      var topHei = size ?? (ImGui.GetCursorScreenPos().Y - Start.Y);
      if (topHei < 0)
        topHei = Size.Y + topHei;
      var prev = StartSize(Start, new float2(Size.X, topHei)); ;
      var next = StartEnd(Start + new float2(0, topHei), End);
      return (prev, next);
    }

    public Space Expand(float left = 0, float top = 0, float right = 0, float bottom = 0) =>
      StartEnd(Start - new float2(left, top), End + new float2(right, bottom));

    public Space Indent(float by) => StartEnd(Start + new float2(by, 0), End);

    public Space TreeIndent() => Indent(ImGui.GetTreeNodeToLabelSpacing());
  }

  public readonly struct DrawListSplitter(ImDrawListPtr drawList) : IDisposable
  {
    private readonly ImDrawListPtr drawList = drawList;
    public void Dispose() => drawList.ChannelsMerge();
  }
}