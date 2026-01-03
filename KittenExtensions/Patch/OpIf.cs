
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

  public override void Execute(XPathNavigator nav)
  {
    if (IsAny(nav, Path))
      Any.Execute(nav);
    else
      None.Execute(nav);
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
  public override void Execute(XPathNavigator nav)
  {
    if (XmlIfOp.IsAny(nav, Path))
      base.Execute(nav);
  }
}

public class XmlIfNoneOp : XmlOpCollection
{
  public override void Execute(XPathNavigator nav)
  {
    if (!XmlIfOp.IsAny(nav, Path))
      base.Execute(nav);
  }
}