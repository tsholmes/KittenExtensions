
using System;
using System.Collections.Generic;

namespace KittenExtensions.Patch;

public class XmlDeleteOp : XmlOp, IXmlLeafOp
{
  public override IEnumerable<OpExecution> Execute(OpExecContext ctx) => throw new InvalidOperationException();

  public IEnumerable<OpAction> ExecuteLeaf(OpExecContext ctx)
  {
    var actions = new List<OpAction>();
    foreach (var node in ctx.Nav.Select(Path).ToNodeList())
      actions.Add(ctx.Action(OpActionType.Delete, node));
    return actions;
  }
}