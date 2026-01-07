
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;

namespace KittenExtensions.Patch;

public interface IXmlLeafOp
{
  public IEnumerable<OpAction> ExecuteLeaf(OpExecContext ctx);
}

public abstract class XmlOp
{
  public const string MergeIdAttr = "_MergeId";
  public const string DefaultMergeId = "Id";
  public const string AnyMergeId = "*";
  public const string NoneMergeId = "-";
  public const string MergePosAttr = "_MergePos";

  [XmlAttribute("Path")]
  public string Path = ".";

  [XmlIgnore]
  public XmlElement Element;

  public abstract IEnumerable<OpExecution> Execute(OpExecContext ctx);
}

public class XmlOpCollection : XmlOp
{
  [XmlElement("Update", typeof(XmlUpdateOp))]
  [XmlElement("Delete", typeof(XmlDeleteOp))]
  [XmlElement("Copy", typeof(XmlCopyOp))]
  [XmlElement("If", typeof(XmlIfOp))]
  [XmlElement("IfAny", typeof(XmlIfAnyOp))]
  [XmlElement("IfNone", typeof(XmlIfNoneOp))]
  [XmlElement("With", typeof(XmlWithOp))]
  public List<XmlOp> Ops;

  public override IEnumerable<OpExecution> Execute(OpExecContext ctx)
  {
    foreach (var op in Ops)
      yield return ctx.Execution(op);
  }
}

[XmlRoot("Patch")]
public class XmlPatch : XmlOpCollection
{
  [XmlIgnore]
  public string Id;

  // TODO: priority? order? something to allow running before and after other mods patches
}

public abstract class XmlChildrenOp : XmlOp
{
  [XmlText(typeof(string))]
  [XmlAnyElement]
  public List<object> Children = [];

  public string StringValue
  {
    get
    {
      if (Children.Count == 0)
        return "";
      if (Children.Count > 1 || Children[0] is not string strVal)
        throw new InvalidOperationException($"Contents are not string for {GetType().Name} '{Path}'");
      return strVal;
    }
  }
}

public class XmlOpElementPopulator
{
  public static void Populate(XmlElement element, object op)
  {
    if (op == null)
      return;
    Get(op.GetType())?.Populate(element, op, [], 0);
  }

  private static readonly Dictionary<Type, XmlOpElementPopulator> popByType = [];

  private static XmlOpElementPopulator Get(Type type)
  {
    if (type == null)
      return null;
    if (popByType.TryGetValue(type, out var pop))
      return pop;

    if (type.IsAssignableTo(typeof(XmlOp)))
    {
      pop = popByType[type] = new();

      foreach (var field in type.GetFields(
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy))
      {
        if (!field.FieldType.IsAssignableTo(typeof(XmlOp)) && !IsList(field.FieldType))
          continue;
        var hasAttr = false;
        foreach (var attr in field.GetCustomAttributes())
        {
          if (attr is XmlAnyElementAttribute)
          {
            pop.anyField = field;
            hasAttr = true;
          }
          else if (attr is XmlElementAttribute elAttr)
          {
            var name = elAttr.ElementName;
            if (string.IsNullOrEmpty(name))
              name = field.Name;
            pop.opFields.Add(name, field);
            hasAttr = true;
          }
        }
        if (!hasAttr)
          pop.opFields.Add(field.Name, field);
      }

      return pop;
    }
    else if (IsList(type))
      return popByType[type] = new() { isList = true };
    else
      return null;
  }

  private static bool IsList(Type type) =>
    type.IsConstructedGenericType && type.GetGenericTypeDefinition() == typeof(List<>);

  private bool isList = false;
  private FieldInfo anyField;
  private readonly Dictionary<string, FieldInfo> opFields = [];

  private XmlOpElementPopulator() { }

  private void Populate(XmlElement element, object obj, Dictionary<(XmlNode, FieldInfo), int> counts, int listPos)
  {
    if (obj == null)
      return;

    if (isList)
    {
      var list = (IList)obj;
      obj = list[listPos];
      Get(obj?.GetType())?.Populate(element, list[listPos], counts, 0);
      return;
    }

    if (obj is not XmlOp op)
      return;

    op.Element = element;

    var children = element.ChildNodes;
    for (var i = 0; i < children.Count; i++)
    {
      if (children[i] is not XmlElement child)
        continue;

      if (!opFields.TryGetValue(child.Name, out var field) && anyField == null)
        continue;
      field ??= anyField;

      var lkey = (element, field);
      var lpos = counts.GetValueOrDefault(lkey);
      counts[lkey] = lpos + 1;

      Get(field.FieldType)?.Populate(child, field.GetValue(obj), counts, lpos);
    }
  }
}