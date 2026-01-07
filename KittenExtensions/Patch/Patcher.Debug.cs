
using System;
using System.Collections;
using System.Collections.Generic;
using System.Xml;
using System.Xml.XPath;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KittenExtensions.Patch;

public static partial class XmlPatcher
{
  private class PatchDebugPopup : Popup
  {
    private readonly string title;
    private readonly char[] buffer = new char[65536];

    private readonly DebugOpExecContext context;
    private readonly IEnumerator<XmlPatch> patches;
    private PatchExecutor executor;
    private bool followTarget = true;
    private bool followPatch = false;
    private int execTab = 0;

    private XmlNode xpathContext;
    private XmlNode newXpathContext;
    private readonly ImInputString testXpath = new(1024, ".");
    private object xpathResult = null;

    private readonly List<XmlNode> nodePath = [];
    private bool newNode = true;
    private XmlNode curNode = null;
    private XmlNode curLineNode = null;

    public PatchDebugPopup()
    {
      title = "PatchDebug####" + PopupId;
      patches = GetPatches().GetEnumerator();
      context = new(RootNode.CreateNavigator());

      xpathContext = RootNode;

      NextPatch();
      UpdatePath();
    }

    private void NextPatch()
    {
      executor?.ToEnd();
      if (!patches.MoveNext())
      {
        executor = null;
        return;
      }
      executor = new(context.Execution(patches.Current));
    }

    private void UpdatePath()
    {
      if (followPatch)
        SetCurNode(executor?.CurOpElement);
      else if (followTarget)
        SetCurNode(executor?.CurTarget ?? executor?.CurNav);
    }

    private void SetCurNode(XmlNode node)
    {
      curLineNode = curNode = node;
      if (node is XmlAttribute attr)
        curLineNode = node = attr.OwnerElement;
      nodePath.Clear();
      while (node != null)
      {
        nodePath.Add(node);
        node = node.ParentNode as XmlElement;
      }
      nodePath.Reverse();
      newNode = true;

      xpathResult = null;
    }

    private ImDrawListPtr parentDl;
    protected override void OnDrawUi()
    {
      var padding = new float2(50, 50);
      var size = float2.Unpack(Program.GetWindow().Size) - padding * 2;

      ImGui.SetNextWindowSize(size, ImGuiCond.Always);
      ImGui.SetNextWindowPos(ImGui.GetMainViewport().Pos + padding, ImGuiCond.Always);
      ImGui.OpenPopup(title);
      ImGui.BeginPopup(
        title, ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.Popup);

      parentDl = ImGui.GetWindowDrawList();

      var windowStart = ImGui.GetCursorScreenPos();
      var contentWidth = ImGui.GetContentRegionAvail().X;
      var spacing = ImGui.GetStyle().ItemSpacing;
      var frame = ImGui.GetStyle().FramePadding;
      var halfWidth = (contentWidth - spacing.X) / 2;
      var centerX = windowStart.X + halfWidth + spacing.X;

      ImGui.AlignTextToFramePadding();
      ImGui.Text("KittenExtensions Patch Debug");

      if (executor != null)
      {
        if (StepButton("Run Patch", sameLine: false))
          OnStep(false);
        if (StepButton("Run All Patches"))
        {
          while (executor != null)
            NextPatch();
          UpdatePath();
        }
        if (StepButton("Step", enabled: executor?.CanStep))
          OnStep(executor.Step());
        if (StepButton("Step Over", enabled: executor?.CanStepOver))
          OnStep(executor.StepOver());
        if (StepButton("Step Out", enabled: executor?.CanStepOut))
          OnStep(executor.StepOut());
        if (StepButton("Next Op", enabled: executor?.CanNextOp))
          OnStep(executor.ToNextOp());
        if (StepButton("Next Action", enabled: executor?.CanNextAction))
          OnStep(executor.ToNextAction());
      }
      else
      {
        if (ImGui.Button("Start Game"))
          Active = false;
      }

      ImGui.Separator();

      var togglesY = ImGui.GetCursorScreenPos().Y;

      ImGui.SetCursorScreenPos(new(windowStart.X + frame.X, togglesY));
      if (Selectable("Exec Tree", execTab == 0, new float2(150, 0))) execTab = 0;
      if (Selectable("XPath Tester", execTab == 1, new float2(150, 0), sameLine: true)) execTab = 1;
      ImGui.SetNextWindowPos(new(windowStart.X, ImGui.GetCursorScreenPos().Y));
      ImGui.BeginChild("Exec", new(halfWidth, ImGui.GetContentRegionAvail().Y), ImGuiChildFlags.Borders);
      switch (execTab)
      {
        case 0: DrawExecTree(context, null); break;
        case 1: DrawXPathTest(); break;
        default: break;
      }
      ImGui.EndChild();

      ImGui.SetCursorScreenPos(new(centerX + frame.X, togglesY));
      if (Selectable("Follow Target", followTarget, new float2(150, 0)))
      {
        followTarget = !followTarget;
        followPatch &= !followTarget;
        UpdatePath();
      }
      if (Selectable("Follow Patch", followPatch, new float2(150, 0), sameLine: true))
      {
        followPatch = !followPatch;
        followTarget &= !followPatch;
        UpdatePath();
      }
      ImGui.SetNextWindowPos(new(centerX, ImGui.GetCursorScreenPos().Y));
      ImGui.BeginChild("Doc", new(halfWidth, ImGui.GetContentRegionAvail().Y), ImGuiChildFlags.Borders);
      DrawDocTree(RootNode, 0, true, false);
      ImGui.EndChild();

      parentDl = null;

      ImGui.EndPopup();

      newNode = false;
      if (newXpathContext != null)
      {
        xpathContext = newXpathContext;
        newXpathContext = null;
        xpathResult = null;
        testXpath.SetValue(".");
      }
    }

    private static bool StepButton(ImString text, bool? enabled = true, bool sameLine = true)
    {
      if (sameLine)
        ImGui.SameLine();
      ImGui.BeginDisabled(enabled != true);
      var res = ImGui.Button(text);
      ImGui.EndDisabled();
      return res;
    }

    private static bool Selectable(
      ImString text, bool selected, float2 size,
      bool sameLine = false, bool centered = true, ImGuiSelectableFlags flags = default)
    {
      if (centered)
        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new float2(0.5f));
      if (sameLine)
        ImGui.SameLine();
      var res = ImGui.Selectable(
        text, selected, flags: ImGuiSelectableFlags.NoAutoClosePopups | flags, size: size);
      if (centered)
        ImGui.PopStyleVar();
      return res;
    }

    private void OnStep(bool result)
    {
      if (!result)
        NextPatch();
      UpdatePath();
    }

    private const ImGuiTreeNodeFlags TREE_FLAGS = ImGuiTreeNodeFlags.DrawLinesFull;
    private const ImGuiTreeNodeFlags LEAF_FLAGS =
      TREE_FLAGS | ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;

    private void DrawExecTree(DebugOpExecContext ctx, DebugOpExecContext parent)
    {
      const float LINE_WIDTH = 3;
      const float X_SPACING = -4;
      const float Y_SPACING = -4;
      var line = new LineBuilder(buffer);
      var pop = false;
      var open = true;
      var highlight = false;
      var underline = false;
      var treeSpace = ImGui.GetTreeNodeToLabelSpacing();
      var startCursor = ImGui.GetCursorScreenPos();
      var contentRight = startCursor.X + ImGui.GetContentRegionAvail().X;
      startCursor.X += treeSpace + X_SPACING;
      if (parent != null)
      {
        var isNav = parent.Nav != ctx.Nav;
        var isExec = parent.ContextExec != ctx.ContextExec && ctx.ContextExec != null;
        var isAction = parent.ContextAction != ctx.ContextAction && ctx.ContextExec != null;

        ReadOnlySpan<char> text;

        if (isNav)
        {
          line.AddNodePath(ctx.Nav.UnderlyingObject as XmlNode, parent.Nav.UnderlyingObject as XmlNode);
          text = line.Line;
        }
        else if (isExec)
        {
          var op = ctx.ContextExec.Op;
          highlight = ctx.ContextExec == executor?.CurExec && executor?.CurAction == null;
          underline = highlight && executor?.LastState == PatchExecutor.ExecState.ExecEnd;
          if (op is XmlPatch patch)
            text = patch.Id;
          else
          {
            line.ElementOpen(op.Element, !op.Element.HasChildNodes);
            text = line.Line;
          }
        }
        else if (isAction)
        {
          var action = ctx.ContextAction;
          highlight = action == executor?.CurAction;
          underline = highlight && executor?.LastState == PatchExecutor.ExecState.ActionEnd;
          line.Add(action.Type);
          line.Add(' ');
          line.AddNodePath(action.Target);
          if (action.Type != OpActionType.Delete)
          {
            line.Add(' ');
            var pos = action.Pos;
            if (pos == OpPosition.Default)
              pos = action.Target is XmlAttribute ? OpPosition.Replace : OpPosition.Merge;
            line.Add(pos);
            line.Add(' ');
          }
          switch (action.Source)
          {
            case string srcString:
              line.AddQuotedFirstLine(srcString);
              break;
            case bool srcBool:
              line.Add(srcBool ? "true" : "false");
              break;
            case double srcDouble:
              line.Add(srcDouble, "f");
              break;
            case IList srcList:
              if (srcList.Count == 1)
              {
                switch (srcList[0])
                {
                  case string elStr:
                    line.AddQuotedFirstLine(elStr);
                    break;
                  case XmlNode elNode:
                    line.NodeOneLine(elNode);
                    break;
                }
              }
              else
              {
                for (var i = 0; i < 3 && i < srcList.Count; i++)
                {
                  switch (srcList[i])
                  {
                    case string elStr:
                      line.Add("\n  ");
                      line.AddQuotedFirstLine(elStr);
                      break;
                    case XmlNode elNode:
                      line.Add("\n  ");
                      line.NodeOneLine(elNode);
                      break;
                  }
                }
                if (srcList.Count > 3)
                  line.Add("\n  ...");
              }
              break;
          }
          text = line.Line;
        }
        else
        {
          text = "???";
        }

        if (highlight && !underline)
        {
          var start = startCursor + new float2(LINE_WIDTH, 0);
          var end = new float2(contentRight, start.Y + LINE_WIDTH);
          var cr = ImGui.GetStyleColorVec4(ImGuiCol.Button);
          parentDl.AddRectFilled(start, end, ImGui.ColorConvertFloat4ToU32(cr));
        }

        if (highlight && newNode)
          ImGui.SetScrollHereY();

        if (ctx.Children.Count == 0)
          ImGui.TreeNodeEx(text, LEAF_FLAGS);
        else
        {
          if (ctx.Ended)
          {
            ImGui.SetNextItemOpen(false);
            ctx.Ended = false;
          }
          open = pop = ImGui.TreeNodeEx(text, TREE_FLAGS | ImGuiTreeNodeFlags.DefaultOpen);
        }
      }

      if (open)
      {
        for (var i = 0; i < ctx.Children.Count; i++)
        {
          ImGui.PushID(i);
          DrawExecTree(ctx.Children[i], ctx);
          ImGui.PopID();
        }
      }

      if (pop)
        ImGui.TreePop();

      var endCursor = ImGui.GetCursorScreenPos() + new float2(treeSpace + X_SPACING, Y_SPACING);

      if (highlight)
      {
        var end = endCursor + new float2(LINE_WIDTH, 0);
        var cr = ImGui.GetStyleColorVec4(ImGuiCol.Button);
        parentDl.AddRectFilled(startCursor, end, ImGui.ColorConvertFloat4ToU32(cr));
      }

      if (underline)
      {
        var end = new float2(contentRight, endCursor.Y + LINE_WIDTH);
        var cr = ImGui.GetStyleColorVec4(ImGuiCol.Button);
        parentDl.AddRectFilled(endCursor, end, ImGui.ColorConvertFloat4ToU32(cr));
      }
    }

    private void DrawXPathTest()
    {
      var line = new LineBuilder(buffer);
      line.Add("Context: ");
      line.AddNodePath(xpathContext);
      if (Selectable(line.Line, xpathContext == curNode, float2.Zero, centered: false))
        SetCurNode(xpathContext);
      line.Clear();

      ImGui.SetNextItemWidth(-float.Epsilon);
      if (ImGui.InputText("##xpath", testXpath) || xpathResult == null)
      {
        try
        {
          xpathResult = xpathContext.CreateNavigator().Evaluate(testXpath.ToString());
          if (xpathResult is XPathNodeIterator iter)
            xpathResult = iter.ToNodeList();
        }
        catch (Exception ex)
        {
          xpathResult = ex;
        }
      }

      switch (xpathResult)
      {
        case Exception resEx:
          ImGui.TextColored(ImColor8.Red.AsFloat4(), resEx.Message);
          break;
        case bool resBool:
          ImGui.Text(resBool ? "true" : "false");
          break;
        case string resStr:
          line.Add('"');
          line.Add(resStr);
          line.Add('"');
          ImGui.Text(line.Line);
          break;
        case double resDouble:
          line.Add(resDouble, "f");
          ImGui.Text(line.Line);
          break;
        case List<XmlNode> resNodes:
          line.Add("node-set(");
          line.Add(resNodes.Count);
          line.Add(")");
          ImGui.Text(line.Line);
          for (var i = 0; i < 100 && i < resNodes.Count; i++)
          {
            ImGui.PushID(i);
            var node = resNodes[i];
            line.Clear();
            line.NodeOneLine(node);
            var cursor = ImGui.GetCursorScreenPos();
            if (line.Length > 100)
            {
              line.Length = 100;
              line.Add("...");
            }
            if (Selectable(
                line.Line, node == curNode, float2.Zero, centered: false,
                flags: ImGuiSelectableFlags.AllowOverlap))
              SetCurNode(node);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.RectOnly))
            {
              ImGui.SameLine();
              if (ImGui.SmallButton("Set Context"))
                newXpathContext = node;
            }
            ImGui.PopID();
          }
          if (resNodes.Count > 100)
          {
            line.Clear();
            line.Add(resNodes.Count - 1);
            line.Add(" results hidden");
            ImGui.TextDisabled(line.Line);
          }
          break;
      }
    }

    private void DrawDocTree(XmlNode node, int depth, bool onPath, bool inCur)
    {
      var line = new LineBuilder(buffer);
      var isCur = node == curLineNode;
      inCur |= isCur;
      if (isCur && newNode)
        ImGui.SetScrollHereY();
      var start = ImGui.GetCursorScreenPos();

      if (node is XmlElement el)
      {
        onPath = onPath && (depth >= nodePath.Count || el == nodePath[depth]);
        var children = el.ChildNodes;

        if (ShouldInline(el))
        {
          line.ElementInline(el);
          ImGui.TreeNodeEx(line.Line, LEAF_FLAGS);
        }
        else
        {
          line.ElementOpen(el, !el.HasChildNodes);

          if (newNode)
            ImGui.SetNextItemOpen(onPath || inCur, ImGuiCond.Always);
          if (ImGui.TreeNodeEx(line.Line, TREE_FLAGS))
          {
            for (var i = 0; i < children.Count; i++)
            {
              ImGui.PushID(i);
              DrawDocTree(children[i], depth + 1, onPath, inCur);
              ImGui.PopID();
            }
            ImGui.TreePop();

            TreeIndent();
            line.Clear();
            line.ElementClose(el.Name);
            ImGui.Text(line.Line);
            TreeUnindent();
          }
        }
      }
      else
      {
        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
        ImGui.PushStyleColor(ImGuiCol.Tab, NodeColor(node));

        line.NodeInline(node);
        ImGui.TreeNodeEx(line.Line, LEAF_FLAGS);

        ImGui.PopStyleColor();
        ImGui.PopTextWrapPos();
      }

      if (isCur)
      {
        var childDl = ImGui.GetWindowDrawList();
        parentDl.PushClipRect(childDl.GetClipRectMin(), childDl.GetClipRectMax());

        var endCursor = ImGui.GetCursorScreenPos();
        var end = new float2(endCursor.X + ImGui.GetContentRegionAvail().X, endCursor.Y);

        var cr = new ImColor8(0, 0, 64);
        parentDl.AddRectFilled(start, end, cr);

        parentDl.PopClipRect();
      }
    }

    private static bool ShouldInline(XmlElement el)
    {
      var children = el.ChildNodes;
      if (!el.HasChildNodes)
        return true;
      if (children.Count > 1)
        return false;
      if (children[0] is not XmlCharacterData)
        return false;
      return !children[0].Value.Contains('\n');
    }

    private static ImColor8 NodeColor(XmlNode node) => node switch
    {
      XmlComment => new(180, 255, 180),
      XmlProcessingInstruction => new(180, 180, 180),
      _ => ImColor8.White,
    };

    private static void TreeIndent() => ImGui.Indent(ImGui.GetTreeNodeToLabelSpacing());
    private static void TreeUnindent() => ImGui.Unindent(ImGui.GetTreeNodeToLabelSpacing());
  }
}