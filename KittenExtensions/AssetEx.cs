
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Xml.Serialization;
using Brutal.Logging;
using KSA;

namespace KittenExtensions;

public static class AssetEx
{
  private static readonly List<Type> AttributeCheckMethods = [];

  private static readonly Dictionary<Type, XmlExtension> xmlExtensions = [];

  public static void Init()
  {
    foreach (var alc in AssemblyLoadContext.All)
      foreach (var asm in alc.Assemblies)
        if (HasAssetAttribute(asm, typeof(KxCustomAttribute)))
          RegisterAllCustomAttributes(asm);

    foreach (var alc in AssemblyLoadContext.All)
      foreach (var asm in alc.Assemblies)
        if (HasAnyAssetAttribute(asm))
          RegisterAll(asm);

    var overrides = XmlHelper.AttributeOverrides;
    foreach (var ext in xmlExtensions.Values)
      ext.BuildOverrides(overrides);

    var existing = new List<Type>(XmlHelper.Serializers.Keys);
    XmlHelper.Serializers.Clear();
    foreach (var type in existing)
      XmlHelper.Serializers.Add(type, new(type, overrides));

    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "<UniverseSerializer>k__BackingField")]
    extern static ref XmlSerializer UniverseSerializer(
        [UnsafeAccessorType("KSA.GameSaves, KSA")] object instance = null);
    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "<VehicleSerializer>k__BackingField")]
    extern static ref XmlSerializer VehicleSerializer(
        [UnsafeAccessorType("KSA.VehicleSaves, KSA")] object instance = null);

    UniverseSerializer() = GetSerializer<UniverseData>();
    VehicleSerializer() = GetSerializer<VehicleSaveData>();
  }

  public static XmlSerializer GetSerializer<T>()
  {
    var type = typeof(T);
    if (XmlHelper.Serializers.TryGetValue(type, out var serializer))
      return serializer;

    return XmlHelper.Serializers[type] = new XmlSerializer(type, XmlHelper.AttributeOverrides);
  }

  private static void RegisterAllCustomAttributes(Assembly asm)
  {
    foreach (var type in GetTypes(asm))
      foreach (var attr in type.GetCustomAttributesData())
      {
        if (attr.AttributeType.FullName != typeof(KxCustomAttribute).FullName)
          continue;

        // Ensure that only valid attributes from their source assembly are added
        if (type.GetMethod("Check", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) != null)
        {
          if (!AttributeCheckMethods.Any(t => t.FullName == type.FullName))
            AttributeCheckMethods.Add(type);
          else
            DefaultCategory.Log.Warning($"Couldn't add {type.FullName} from {type.Assembly} because customAttributes already contains an instance.");
        }
      }
  }

  private static void RegisterAll(Assembly asm)
  {
    foreach (var type in GetTypes(asm))
      foreach (var attr in type.GetCustomAttributesData())
        RegisterAttr(type, attr);
  }

  public static bool FailAttribute(string message, Type type, CustomAttributeData attr)
  {
    DefaultCategory.Log.Warning($"invalid attribute {attr.AttributeType} on {type}: {message}");
    return false;
  }

  private static bool RegisterAttr(Type type, CustomAttributeData attr)
  {
    Type AttributeType = AttributeCheckMethods.FirstOrDefault(t => t.FullName == attr.AttributeType.FullName);
    if (AttributeType == null) return false;
    var method = AttributeType.GetMethod("Check", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

    if (method == null) return false;
    else return (bool)method.Invoke(null, new object[] { type, attr });
  }

  public static bool ValidateArg<T>(CustomAttributeData attr, int argIdx, out T val, out string err)
    where T : class
  {
    var arg = attr.ConstructorArguments[argIdx];
    val = arg.Value as T;
    if (!typeof(T).IsAssignableTo(arg.ArgumentType))
    {
      err = $"argument {argIdx} should be {typeof(T)}, not {arg.ArgumentType}";
      return false;
    }
    if (val == null || (val is string strVal && string.IsNullOrEmpty(strVal)))
    {
      err = $"argument {argIdx} should not be null";
      return false;
    }
    err = null;
    return true;
  }

  public static void AddExtension(Type parent, string member, Type child, string xmlElement)
  {
    if (!xmlExtensions.TryGetValue(parent, out var ext))
      ext = xmlExtensions[parent] = new(parent);

    ext.Add(member, child, xmlElement);
  }

  private static bool HasAnyAssetAttribute(Assembly asm) => AttributeCheckMethods.Any(kvp => HasAssetAttribute(asm, kvp));

  private static bool HasAssetAttribute(Assembly asm, Type t)
  {
    try
    {
      return asm.GetType(t.FullName) != null;
    }
    catch
    {
      // ignore any errors trying to get attribute
      return false;
    }
  }

  private static IEnumerable<Type> GetTypes(Assembly asm)
  {
    try
    {
      return asm.GetTypes();
    }
    catch (ReflectionTypeLoadException ex)
    {
      return ex.Types.Where(t => t != null);
    }
  }
}

public class XmlExtension(Type type)
{
  private readonly Type type = type;
  private readonly HashSet<XmlElementInjection> injections = [];

  public void BuildOverrides(XmlAttributeOverrides overrides)
  {
    foreach (var group in injections.GroupBy(inj => inj.Member))
    {
      var member = group.Key;
      var attrs = new XmlAttributes();
      foreach (var attr in member.GetCustomAttributes())
      {
        if (attr is XmlElementAttribute elAttr)
          attrs.XmlElements.Add(elAttr);
      }
      foreach (var inj in group)
        attrs.XmlElements.Add(new(inj.XmlElement, inj.ChildType));

      overrides.Add(type, member.Name, attrs);
    }
  }

  public bool Add(string memberName, Type childType, string xmlElement)
  {
    var member = (MemberInfo)type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public) ??
      type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public);

    var inj = new XmlElementInjection(member, childType, xmlElement);
    if (injections.Contains(inj))
      return false;

    if (!Validate(memberName, member, childType, xmlElement))
      return false;

    return injections.Add(inj);
  }

  private bool Validate(string memberName, MemberInfo member, Type childType, string xmlElement)
  {
    bool fail(string message)
    {
      DefaultCategory.Log.Warning($"failed to inject {childType} as <{xmlElement}> into {type}.{memberName}: {message}");
      return false;
    }

    if (member == null)
      return fail($"member {memberName} not found");

    var mtype = (member as PropertyInfo)?.PropertyType ?? (member as FieldInfo)?.FieldType;
    if (!TypesCompatible(mtype, childType))
      return fail($"{childType} incompatible with member type {mtype}");

    var attrs = member.GetCustomAttributes();

    if (attrs.Any(attr => attr is XmlIgnoreAttribute))
      return fail($"member {memberName} is ignored for xml serialization");

    if (attrs.Any(attr => attr is XmlAttributeAttribute))
      return fail($"member {memberName} is an xml attribute field");

    var attrMatch = attrs.OfType<XmlElementAttribute>().FirstOrDefault(attr => attr.ElementName == xmlElement);
    if (attrMatch != null)
      return fail($"<{xmlElement}> is already mapped by default as {attrMatch.Type}");

    var injMatch = injections.FirstOrDefault(inj => inj.Member == member && inj.XmlElement == xmlElement);
    if (injMatch != null)
      return fail($"<{xmlElement}> is already injected as {injMatch.ChildType}");

    return true;
  }

  private static bool TypesCompatible(Type memberType, Type childType)
  {
    if (memberType == null)
      return false;
    if (childType.IsAssignableTo(memberType))
      return true;

    if (!memberType.IsConstructedGenericType)
      return false;
    if (memberType.GetGenericTypeDefinition() != typeof(List<>))
      return false;

    return childType.IsAssignableTo(memberType.GetGenericArguments()[0]);
  }
}

public record class XmlElementInjection(MemberInfo Member, Type ChildType, string XmlElement);