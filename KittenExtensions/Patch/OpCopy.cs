
using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Serialization;
using System.Xml.XPath;

namespace KittenExtensions.Patch;

public class XmlCopyOp : XmlOp
{
  [XmlAttribute("Pos")]
  public Position Pos;

  [XmlAttribute("From")]
  public string From;

  public override void Execute(XPathNavigator nav)
  {
    var source = nav.Evaluate(From);
    if (source is XPathNodeIterator srcNodes)
      source = srcNodes.ToNodeList();

    var targets = nav.Select(Path).ToNodeList();
    foreach (var target in targets)
    {
      switch (target)
      {
        case XmlElement el: Copy(el, source); break;
        case XmlAttribute attr: Copy(attr, source); break;
        default: throw new InvalidOperationException($"Unknown node type {source.GetType()}");
      }
    }
  }

  private void Copy(XmlElement target, object source)
  {
    if (Pos is Position.Merge or Position.Default)
    {
      CopyMerge(target, source);
      return;
    }
    var inserter = new Inserter(target, Pos == Position.Replace ? Position.Before : Pos);
    switch (source)
    {
      case List<XmlNode> srcNodes:
        foreach (var node in srcNodes)
          inserter.Insert(target.OwnerDocument.ImportNode(node, true));
        break;
      case bool srcBool: inserter.Insert(ToNode(target, srcBool.ToString())); break;
      case double srcDouble: inserter.Insert(ToNode(target, srcDouble.ToString("g"))); break;
      case string srcString: inserter.Insert(ToNode(target, srcString)); break;
      default:
        throw new InvalidOperationException($"{source?.GetType()}");
    }
    if (Pos == Position.Replace)
      target.ParentNode.RemoveChild(target);
  }

  private void CopyMerge(XmlElement target, object source)
  {
    if (source is not List<XmlNode> srcNodes)
      throw new InvalidOperationException($"Copy Merge operations require a node Path");
    
    foreach (var node in srcNodes)
    {
      if (node is not XmlElement srcEl)
        throw new InvalidOperationException(
          $"Copy Merge From should select elements, not {node.GetType().Name}");
      Merge(srcEl, target);
    }
  }

  private void Copy(XmlAttribute target, object source)
  {
    string copyVal;
    if (source is List<XmlNode> srcNodes)
    {
      var first = srcNodes.Count > 0 ? srcNodes[0] : null;
      copyVal = first switch
      {
        XmlElement firstEl => firstEl.FirstChild?.Value,
        _ => first.Value,
      };
    }
    else
      copyVal = source switch
      {
        bool srcBool => srcBool.ToString(),
        double srcDouble => srcDouble.ToString("g"),
        string srcString => srcString,
        _ => throw new InvalidOperationException($"{source?.GetType()}"),
      };

    copyVal ??= "";

    target.Value = Pos switch
    {
      Position.Replace or Position.Default => copyVal,
      Position.Append => target.Value + copyVal,
      Position.Prepend => copyVal + target.Value,
      _ => throw new InvalidOperationException($"Invalid Pos for Copy to attribute: {Pos}"),
    };

    if (target.Value == "")
      target.ParentNode.RemoveChild(target);
  }
}