
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
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
    private readonly PatchExecutor executor;
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

    private bool HasError => executor.Error != null;

    public PatchDebugPopup()
    {
      title = "PatchDebug####" + PopupId;
      context = DebugOpExecContext.NewRoot(RootNode.CreateNavigator());
      executor = new(context, GetPatches());

      xpathContext = RootNode;

      UpdatePath();
    }

    private void UpdatePath()
    {
      if (HasError)
      {
        followPatch = true;
        followTarget = false;
      }
      if (followPatch)
        SetCurNode(executor.CurElement);
      else if (followTarget)
        SetCurNode(executor.CurTarget ?? executor.CurNav);
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

    protected override void OnDrawUi()
    {
      var padding = new float2(50, 50);
      var size = float2.Unpack(Program.GetWindow().Size) - padding * 2;

      ImGui.SetNextWindowSize(size, ImGuiCond.Always);
      ImGui.SetNextWindowPos(ImGui.GetMainViewport().Pos + padding, ImGuiCond.Always);
      ImGui.OpenPopup(title);
      ImGui.BeginPopup(
        title, ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.Popup);

      var frameX = new float2(ImGui.GetStyle().FramePadding.X, 0);

      const string titleText = "KittenExtensions Patch Debug";
      ImGuiEx.CenterTextCursor(ImGuiEx.AvailableSpace(), titleText);
      ImGui.Text(titleText);

      if (executor.LastState != PatchExecutor.ExecState.End)
      {
        if (StepButton("Run All Patches", enabled: !HasError, sameLine: false))
        {
          executor.ToEnd();
          UpdatePath();
        }
        if (StepButton("Next Patch", enabled: executor.CanNextPatch))
          OnStep(executor.NextPatch());
        if (StepButton("Step", enabled: executor.CanStep))
          OnStep(executor.Step());
        if (StepButton("Step Over", enabled: executor.CanStepOver))
          OnStep(executor.StepOver());
        if (StepButton("Step Out", enabled: executor.CanStepOut))
          OnStep(executor.StepOut());
        if (StepButton("Next Op", enabled: executor.CanNextOp))
          OnStep(executor.ToNextOp());
        if (StepButton("Next Action", enabled: executor.CanNextAction))
          OnStep(executor.ToNextAction());
      }
      else
      {
        if (ImGui.Button("Start Game"))
          Active = false;
      }

      ImGui.Separator();

      var (left, right) = ImGuiEx.AvailableSpace().SplitX();

      if (newNode && HasError)
        execTab = 0;

      ImGui.SetCursorScreenPos(left.Start + frameX);
      if (Selectable("Exec Tree", execTab == 0, new float2(150, 0))) execTab = 0;
      if (Selectable("XPath Tester", execTab == 1, new float2(150, 0), sameLine: true)) execTab = 1;

      (_, left) = left.CutY();
      ImGui.SetNextWindowPos(left.Start);
      ImGui.BeginChild("Exec", left.Size, ImGuiChildFlags.Borders);
      using (var bg = ImGuiEx.SplitBg())
      {
        switch (execTab)
        {
          case 0: DrawExecTree(context, null); break;
          case 1: DrawXPathTest(); break;
          default: break;
        }
      }
      ImGui.EndChild();

      if (newNode && HasError)
      {
        followPatch = true;
        followTarget = false;
      }

      ImGui.SetCursorScreenPos(right.Start + frameX);
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
      (_, right) = right.CutY();
      ImGui.SetNextWindowPos(right.Start);
      ImGui.BeginChild("Doc", right.Size, ImGuiChildFlags.Borders);
      using (var bg = ImGuiEx.SplitBg())
      {
        DrawDocTree(RootNode, 0, curNode != null, false);
      }
      ImGui.EndChild();

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

    private static bool StepButton(ImString text, bool enabled = true, bool sameLine = true)
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
      UpdatePath();
    }

    private const ImGuiTreeNodeFlags TREE_FLAGS = ImGuiTreeNodeFlags.DrawLinesFull;
    private const ImGuiTreeNodeFlags LEAF_FLAGS =
      TREE_FLAGS | ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;

    private void DrawExecTree(DebugOpExecContext ctx, DebugOpExecContext parent)
    {
      var line = new LineBuilder(buffer);
      var pop = false;
      var open = true;
      var outline = OutlineType.None;
      var space = ImGuiEx.AvailableSpace();
      if (parent != null)
      {
        if (ctx.Type == ContextType.Patch && ctx.ContextPatch.Error == null)
          ctx = ctx.Children[0];
        var isCur = ctx.Type switch
        {
          ContextType.Patch => ctx.ContextPatch == executor.CurPatch,
          ContextType.Exec => ctx.ContextExec == executor.CurExec && executor.CurAction == null,
          ContextType.Action => ctx.ContextAction == executor.CurAction,
          _ => false,
        };
        if (isCur)
          outline |= OutlineType.Left;

        if (isCur && HasError)
          outline = OutlineType.All;

        ReadOnlySpan<char> text;

        if (ctx.Type == ContextType.Patch)
        {
          text = ctx.ContextPatch.Id;
          if (isCur)
            outline = OutlineType.All;
        }
        else if (ctx.Type == ContextType.Nav)
        {
          line.AddNodePath(ctx.Nav.UnderlyingObject as XmlNode, parent.Nav.UnderlyingObject as XmlNode);
          text = line.Line;
        }
        else if (ctx.Type == ContextType.Exec)
        {
          var op = ctx.ContextExec.Op;
          if (isCur)
          {
            outline |= executor.LastState switch
            {
              PatchExecutor.ExecState.ExecEnd => OutlineType.Bottom,
              _ => OutlineType.Top,
            };
          }
          if (op is XmlPatch patch)
            text = patch.Id;
          else
          {
            line.ElementOpen(op.Element, !op.Element.HasChildNodes);
            text = line.Line;
          }
        }
        else if (ctx.Type == ContextType.Action)
        {
          var action = ctx.ContextAction;
          if (isCur)
          {
            outline |= executor.LastState switch
            {
              PatchExecutor.ExecState.ActionEnd => OutlineType.Bottom,
              _ => OutlineType.Top,
            };
          }
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
          line.AddXPathVal(action.Source);
          text = line.Line;
        }
        else
        {
          text = "???";
        }

        if (isCur && newNode)
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
        if (isCur && HasError && open)
        {
          if (!pop)
            TreeIndent();
          var ex = executor.Error;
          ImGui.TextColored(new(1, 0, 0, 1), ex.Message);
          ImGui.TextColored(new(1, 0, 0, 1), ex.StackTrace);
          if (!pop)
            TreeUnindent();
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

      if (outline != 0)
      {
        const float OUTLINE_WIDTH = 3;
        (space, _) = space.CutY();
        space = space.TreeIndent().Expand(left: 4, bottom: -4);
        if (HasError)
          ImGuiEx.BgOutline(space, outline, OUTLINE_WIDTH, cr: ImColor8.Red);
        else
          ImGuiEx.BgOutline(space, outline, OUTLINE_WIDTH, styleCr: ImGuiCol.Button);
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
      var space = ImGuiEx.AvailableSpace();

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
        ImGui.PushStyleColor(ImGuiCol.Text, NodeColor(node));

        line.NodeInline(node);
        ImGui.TreeNodeEx(line.Line, LEAF_FLAGS);

        ImGui.PopStyleColor();
        ImGui.PopTextWrapPos();
      }

      if (isCur)
      {
        (space, _) = space.CutY();
        ImGuiEx.BgHighlight(space, styleCr: ImGuiCol.FrameBg);
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
      XmlComment => new(106, 153, 85),
      XmlProcessingInstruction => new(180, 180, 180),
      _ => ImColor8.White,
    };

    private static void TreeIndent() => ImGui.Indent(ImGui.GetTreeNodeToLabelSpacing());
    private static void TreeUnindent() => ImGui.Unindent(ImGui.GetTreeNodeToLabelSpacing());
  }

  public class ErrorPopup : Popup
  {
    private readonly string title;
    private readonly string error;

    private static readonly char[] buffer = new char[0x1000];

    public ErrorPopup(string error, XmlElement elementLoc = null, string stringLoc = null)
    {
      var sb = new StringBuilder();
      sb.Append("Patch Error @ ");
      if (elementLoc != null)
        AddPath(elementLoc, sb);
      else
        sb.Append(stringLoc ?? "Unknown");
      sb.AppendLine();
      sb.Append(error);
      if (elementLoc != null)
      {
        sb.AppendLine().AppendLine();

        var indent = AddAbbrevXmlStart(elementLoc?.ParentNode as XmlElement, sb);
        AddPrevSiblings(elementLoc, sb, indent);
        sb.Append(indent).AppendLine($"<!-- ERROR -->");
        AddXml(elementLoc, sb, indent);
        AddNextSiblings(elementLoc, sb, indent);
        AddAbbrevXmlEnd(elementLoc?.ParentNode as XmlElement, sb);
      }
      this.error = sb.ToString();

      title = "PatchDebug####" + PopupId;
    }

    private static int AddPath(XmlElement el, StringBuilder sb)
    {
      if (el?.ParentNode is null or XmlDocument)
        return 0;

      var depth = AddPath(el.ParentNode as XmlElement, sb) + 1;
      if (depth > 1)
        sb.Append('/');

      if (depth == 2 && el.GetAttributeNode("Path") is XmlAttribute pathAttr)
        sb.Append($"{el.Name}[@Path=\"{pathAttr.Value}\"]");
      else if (el.GetAttributeNode("Id") is XmlAttribute idAttr)
        sb.Append($"{el.Name}[@Id=\"{idAttr.Value}\"]");
      else
      {
        var idx = 1;
        var prev = el.PreviousSibling;
        while (prev != null)
        {
          if (prev.Name == el.Name)
            idx++;
          prev = prev.PreviousSibling;
        }
        sb.Append($"{el.Name}[{idx}]");
      }
      return depth;
    }

    private static void AddXml(XmlNode node, StringBuilder sb, string indent)
    {
      if (node == null)
        return;

      var line = new LineBuilder(buffer);
      line.Add(indent);
      line.NodeOneLine(node);
      sb.Append(line.Line).AppendLine();

      if (!node.HasChildNodes)
        return;

      var children = node.ChildNodes;
      var childIndent = indent + "  ";
      for (var i = 0; i < children.Count; i++)
        AddXml(children[i], sb, childIndent);

      line.Clear();
      line.Add(indent);
      line.ElementClose(node.Name);
      sb.Append(line.Line).AppendLine();
    }

    private static string AddAbbrevXmlStart(XmlElement el, StringBuilder sb)
    {
      if (el == null)
        return "";

      var indent = AddAbbrevXmlStart(el.ParentNode as XmlElement, sb);

      AddPrevSiblings(el, sb, indent);

      var line = new LineBuilder(buffer);
      line.Add(indent);
      AddElOpen(el, ref line);

      sb.Append(line.Line).AppendLine();

      return indent + "  ";
    }

    private static void AddAbbrevXmlEnd(XmlElement el, StringBuilder sb)
    {
      if (el == null)
        return;

      ReadOnlySpan<char> indent = "";
      var parent = el.ParentNode as XmlElement;
      while (parent != null)
      {
        indent = "  " + indent.ToString();
        parent = parent.ParentNode as XmlElement;
      }

      while (el != null)
      {
        sb.Append(indent).AppendLine($"</{el.Name}>");
        AddNextSiblings(el, sb, indent);
        el = el.ParentNode as XmlElement;
        if (el != null)
          indent = indent[2..];
      }
    }

    private static void AddPrevSiblings(XmlElement el, StringBuilder sb, string indent)
    {
      var prev = el;
      for (var i = 0; i < 2 && prev.PreviousSibling is XmlElement prevSib; i++)
        prev = prevSib;

      if (prev.PreviousSibling != null)
        sb.Append(indent).AppendLine("...");
      while (prev != null && prev != el)
      {
        sb.Append(indent);
        AddCollapsedXml(prev, sb);
        sb.AppendLine();
        prev = prev.NextSibling as XmlElement;
      }
    }

    private static void AddNextSiblings(XmlElement el, StringBuilder sb, ReadOnlySpan<char> indent)
    {
      var next = el;
      for (var i = 0; i < 2 && next.NextSibling is XmlElement nextSib; i++)
      {
        next = nextSib;
        sb.Append(indent);
        AddCollapsedXml(next, sb);
        sb.AppendLine();
      }
      if (next.NextSibling != null)
        sb.Append(indent).AppendLine("...");
    }

    private static void AddCollapsedXml(XmlElement el, StringBuilder sb)
    {
      var line = new LineBuilder(buffer);

      AddElOpen(el, ref line);

      if (el.ChildNodes.Count > 0)
      {
        if (el.ChildNodes.Count == 1 && el.ChildNodes[0] is XmlText text && text.Value.Length <= 32)
          line.Add(text.Value);
        else
          line.Add("...");

        line.ElementClose(el.Name);
      }

      sb.Append(line.Line);
    }

    private static void AddElOpen(XmlElement el, ref LineBuilder line)
    {
      line.ElOpenStart(el.Name);

      var attrs = el.Attributes;
      for (var i = 0; i < attrs.Count; i++)
      {
        var attr = attrs[i];
        if (attr.Name == "PathKey")
          continue;
        line.Add(' ');
        line.ElAttr(attr.Name, attr.Value);
      }

      line.ElOpenEnd(!el.HasChildNodes);
    }

    protected override void OnDrawUi()
    {
      ImGui.SetNextWindowSize(float2.Unpack(Program.GetWindow().Size) * 0.8f);
      ImGui.OpenPopup(title);
      ImGui.BeginPopup(
        title, ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.Popup);
      ImGuiHelper.SetCurrentWindowToCenter();

      var space = ImGuiEx.AvailableSpace();
      const string titleText = "KittenExtensions Patch Error";
      ImGuiEx.CenterTextCursor(space, titleText);
      ImGui.TextColored(new float4(1, 0, 0, 1), titleText);

      (_, space) = space.CutY();
      var (left, right) = space.SplitX();

      ImGui.SetCursorScreenPos(left.Start);
      if (ImGui.Button("Copy To Clipboard", new float2(left.Size.X, 0)))
        ImGui.SetClipboardText(error);
      ImGui.Separator();
      (_, space) = space.CutY();

      ImGui.SetCursorScreenPos(right.Start);
      if (ImGui.Button("Exit", new float2(right.Size.X, 0)))
        Active = false;

      ImGui.SetNextWindowPos(space.Start);
      ImGui.SetNextWindowSize(space.Size);
      ImGui.BeginChild("Error", windowFlags: ImGuiWindowFlags.HorizontalScrollbar);
      ImGui.TextColored(new float4(1, 0, 0, 1), error);
      ImGui.EndChild();

      ImGui.EndPopup();
    }
  }
}