
using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KittenExtensions.Patch;

[Flags]
public enum OutlineType
{
  None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8, All = Left | Top | Right | Bottom,
}

public static partial class ImGuiEx
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

  public static IndentScope WithIndent(float indent) => new(indent);
  public readonly struct IndentScope : IDisposable
  {
    private readonly float indent;
    public IndentScope(float indent)
    {
      this.indent = indent;
      ImGui.Indent(indent);
    }
    public void Dispose() => ImGui.Unindent(indent);
  }
}