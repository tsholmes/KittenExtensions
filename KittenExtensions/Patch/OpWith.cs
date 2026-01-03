
using System.Xml.XPath;

namespace KittenExtensions.Patch;

public class XmlWithOp : XmlOpCollection
{
  public override void Execute(XPathNavigator nav)
  {
    foreach (var match in nav.Select(Path).ToNavList())
      base.Execute(match);
  }
}