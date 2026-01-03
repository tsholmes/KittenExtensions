
using System;
using System.Xml;
using System.Xml.Serialization;
using System.Xml.XPath;

namespace KittenExtensions.Patch;

public class XmlUpdateOp : XmlChildrenOp
{
  [XmlAttribute("Pos")]
  public Position Pos = Position.Default;

  public override void Execute(XPathNavigator nav)
  {
    foreach (var node in nav.Select(Path).ToNodeList())
    {
      switch (node)
      {
        case XmlElement el: Update(el); break;
        case XmlAttribute attr: UpdateText(attr); break;
        case XmlText text: UpdateText(text); break;
        default: throw new InvalidOperationException($"Unknown node type {node.GetType()}");
      }
    }
  }

  private void Update(XmlElement el)
  {
    if (Pos is Position.Merge or Position.Default)
    {
      UpdateMerge(el);
      return;
    }
    var inserter = new Inserter(el, Pos == Position.Replace ? Position.Before : Pos);
    foreach (var child in Children)
      inserter.Insert(ToNode(el, child));
    if (Pos == Position.Replace)
      el.ParentNode.RemoveChild(el);
  }

  private void UpdateText(XmlNode node)
  {
    var strVal = StringValue;
    node.Value = Pos switch
    {
      Position.Replace or Position.Default => strVal,
      Position.Append => node.Value + strVal,
      Position.Prepend => strVal + node.Value,
      _ => throw new InvalidOperationException($"Invalid Pos for string Update: {Pos}"),
    };
  }

  private void UpdateMerge(XmlElement target)
  {
    if (Children.Count != 1 || Children[0] is not XmlElement source)
      throw new InvalidOperationException($"Update Merge operations require one child element");
    Merge(source, target);
  }
}