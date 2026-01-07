
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;
using System.Xml.XPath;
using Brutal.Logging;
using Core.Files.Paths;
using KSA;
using Tomlet;
using Tomlet.Attributes;

namespace KittenExtensions.Patch;

public static partial class XmlPatcher
{
  private static XmlDocument RootDoc;
  private static XmlElement RootNode;

  private static readonly Dictionary<Type, string> XmlRootName = [];

  public static T Load<T>(string filePath, Mod mod) where T : ILibraryData
  {
    if (!XmlRootName.TryGetValue(typeof(T), out var rootName))
    {
      rootName = typeof(T).GetCustomAttribute<XmlRootAttribute>()?.ElementName;
      if (string.IsNullOrEmpty(rootName))
        rootName = typeof(T).Name;
      XmlRootName[typeof(T)] = rootName;
    }
    var result = default(T);
    try
    {
      var normPath = Filepath.CorrectSeparators(Path.GetFullPath(filePath)).ToLowerInvariant();
      var node = RootNode.SelectSingleNode($"Mod/{rootName}[@PathKey='{normPath}']");
      if (node != null)
      {
        var serializer = AssetEx.GetSerializer<T>();
        using var reader = new XmlNodeReader(node);
        result = serializer.Deserialize(reader) is T val ? val : default;
      }
    }
    catch (Exception ex)
    {
      DefaultCategory.Log.Error(ex);
    }
    result?.OnDataLoad(mod);
    return result;
  }

  public static void OnPrepare()
  {
    LoadData();

    if (DebugEnabled())
    {
      RootDoc.Save(Path.Combine(Constants.DocumentsFolderPath, "root.xml"));
      var patchTask = new PopupTask(new PatchDebugPopup());
      while (patchTask.Show)
        patchTask.OnFrame();
    }
    else
      RunPatches();
  }

  private static List<XmlElement> ChildElementList(XmlNode node, string path) =>
    new(node.SelectNodes(path).Cast<XmlElement>());

  private static IEnumerable<XmlPatch> GetPatches()
  {
    foreach (var mod in ChildElementList(RootNode, "Mod"))
      foreach (var patch in ChildElementList(mod, "Patch"))
        yield return DeserializePatch(patch);
  }

  private static void RunPatches()
  {
    var ctx = new DefaultOpExecContext(RootNode.CreateNavigator());
    foreach (var patch in GetPatches())
    {
      new PatchExecutor(ctx.Execution(patch)).ToEnd();
    }
  }

  private static XmlPatch DeserializePatch(XmlElement element)
  {
    var serializer = AssetEx.GetSerializer<XmlPatch>();
    XmlPatch patch;
    using (var reader = new XmlNodeReader(element))
    {
      patch = (XmlPatch)serializer.Deserialize(reader);
    }
    XmlOpElementPopulator.Populate(element, patch);

    patch.Id = $"{(element.ParentNode as XmlElement)?.GetAttribute("Id")}/{element.GetAttribute("Path")}";
    return patch;
  }

  private static void RunPatch(XmlElement element, OpExecContext ctx)
  {
    var serializer = AssetEx.GetSerializer<XmlPatch>();
    XmlPatch patch;
    using (var reader = new XmlNodeReader(element))
    {
      patch = (XmlPatch)serializer.Deserialize(reader);
    }
    XmlOpElementPopulator.Populate(element, patch);

    patch.Id = $"{(element.ParentNode as XmlElement)?.GetAttribute("Id")}/{element.GetAttribute("Path")}";

    RunOp(ctx.Execution(patch));
  }

  public static void RunOp(OpExecution exec)
  {
    if (exec.Op is IXmlLeafOp leaf)
    {
      foreach (var action in leaf.ExecuteLeaf(exec.Context))
        action.Run();
    }
    else
      foreach (var subExec in exec.Op.Execute(exec.Context))
        RunOp(subExec);
  }

  private static void LoadData()
  {
    RootDoc = new XmlDocument();
    RootDoc.LoadXml("<Root/>");
    RootNode = RootDoc.DocumentElement;

    foreach (var mod in ModLibrary.Manifest.Mods)
      LoadModData(mod);
  }

  private static void LoadModData(ModEntry modEntry)
  {
    // TODO: error handling when file doesn't exist, is malformed, etc
    var tomlPath = Path.Combine("Content", modEntry.Id, "mod.toml");
    var mod = TomletMain.To<ModToml>(File.ReadAllText(tomlPath));
    var dirPath = Filepath.CorrectSeparators(Path.GetFullPath(Path.GetDirectoryName(tomlPath) ?? ""));

    var modNode = RootDoc.CreateElement("Mod");
    RootNode.AppendChild(modNode);

    modNode.SetAttribute("Id", modEntry.Id);
    modNode.SetAttribute("Name", mod.Name);
    modNode.SetAttribute("PathKey", dirPath.ToLowerInvariant());

    foreach (var path in mod.PlanetMeshCollections)
      LoadModFile(modNode, dirPath, path);
    foreach (var path in mod.SystemTemplates)
      LoadModFile(modNode, dirPath, path);
    foreach (var path in mod.Assets)
      LoadModFile(modNode, dirPath, path);
    foreach (var path in mod.Patches)
      LoadModFile(modNode, dirPath, path);
  }

  private static void LoadModFile(XmlElement modNode, string dirPath, string path)
  {
    var fullPath = Filepath.CorrectSeparators(Path.GetFullPath(Path.Combine(dirPath, path)));
    var doc = new XmlDocument();
    doc.Load(fullPath);

    var docNode = (XmlElement)RootDoc.ImportNode(doc.DocumentElement, true);
    modNode.AppendChild(docNode);

    docNode.SetAttribute("Path", Filepath.CorrectSeparators(path));
    docNode.SetAttribute("PathKey", fullPath.ToLowerInvariant());
  }

  private class ModToml
  {
    [TomlField("name")]
    public string Name = "";
    [TomlField("planetMeshes")]
    public string[] PlanetMeshCollections = [];
    [TomlField("systems")]
    public string[] SystemTemplates = [];
    [TomlField("assets")]
    public string[] Assets = [];
    [TomlField("patches")]
    public string[] Patches = [];
  }

  private static bool DebugEnabled()
  {
    try
    {
      var manifest = TomletMain.To<ModManifestToml>(File.ReadAllText(ModLibrary.LocalManifestPath));
      return manifest.Mods.FirstOrDefault(mod => mod.Id == "KittenExtensions" && mod.Enabled)?.Debug ?? false;
    }
    catch (Exception)
    {
      return false;
    }
  }

  private class ModManifestToml
  {
    [TomlDoNotInlineObject]
    [TomlField("mods")]
    public List<ModEntryToml> Mods = [];
  }

  private class ModEntryToml
  {
    [TomlField("id")]
    public string Id = "";
    [TomlField("enabled")]
    public bool Enabled = true;
    [TomlField("debug")]
    public bool Debug = false;
  }
}

public static partial class Extensions
{
  public static List<XmlNode> ToNodeList(this XPathNodeIterator iter)
  {
    var list = new List<XmlNode>(iter.Count);
    while (iter.MoveNext())
      list.Add((XmlNode)iter.Current.UnderlyingObject);
    return list;
  }
  public static List<XPathNavigator> ToNavList(this XPathNodeIterator iter)
  {
    var list = new List<XPathNavigator>(iter.Count);
    while (iter.MoveNext())
      list.Add(iter.Current.Clone());
    return list;
  }
}