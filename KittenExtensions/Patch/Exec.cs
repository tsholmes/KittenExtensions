
using System;
using System.Collections;
using System.Collections.Generic;
using System.Xml;

namespace KittenExtensions.Patch;

public class PatchExecutor
{
  public enum ExecState
  {
    ExecStart,
    ExecEnd,
    ActionStart,
    ActionEnd,
    End,
  }

  private readonly IEnumerator<ExecState> walk;

  public OpExecution CurExec { get; private set; }
  public OpAction CurAction { get; private set; }

  public XmlElement CurOpElement => CurExec?.Op?.Element;
  public XmlNode CurTarget => CurAction?.Target;
  public XmlNode CurNav => CurExec?.Context.Nav.UnderlyingObject as XmlNode;

  public ExecState LastState { get; private set; } = ExecState.ExecStart;

  public PatchExecutor(OpExecution rootExec)
  {
    walk = WalkExec([rootExec]).GetEnumerator();
    Next(out _);
  }

  public void ToEnd()
  {
    while (Next(out _)) ;
  }

  public bool CanStep => LastState != ExecState.End;
  public bool Step() => Next(out _);

  public bool CanStepOver => CurExec != null && LastState == ExecState.ExecStart;
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

  public bool CanStepOut => CurExec != null;
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

  public bool CanNextAction => LastState != ExecState.End;
  public bool ToNextAction()
  {
    while (Next(out var state))
    {
      if (state == ExecState.ActionStart)
        return true;
    }
    return false;
  }

  public bool CanNextOp => LastState != ExecState.End;
  public bool ToNextOp()
  {
    while (Next(out var state))
    {
      if (state == ExecState.ExecStart)
        return true;
    }
    return false;
  }

  private bool Next(out ExecState state)
  {
    if (!walk.MoveNext())
    {
      state = LastState = ExecState.End;
      return false;
    }
    state = LastState = walk.Current;
    return true;
  }

  private IEnumerable<ExecState> WalkExec(IEnumerable<OpExecution> execs)
  {
    var prev = CurExec;
    foreach (var exec in execs)
    {
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
    _ => throw new InvalidOperationException($"{child?.GetType()?.Name}"),
  };

  public void Run()
  {
    switch (Type)
    {
      case OpActionType.Update or OpActionType.Copy: Update(); break;
      case OpActionType.Delete: Delete(); break;
      default: throw new InvalidOperationException($"{Type}");
    }
  }

  private void Delete() => Target.ParentNode.RemoveChild(Target);

  private void Update()
  {
    switch (Target)
    {
      case XmlElement el: UpdateElement(el, Source); break;
      case XmlCharacterData or XmlAttribute: UpdateText(Target, Source); break;
      default: throw new InvalidOperationException($"{Target.GetType()}");
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
        throw new InvalidOperationException($"{source?.GetType()}");
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
        _ => throw new InvalidOperationException($"{list[0]?.GetType()}"),
      },
      IList => "",
      _ => throw new InvalidOperationException($"{source?.GetType()}"),
    } ?? "";

    target.Value = Pos switch
    {
      OpPosition.Replace or OpPosition.Default => copyVal,
      OpPosition.Append => target.Value + copyVal,
      OpPosition.Prepend => copyVal + target.Value,
      _ => throw new InvalidOperationException($"{Pos}"),
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
          throw new InvalidOperationException($"{pos}");
      }
    }
  }
}