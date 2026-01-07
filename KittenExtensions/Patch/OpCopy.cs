
using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Serialization;
using System.Xml.XPath;

namespace KittenExtensions.Patch;

public class XmlCopyOp : XmlOp, IXmlLeafOp
{
  [XmlAttribute("Pos")]
  public OpPosition Pos;

  [XmlAttribute("From")]
  public string From;

  public override IEnumerable<OpExecution> Execute(OpExecContext ctx) => throw new InvalidOperationException();

  public IEnumerable<OpAction> ExecuteLeaf(OpExecContext ctx)
  {
    var source = ctx.Nav.Evaluate(From);
    if (source is XPathNodeIterator srcNodes)
      source = srcNodes.ToNodeList();

    var actions = new List<OpAction>();
    var targets = ctx.Nav.Select(Path).ToNodeList();
    foreach (var target in targets)
      actions.Add(ctx.Action(OpActionType.Copy, target, Source: source, Pos: Pos));

    return actions;
  }
}