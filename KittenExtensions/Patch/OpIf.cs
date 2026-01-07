
using System.Collections.Generic;
using System.Xml;
using System.Xml.Serialization;
using System.Xml.XPath;

namespace KittenExtensions.Patch;

public class XmlIfOp : XmlOp
{
  [XmlElement("Any")]
  public XmlOpCollection Any = new();
  [XmlElement("None")]
  public XmlOpCollection None = new();

  public override IEnumerable<OpExecution> Execute(OpExecContext ctx)
  {
    if (IsAny(ctx.Nav, Path))
      yield return ctx.Execution(Any);
    else
      yield return ctx.Execution(None);
  }

  public static bool IsAny(XPathNavigator nav, string path) => nav.Evaluate(path) switch
  {
    bool val => val,
    double val => val != 0 && !double.IsNaN(val),
    string val => !string.IsNullOrEmpty(val),
    XPathNodeIterator val => val.MoveNext(),
    _ => false,
  };
}

public class XmlIfAnyOp : XmlOpCollection
{
  public override IEnumerable<OpExecution> Execute(OpExecContext ctx)
  {
    if (XmlIfOp.IsAny(ctx.Nav, Path))
      return base.Execute(ctx);
    return [];
  }
}

public class XmlIfNoneOp : XmlOpCollection
{
  public override IEnumerable<OpExecution> Execute(OpExecContext ctx)
  {
    if (!XmlIfOp.IsAny(ctx.Nav, Path))
      return base.Execute(ctx);
    return [];
  }
}