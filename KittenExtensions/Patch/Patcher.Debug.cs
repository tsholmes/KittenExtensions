
using System;
using System.Collections.Generic;
using System.Text;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using XPP.Doc;
using XPP.Patch;
using XPP.Path;

namespace KittenExtensions.Patch;

public static partial class XmlPatcher
{
  private class PatchDebugPopup : Popup
  {
    private readonly string title;
    private readonly char[] buffer = new char[65536];

    private bool followTarget = true;
    private bool followPatch = false;
    private bool followNext = true;
    private int execTab = 0;

    private XPNodeRef xpathContext;
    private XPNodeRef newXpathContext = XPNodeRef.Invalid;
    private readonly ImInputString testXpath = new(1024, ".");
    private List<ExecValue> xpathResult;
    private Exception xpathErr;

    private bool newNode = true;
    private XPNodeRef curNode = XPNodeRef.Invalid;
    private XPNodeRef nextCurNode = XPNodeRef.Invalid;

    private readonly ImGuiTreeView<PatchAction, PatchActionAdapter> actionView = new();
    private readonly ImGuiTreeView<int, DummyAdapter> actionDetailView = new(
      clickSelectable: false);
    private readonly ImGuiTreeView<int, DummyAdapter> xpathTestView = new(
      clickSelectable: false);
    private readonly ImGuiTreeView<XPNodeRef, XPNodeRefAdapter> xmlView = new(
      clickable: false,
      defaultExpandDepth: 1);

    private bool HasError => Executor.HasError;

    public PatchDebugPopup()
    {
      title = "PatchDebug####" + PopupId;
      Executor.Step();

      xpathContext = Domain.Doc.LatestRoot.FirstContent.LatestVersion;

      UpdatePath();
    }

    private void ClearXPathResult()
    {
      xpathResult = null;
      xpathErr = null;
    }

    private void UpdatePath()
    {
      if (HasError)
      {
        followPatch = true;
        followTarget = false;
        followNext = true;
      }
      if (followPatch)
        SetCurNode(
          (followNext ? Executor.Next?.Patch : Executor.Last?.Patch)
          ?? XPNodeRef.Invalid);
      else if (followTarget)
        SetCurNode(
          (followNext ? Executor.Next?.Target : Executor.Last?.Target)
          ?? XPNodeRef.Invalid);
      if (followPatch || followTarget)
      {
        if (followNext && Executor.Next != null)
          actionView.SetSelected(Executor.Next);
        else if (!followNext && Executor.Last != null)
          actionView.SetSelected(Executor.Last);
        else
          actionView.ClearSelected();
      }
    }

    private void SetCurNode(XPNodeRef node)
    {
      curNode = node;
      xmlView.SetSelected(node.Type.IsAttribute ? node.Parent : node);
      newNode = true;
      ClearXPathResult();
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

      if (!Executor.Done)
      {
        if (StepButton("Run All Patches", enabled: !HasError, sameLine: false))
        {
          Executor.StepToEnd();
          UpdatePath();
        }
        if (StepButton("Step", enabled: !HasError))
          OnStep(Executor.Step());
        if (StepButton("Step Over", enabled: !HasError))
          OnStep(Executor.StepOver());
        if (StepButton("Step Out", enabled: !HasError))
          OnStep(Executor.StepOut());
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
      if (Selectable("Action Tree", execTab == 0, new float2(150, 0))) execTab = 0;
      if (Selectable("XPath Tester", execTab == 1, new float2(150, 0), sameLine: true)) execTab = 1;

      (_, left) = left.CutY();
      ImGui.SetNextWindowPos(left.Start);
      ImGui.BeginChild("Exec", left.Size, ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar);
      {
        switch (execTab)
        {
          case 0: DrawExecTreeTab(left); break;
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
      ImGui.Text("Follow");
      if (Selectable("Last", !followNext, new float2(100, 0), sameLine: true))
      {
        followNext = false;
        UpdatePath();
      }
      if (Selectable("Next", followNext, new float2(100, 0), sameLine: true))
      {
        followNext = true;
        UpdatePath();
      }
      if (Selectable("Target", followTarget, new float2(100, 0), sameLine: true))
      {
        followTarget = !followTarget;
        followPatch &= !followTarget;
        UpdatePath();
      }
      if (Selectable("Patch", followPatch, new float2(100, 0), sameLine: true))
      {
        followPatch = !followPatch;
        followTarget &= !followPatch;
        UpdatePath();
      }
      (_, right) = right.CutY();
      ImGui.SetNextWindowPos(right.Start);
      ImGui.BeginChild("Doc", right.Size, ImGuiChildFlags.Borders);
      {
        xmlView.Begin();
        DrawDocTree(Domain.Root);
        xmlView.End();
      }
      ImGui.EndChild();

      ImGui.EndPopup();

      newNode = false;
      if (newXpathContext.Valid)
      {
        xpathContext = newXpathContext;
        newXpathContext = XPNodeRef.Invalid;
        ClearXPathResult();
        testXpath.SetValue(".");
      }
      if (nextCurNode.Valid)
      {
        SetCurNode(nextCurNode);
        nextCurNode = XPNodeRef.Invalid;
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

    private void DrawExecTreeTab(ImGuiEx.Space space)
    {
      var hasSelected = actionView.GetSelected(out var selected);
      var (top, bottom) = space.CutY(hasSelected ? space.Size.Y / 2f : 0f);

      ImGui.SetNextWindowPos(top.Start);
      ImGui.BeginChild("##actionTree", top.Size);
      actionView.Begin();
      DrawExecTree(Executor.Root);
      actionView.End();
      ImGui.EndChild();

      if (hasSelected)
      {
        ImGui.SetNextWindowPos(bottom.Start);
        ImGui.BeginChild("##actionSelected", bottom.Size, ImGuiChildFlags.Borders);

        DrawActionDetails(selected);

        ImGui.EndChild();
      }
    }

    private void DrawExecTree(PatchAction action)
    {
      var line = new LineBuilder(buffer);

      line.Add(action.Type);
      if (action.Type.HasPos)
      {
        line.Add(' ');
        line.Add(action.Position);
      }
      if (action.Target.Valid && action.Type is ActionType.WithCtx or { TargetsPatch: false })
      {
        line.Add(' ');
        line.AddNodePath(action.Target,
          action.Type == ActionType.Root ? XPNodeRef.Invalid : action.Context);
      }

      var isNext = action == Executor.Next;

      float4? highlightCr =
        action.Error != null
        ? ImColor8.Red.AsFloat4()
        : isNext
          ? new float4(0.6f, 1f, 0.6f, 1f)
          : null;

      using var nodeScope = actionView.TreeNode(
        action, line.Line, out var open, out _,
        hasChildren: action.Children.Count > 0,
        highlightCr: highlightCr
      );

      if (!open)
        return;
      for (var i = 0; i < action.Children.Count; i++)
      {
        ImGui.PushID(i);
        DrawExecTree(action.Children[i]);
        ImGui.PopID();
      }
    }

    private void DrawActionDetails(PatchAction action)
    {
      var line = new LineBuilder(buffer);
      var type = action.Type;
      line.Add(type);
      ImGui.Text(line.Line);

      if (action.Error != null)
        ImGui.TextColored(ImColor8.Red.AsFloat4(), $"{action.Error}");

      actionDetailView.Begin();

      if (type.HasPos)
      {
        line.Clear();
        line.Add("Position: ");
        line.Add(action.Position);
        using (actionDetailView.TreeNode(
          default, line.Line, out _, out _, clickable: false, hasChildren: false)) { }
      }
      if (action.Patch.Valid)
      {
        line.Clear();
        line.Add("Patch: ");
        line.AddNodePath(action.Patch);
        using (actionDetailView.TreeNode(
          default, line.Line, out _, out var clicked, clickable: true, hasChildren: false))
        {
          if (clicked) nextCurNode = action.Patch;
        }
      }
      if (action.Target.Valid && !action.Target.SameAs(action.Patch))
      {
        line.Clear();
        line.Add("Target: ");
        line.AddNodePath(action.Target);
        using (actionDetailView.TreeNode(
          default, line.Line, out _, out var clicked, clickable: true, hasChildren: false))
        {
          if (clicked) nextCurNode = action.Target;
        }
      }
      if (!string.IsNullOrEmpty(action.TargetPath))
      {
        line.Clear();
        line.Add("Target XPath: ");
        line.Add(action.TargetPath);
        using (actionDetailView.TreeNode(
          default, line.Line, out _, out _, clickable: false, hasChildren: false)) { }
      }
      if (action.TargetResult.Count > 0)
      {
        using (actionDetailView.TreeNode(default, "Target XPath Result:",
          out var open, out _, clickable: false))
        {
          if (open)
          {
            var tgtCount = action.TargetResult.Count;
            for (var i = 0; i < tgtCount && i < 100; i++)
            {
              ImGui.PushID(i);
              DrawExecValueLine(actionDetailView, action.TargetResult[i]);
              ImGui.PopID();
            }

            if (tgtCount > 100)
            {
              line.Clear();
              line.Add(tgtCount - 100);
              line.Add(" results hidden");
              actionDetailView.TreeNodeExtra(line.Line, ImGui.GetStyleColorVec4(ImGuiCol.TextDisabled));
            }
          }
        }
      }
      if (action.Source.Valid)
      {
        line.Clear();
        line.Add("Source: ");
        line.AddNodePath(action.Source);
        using (actionDetailView.TreeNode(
          default, line.Line, out _, out var clicked, clickable: true, hasChildren: false))
        {
          if (clicked) nextCurNode = action.Source;
        }
      }
      if (!string.IsNullOrEmpty(action.SourcePath))
      {
        line.Clear();
        line.Add("Source XPath: ");
        line.Add(action.SourcePath);
        using (actionDetailView.TreeNode(
          default, line.Line, out _, out _, clickable: false, hasChildren: false)) { }
      }
      if (action.SourceResult.Count > 0)
      {
        using (actionDetailView.TreeNode(default, "Source XPath Result:",
          out var open, out _, clickable: false))
        {
          if (open)
          {
            var srcCount = action.SourceResult.Count;
            for (var i = 0; i < srcCount && i < 100; i++)
            {
              ImGui.PushID(i);
              DrawExecValueLine(actionDetailView, action.SourceResult[i]);
              ImGui.PopID();
            }

            if (srcCount > 100)
            {
              line.Clear();
              line.Add(srcCount - 100);
              line.Add(" results hidden");
              actionDetailView.TreeNodeExtra(line.Line, ImGui.GetStyleColorVec4(ImGuiCol.TextDisabled));
            }
          }
        }
      }

      actionDetailView.End();
    }

    private void DrawXPathTest()
    {
      var line = new LineBuilder(buffer);
      line.Add("Context: ");
      line.AddNodePath(xpathContext);
      if (Selectable(line.Line, xpathContext.SameAs(curNode), float2.Zero, centered: false))
        nextCurNode = xpathContext;
      line.Clear();

      ImGui.SetNextItemWidth(-float.Epsilon);
      if (ImGui.InputText("##xpath", testXpath) || (xpathResult == null && xpathErr == null))
      {
        ClearXPathResult();
        try
        {
          xpathResult = [.. XPath.Exec(testXpath.ToString(), xpathContext)];
        }
        catch (Exception ex)
        {
          xpathErr = ex;
        }
      }

      if (xpathErr != null)
      {
        ImGui.TextColored(ImColor8.Red.AsFloat4(), xpathErr.Message);
        return;
      }

      if (xpathResult.Count != 1 || xpathResult[0].Type == XPValueType.NodeSet)
      {
        line.Add("node-set(");
        line.Add(xpathResult.Count);
        line.Add(")");
        ImGui.Text(line.Line);
      }

      xpathTestView.Begin();
      for (var index = 0; index < 100 && xpathResult != null && index < xpathResult.Count; index++)
      {
        var val = xpathResult[index];
        ImGui.PushID(index);
        DrawExecValueLine(xpathTestView, val, true);
        ImGui.PopID();
      }
      xpathTestView.End();

      if (xpathResult != null && xpathResult.Count > 100)
      {
        line.Clear();
        line.Add(xpathResult.Count - 100);
        line.Add(" results hidden");
        ImGui.TextDisabled(line.Line);
      }
    }

    private void DrawExecValueLine<N, A>(
      ImGuiTreeView<N, A> treeView, ExecValue val, bool contextBtn = false)
      where A : ITreeNodeAdapter<N>
    {
      var line = new LineBuilder(buffer);
      switch (val.Type)
      {
        case XPValueType.Bool:
          line.Add(val.Bool ? "true" : "false");
          break;
        case XPValueType.Number:
          line.Add(val.Number, "0.#################");
          break;
        case XPValueType.String:
          line.Add('"');
          line.Add(val.String);
          line.Add('"');
          break;
        case XPValueType.NodeSet:
          var node = val.Node;
          line.NodeOneLine(node);
          if (line.Length > 80)
          {
            line.Length = 77;
            line.Add("...");
          }
          break;
      }
      using (treeView.TreeNode(default, line.Line, out _, out var clicked, hasChildren: false))
      {
        if (val.Type is XPValueType.NodeSet)
        {
          if (clicked)
            nextCurNode = val.Node;
          if (contextBtn && treeView.InlineButton("Set Context"))
            newXpathContext = val.Node;
        }
      }
    }

    private void DrawDocTree(XPNodeRef node)
    {
      var line = new LineBuilder(buffer);

      var hasChildren = node.FirstContent.Valid;
      var inline = true;
      if (node.Type is XPType.Element)
      {
        inline = ShouldInline(node);
        if (inline)
          line.ElementInline(node);
        else
          line.ElementOpen(node, !hasChildren);
      }
      else
        line.NodeInline(node);

      var cr = NodeColor(node).AsFloat4();

      using var nodeScope = xmlView.TreeNode(
        node, line.Line,
        out var open, out _,
        hasChildren: hasChildren && !inline, textCr: cr);

      if (!inline && hasChildren && open)
      {
        var child = node.FirstContent;
        var index = 0;
        while (child.Valid)
        {
          ImGui.PushID(index++);
          DrawDocTree(child);
          child = child.NextSibling;
          ImGui.PopID();
        }

        line.Clear();
        line.ElementClose(node.Name);
        xmlView.TreeNodeExtra(line.Line, cr);
      }
    }

    private static bool ShouldInline(XPNodeRef el)
    {
      var child = el.FirstContent;
      if (!child.Valid)
        return true;
      if (child.NextSibling.Valid)
        return false;
      if (child.Type is not (XPType.Text or XPType.Comment))
        return false;
      return !child.Value.Contains('\n');
    }

    private static ImColor8 NodeColor(XPNodeRef node) => node.Type switch
    {
      XPType.Comment => new(106, 153, 85),
      XPType.ProcInst => new(180, 180, 180),
      _ => ImColor8.White,
    };



    public static bool NodesEqual(ExecValue node1, ExecValue node2) => false;
    public static bool NodeParent(ExecValue node, out ExecValue parent)
    {
      parent = default;
      return false;
    }

    private class PatchActionAdapter : ITreeNodeAdapter<PatchAction>
    {
      public static bool NodesEqual(PatchAction node1, PatchAction node2) => node1 == node2;
      public static bool NodeParent(PatchAction node, out PatchAction parent) =>
        (parent = node.Parent) != null;
    }

    private class XPNodeRefAdapter : ITreeNodeAdapter<XPNodeRef>
    {
      public static bool NodesEqual(XPNodeRef node1, XPNodeRef node2) =>
        node1.FirstVersion.SameAs(node2.FirstVersion);
      public static bool NodeParent(XPNodeRef node, out XPNodeRef parent) =>
        (parent = node.Parent).Valid && parent.Type is not XPType.Document;
    }

    private class DummyAdapter : ITreeNodeAdapter<int>
    {
      public static bool NodeParent(int node, out int parent) { parent = default; return false; }
      public static bool NodesEqual(int node1, int node2) => false;
    }
  }

  public class ErrorPopup : Popup
  {
    private readonly string title;
    private readonly string error;

    private static readonly char[] buffer = new char[0x1000];

    public ErrorPopup(string error, XPNodeRef elementLoc = default, string stringLoc = null)
    {
      var sb = new StringBuilder();
      sb.Append("Patch Error @ ");
      if (elementLoc.Valid)
        AddPath(elementLoc, sb);
      else
        sb.Append(stringLoc ?? "Unknown");
      sb.AppendLine();
      sb.Append(error);
      if (elementLoc.Valid)
      {
        sb.AppendLine().AppendLine();

        var indent = AddAbbrevXmlStart(elementLoc.Parent, sb);
        AddPrevSiblings(elementLoc, sb, indent);
        sb.Append(indent).AppendLine($"<!-- ERROR -->");
        AddXml(elementLoc, sb, indent);
        AddNextSiblings(elementLoc, sb, indent);
        AddAbbrevXmlEnd(elementLoc.Parent, sb);
      }
      this.error = sb.ToString();

      title = "PatchDebug####" + PopupId;
    }

    private static int AddPath(XPNodeRef el, StringBuilder sb)
    {
      if (el.Parent is { Valid: false } or { Type: XPType.Document })
        return 0;

      var depth = AddPath(el.Parent, sb) + 1;
      if (depth > 1)
        sb.Append('/');

      if (depth == 2 && el.Attribute("Path") is { Valid: true } pathAttr)
        sb.Append($"{el.Name}[@Path=\"{pathAttr.Value}\"]");
      else if (el.Attribute("Id") is { Valid: true } idAttr)
        sb.Append($"{el.Name}[@Id=\"{idAttr.Value}\"]");
      else
      {
        var idx = 1;
        var prev = el.PrevSibling;
        while (prev.Valid)
        {
          if (prev.Name == el.Name)
            idx++;
          prev = prev.PrevSibling;
        }
        sb.Append($"{el.Name}[{idx}]");
      }
      return depth;
    }

    private static void AddXml(XPNodeRef node, StringBuilder sb, string indent)
    {
      if (!node.Valid)
        return;

      var line = new LineBuilder(buffer);
      line.Add(indent);
      line.NodeOneLine(node);
      sb.Append(line.Line).AppendLine();

      var child = node.FirstContent;
      if (!child.Valid)
        return;

      var childIndent = indent + "  ";
      while (child.Valid)
      {
        AddXml(child, sb, childIndent);
        child = child.NextSibling;
      }

      line.Clear();
      line.Add(indent);
      line.ElementClose(node.Name);
      sb.Append(line.Line).AppendLine();
    }

    private static string AddAbbrevXmlStart(XPNodeRef el, StringBuilder sb)
    {
      if (!el.Valid || el.Type is XPType.Document)
        return "";

      var indent = AddAbbrevXmlStart(el.Parent, sb);

      AddPrevSiblings(el, sb, indent);

      var line = new LineBuilder(buffer);
      line.Add(indent);
      AddElOpen(el, ref line);

      sb.Append(line.Line).AppendLine();

      return indent + "  ";
    }

    private static void AddAbbrevXmlEnd(XPNodeRef el, StringBuilder sb)
    {
      if (!el.Valid || el.Type is XPType.Document)
        return;

      ReadOnlySpan<char> indent = "";
      var parent = el.Parent;
      while (parent.Valid)
      {
        indent = "  " + indent.ToString();
        parent = parent.Parent;
      }

      while (el.Valid)
      {
        sb.Append(indent).AppendLine($"</{el.Name.Local}>");
        AddNextSiblings(el, sb, indent);
        el = el.Parent;
        if (el.Valid)
          indent = indent[2..];
      }
    }

    private static void AddPrevSiblings(XPNodeRef el, StringBuilder sb, string indent)
    {
      var prev = el;
      for (var i = 0; i < 2 && prev.PrevSibling is { Valid: true } prevSib; i++)
        prev = prevSib;

      if (prev.PrevSibling.Valid)
        sb.Append(indent).AppendLine("...");
      while (prev.Valid && !prev.SameAs(el))
      {
        sb.Append(indent);
        AddCollapsedXml(prev, sb);
        sb.AppendLine();
        prev = prev.NextSibling;
      }
    }

    private static void AddNextSiblings(XPNodeRef el, StringBuilder sb, ReadOnlySpan<char> indent)
    {
      var next = el;
      for (var i = 0; i < 2 && next.NextSibling is { Valid: true } nextSib; i++)
      {
        next = nextSib;
        sb.Append(indent);
        AddCollapsedXml(next, sb);
        sb.AppendLine();
      }
      if (next.NextSibling.Valid)
        sb.Append(indent).AppendLine("...");
    }

    private static void AddCollapsedXml(XPNodeRef el, StringBuilder sb)
    {
      var line = new LineBuilder(buffer);

      AddElOpen(el, ref line);

      var child = el.FirstContent;
      if (child.Valid)
      {
        if (!child.NextSibling.Valid && child.Type == XPType.Text && child.Value.Length <= 32)
          line.Add(child.Value);
        else
          line.Add("...");

        line.ElementClose(el.Name);
      }

      sb.Append(line.Line);
    }

    private static void AddElOpen(XPNodeRef el, ref LineBuilder line)
    {
      line.ElOpenStart(el.Name);

      var attr = el.FirstAttr;
      while (attr.Valid)
      {
        line.Add(' ');
        line.ElAttr(attr.Name, attr.Value);
        attr = attr.NextSibling;
      }

      line.ElOpenEnd(!el.FirstContent.Valid);
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