
using System;
using System.Collections;
using System.Collections.Generic;
using System.Xml;

namespace KittenExtensions.Patch;

public record class DeserializedPatch(string Id, XmlElement Element, XmlPatch Patch, Exception Error);

public class PatchExecutor
{
  public enum ExecState
  {
    PatchStart,
    PatchEnd,
    ExecStart,
    ExecEnd,
    ActionStart,
    ActionEnd,
    End,
    Error,
  }

  private readonly IEnumerator<ExecState> walk;

  private XmlElement curElement;
  private XmlElement lastElement;
  private XmlElement errElement;
  public XmlElement CurElement => errElement ?? curElement;
  public DeserializedPatch CurPatch { get; private set; }
  public OpExecution CurExec { get; private set; }
  public OpAction CurAction { get; private set; }

  public XmlNode CurTarget => CurAction?.Target;
  public XmlNode CurNav => CurExec?.Context.Nav.UnderlyingObject as XmlNode;

  public ExecState LastState { get; private set; } = ExecState.ExecStart;

  public Exception Error { get; private set; }

  public PatchExecutor(OpExecContext ctx, IEnumerable<DeserializedPatch> patches)
  {
    walk = WalkPatches(ctx, patches).GetEnumerator();
    Next(out _);
  }

  public void ToEnd()
  {
    while (Next(out _)) ;
  }

  public bool CanNextPatch => Error == null && CurPatch != null;
  public bool NextPatch()
  {
    if (LastState == ExecState.PatchEnd)
      return Next(out _);
    while (Next(out var state))
    {
      if (state == ExecState.PatchEnd)
        return Next(out _);
    }
    return false;
  }

  public bool CanStep => Error == null && LastState != ExecState.End;
  public bool Step() => Next(out _);

  public bool CanStepOver => Error == null && CurExec != null && LastState == ExecState.ExecStart;
  public bool StepOver()
  {
    var cur = CurExec;
    while (Next(out var state))
    {
      if (state == ExecState.ExecEnd && CurExec == cur)
        return Next(out _);
    }
    return false;
  }

  public bool CanStepOut => Error == null && CurExec != null;
  public bool StepOut()
  {
    var depth = LastState == ExecState.ExecStart ? 1 : 0;
    while (Next(out var state))
    {
      if (state == ExecState.ExecStart)
        depth++;
      else if (state == ExecState.ExecEnd)
        depth--;
      if (depth < 0)
        return true;
    }
    return false;
  }

  public bool CanNextAction => Error == null && LastState != ExecState.End;
  public bool ToNextAction()
  {
    while (Next(out var state))
    {
      if (state is ExecState.ActionStart or ExecState.ActionEnd)
        return true;
    }
    return false;
  }

  public bool CanNextOp => Error == null && LastState != ExecState.End;
  public bool ToNextOp()
  {
    var hasNonEnd = false;
    while (Next(out var state))
    {
      if (state == ExecState.ExecStart)
        return true;
      else if (state == ExecState.ExecEnd && hasNonEnd)
        return true;
      hasNonEnd |= state != ExecState.ExecEnd;
    }
    return false;
  }

  private bool Next(out ExecState state)
  {
    if (Error != null)
    {
      state = ExecState.Error;
      return false;
    }
    try
    {
      if (!walk.MoveNext())
      {
        state = LastState = ExecState.End;
        return false;
      }
      state = LastState = walk.Current;
      return true;
    }
    catch (Exception ex)
    {
      Error = ex;
      errElement = lastElement;
      state = ExecState.Error;
      return false;
    }
  }

  private IEnumerable<ExecState> WalkPatches(OpExecContext ctx, IEnumerable<DeserializedPatch> patches)
  {
    foreach (var patch in patches)
    {
      CurPatch = patch;
      var patchCtx = ctx.WithPatch(patch);
      if (patch.Error != null)
      {
        Error = patch.Error;
        errElement = patch.Element;
        yield return ExecState.Error;
        break;
      }
      using var _ = WithElement(patch.Element);
      var exec = patchCtx.Execution(patch.Patch);
      CurExec = exec;
      yield return ExecState.PatchStart;
      CurExec = null;
      foreach (var state in WalkExec([exec]))
        yield return state;
      yield return ExecState.PatchEnd;
      exec.Context.End();
      CurPatch = null;
    }
  }

  private IEnumerable<ExecState> WalkExec(IEnumerable<OpExecution> execs)
  {
    var prev = CurExec;
    foreach (var exec in execs)
    {
      using var _ = WithElement(exec.Op.Element);
      CurExec = exec;
      yield return ExecState.ExecStart;
      if (exec.Op is IXmlLeafOp leaf)
      {
        foreach (var state in WalkAction(leaf.ExecuteLeaf(exec.Context)))
          yield return state;
      }
      else
      {
        foreach (var state in WalkExec(exec.Op.Execute(exec.Context)))
          yield return state;
      }
      yield return ExecState.ExecEnd;
      exec.Context.End();
    }
    CurExec = prev;
  }

  private IEnumerable<ExecState> WalkAction(IEnumerable<OpAction> actions)
  {
    foreach (var action in actions)
    {
      CurAction = action;
      yield return ExecState.ActionStart;
      action.Run();
      yield return ExecState.ActionEnd;
      action.Context.End();
    }
    CurAction = null;
  }

  private ElementScope WithElement(XmlElement el)
  {
    var prev = CurElement;
    curElement = el;
    lastElement = el;
    return new(this, prev);
  }

  private readonly struct ElementScope(PatchExecutor executor, XmlElement prev) : IDisposable
  {
    private readonly PatchExecutor executor = executor;
    private readonly XmlElement prev = prev;
    public void Dispose() => executor.curElement = prev;
  }
}

public record class OpExecution(XmlOp Op, OpExecContext Context);

public enum OpActionType
{
  Update,
  Copy,
  Delete,
}

public enum OpPosition
{
  Default,
  Replace,
  Merge,
  Append,
  Prepend,
  Before,
  After
}

public record class OpAction(
  XmlOp Op,
  OpExecContext Context,
  OpActionType Type,
  XmlNode Target,
  object Source = null,
  OpPosition Pos = OpPosition.Default
)
{
  public const string MergeIdAttr = "_MergeId";
  public const string DefaultMergeId = "Id";
  public const string AnyMergeId = "*";
  public const string NoneMergeId = "-";
  public const string MergePosAttr = "_MergePos";

  public static XmlNode ToNode(XmlNode cur, object child) => child switch
  {
    XmlNode nodeVal => cur.OwnerDocument.ImportNode(nodeVal, true),
    string strVal => cur.OwnerDocument.CreateTextNode(strVal),
    _ => throw new InvalidOperationException($"Invalid child type {child?.GetType()?.Name}"),
  };

  public void Run()
  {
    switch (Type)
    {
      case OpActionType.Update or OpActionType.Copy: Update(); break;
      case OpActionType.Delete: Delete(); break;
      default: throw new InvalidOperationException($"Invalid Action {Type}");
    }
  }

  private void Delete() => Target.ParentNode.RemoveChild(Target);

  private void Update()
  {
    switch (Target)
    {
      case XmlElement el: UpdateElement(el, Source); break;
      case XmlCharacterData or XmlAttribute: UpdateText(Target, Source); break;
      default: throw new InvalidOperationException($"Invalid Update target {Target.GetType()}");
    }
  }

  private void UpdateElement(XmlElement target, object source)
  {
    if (Pos is OpPosition.Merge or OpPosition.Default)
    {
      Merge(target, source);
      return;
    }

    var inserter = new Inserter(target, Pos == OpPosition.Replace ? OpPosition.Before : Pos);
    switch (source)
    {
      case IList srcList:
        foreach (var srcVal in srcList)
        {
          inserter.Insert(ToNode(target, srcVal));
        }
        break;
      case bool srcBool: inserter.Insert(ToNode(target, srcBool.ToString())); break;
      case double srcDouble: inserter.Insert(ToNode(target, srcDouble.ToString("f"))); break;
      case string srcString: inserter.Insert(ToNode(target, srcString)); break;
      default:
        throw new InvalidOperationException($"Invalid Update source {source?.GetType()}");
    }
    if (Pos == OpPosition.Replace)
      target.ParentNode.RemoveChild(target);
  }

  private void Merge(XmlElement target, object source)
  {
    if (source is not IList list)
      throw new InvalidOperationException($"Merge source must be elements, not {source?.GetType()?.Name}");

    foreach (var node in list)
    {
      if (node is not XmlElement el)
        throw new InvalidOperationException($"Merge source must be element, not {node?.GetType()?.Name}");

      MergeElements(target, el);
    }
  }

  private void MergeElements(XmlElement target, XmlElement source)
  {
    var attrs = source.Attributes;
    for (var i = 0; i < attrs.Count; i++)
    {
      var attr = attrs[i];
      if (attr.Name is MergePosAttr or MergeIdAttr)
        continue;
      target.SetAttribute(attr.Name, attr.Value);
    }

    var mergePos = OpPosition.Append;
    if (source.GetAttribute(MergePosAttr) is string posStr && posStr != "")
    {
      if (!Enum.TryParse(posStr, out mergePos))
        throw new InvalidOperationException($"Invalid {MergePosAttr} '{posStr}'");

      mergePos = mergePos switch
      {
        OpPosition.Append or OpPosition.Prepend => mergePos,
        _ => throw new InvalidOperationException($"Invalid {MergePosAttr} '{mergePos}'"),
      };
    }

    var inserter = new Inserter(target, mergePos);

    var children = source.ChildNodes;
    for (var i = 0; i < children.Count; i++)
    {
      switch (children[i])
      {
        case XmlText text:
          inserter.Insert(target.OwnerDocument.ImportNode(text, true));
          break;
        case XmlElement el:
          string idAttr = GetMergeId(el);
          var id = el.GetAttribute(idAttr);
          if (FindChildElement(target, el.Name, idAttr, id) is XmlElement tgtEl)
            MergeElements(tgtEl, el);
          else
            inserter.Insert(target.OwnerDocument.ImportNode(el, true));
          break;
        default:
          throw new InvalidOperationException($"Invalid merge child type {children[i].GetType().Name}");
      }
    }
  }

  private static string GetMergeId(XmlElement el)
  {
    var id = el.GetAttribute(MergeIdAttr);
    if (!string.IsNullOrEmpty(id))
      return id;
    if (el.HasAttribute(DefaultMergeId))
      return DefaultMergeId;
    return AnyMergeId;
  }

  private static XmlElement FindChildElement(XmlElement parent, string name, string idAttr, string id)
  {
    if (idAttr == NoneMergeId)
      return null;
    var children = parent.ChildNodes;
    var isAny = idAttr == AnyMergeId;
    for (var i = 0; i < children.Count; i++)
    {
      if (children[i] is not XmlElement el)
        continue;
      if (el.Name != name)
        continue;
      if (isAny || el.GetAttribute(idAttr) == id)
        return el;
    }
    return null;
  }

  private void UpdateText(XmlNode target, object source)
  {
    var copyVal = source switch
    {
      bool srcBool => srcBool ? "true" : "false",
      double srcDouble => srcDouble.ToString("f"),
      string srcString => srcString,
      IList { Count: > 0 } list => list[0] switch
      {
        string srcString => srcString,
        XmlNode srcNode => srcNode.Value,
        _ => throw new InvalidOperationException($"Invalid Update source {list[0]?.GetType()}"),
      },
      IList => "",
      _ => throw new InvalidOperationException($"Invalid Update source {source?.GetType()}"),
    } ?? "";

    target.Value = Pos switch
    {
      OpPosition.Replace or OpPosition.Default => copyVal,
      OpPosition.Append => target.Value + copyVal,
      OpPosition.Prepend => copyVal + target.Value,
      _ => throw new InvalidOperationException($"Invalid Pos {Pos} for value Update"),
    };
  }

  public struct Inserter(XmlElement el, OpPosition pos)
  {
    private readonly XmlElement el = el;
    private readonly OpPosition pos = pos;
    private XmlNode last;

    public void Insert(XmlNode node)
    {
      switch (pos)
      {
        case OpPosition.Replace:
        case OpPosition.Append:
          el.AppendChild(node);
          break;
        case OpPosition.Prepend:
          el.InsertAfter(node, last);
          last = node;
          break;
        case OpPosition.After:
          el.ParentNode.InsertAfter(node, last ?? el);
          last = node;
          break;
        case OpPosition.Before:
          if (last == null)
            el.ParentNode.InsertBefore(node, el);
          else
            el.InsertAfter(node, last);
          last = node;
          break;
        default:
          throw new InvalidOperationException($"Invalid insert Pos {pos}");
      }
    }
  }
}