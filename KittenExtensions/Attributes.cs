using KSA;
using System;
using System.Reflection;
using static KittenExtensions.AssetEx;
#pragma warning disable CS9113

namespace KittenExtensions;

[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public class KxCustomAttribute() : Attribute;

[AttributeUsage(AttributeTargets.Class)]
[KxCustom]
internal class KxAssetAttribute(string xmlElement) : Attribute
{
  public static bool Check(Type type, CustomAttributeData attr)
  {
    if (attr.ConstructorArguments.Count < 1)
      return FailAttribute("not enough arguments", type, attr);

    if (!ValidateArg(attr, 0, out string elName, out var err))
      return FailAttribute(err, type, attr);

    AddExtension(typeof(AssetBundle), nameof(AssetBundle.Assets), type, elName);
    return true;
  }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
[KxCustom()]
internal class KxAssetInjectAttribute(Type parent, string member, string xmlElement) : Attribute
{
  public static bool Check(Type type, CustomAttributeData attr)
  {
    if (attr.ConstructorArguments.Count < 3)
      return FailAttribute("not enough arguments", type, attr);

    if (!ValidateArg(attr, 0, out Type parent, out var err))
      return FailAttribute(err, type, attr);
    if (!ValidateArg(attr, 1, out string member, out err))
      return FailAttribute(err, type, attr);
    if (!ValidateArg(attr, 2, out string xmlElement, out err))
      return FailAttribute(err, type, attr);

    AddExtension(parent, member, type, xmlElement);
    return true;
  }
}
