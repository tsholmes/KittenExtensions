
using System.Xml.XPath;

namespace KittenExtensions.Patch;

public class XmlDeleteOp : XmlOp
{
  public override void Execute(XPathNavigator nav)
  {
    foreach (var node in nav.Select(Path).ToNodeList())
    {
      node.ParentNode.RemoveChild(node);
    }
  }
}