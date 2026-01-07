
using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Serialization;

namespace KittenExtensions.Patch;

public class XmlUpdateOp : XmlChildrenOp, IXmlLeafOp
{
  [XmlAttribute("Pos")]
  public OpPosition Pos = OpPosition.Default;

  public override IEnumerable<OpExecution> Execute(OpExecContext ctx) => throw new InvalidOperationException();

  public IEnumerable<OpAction> ExecuteLeaf(OpExecContext ctx)
  {
    var actions = new List<OpAction>();
    foreach (var node in ctx.Nav.Select(Path).ToNodeList())
      actions.Add(ctx.Action(OpActionType.Update, node, Source: Children, Pos: Pos));
    return actions;
  }
}