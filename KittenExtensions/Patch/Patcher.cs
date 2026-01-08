
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

  private static IEnumerable<DeserializedPatch> GetPatches()
  {
    foreach (var mod in ChildElementList(RootNode, "Mod"))
      foreach (var patch in ChildElementList(mod, "Patch"))
        yield return DeserializePatch(patch);
  }

  private static void RunPatches()
  {
    try
    {
      var ctx = new DefaultOpExecContext(RootNode.CreateNavigator());
      var executor = new PatchExecutor(ctx, GetPatches());
      executor.ToEnd();
      if (executor.Error != null)
        throw ErrorAndExit(executor.Error.ToString(), executor.CurElement);
    }
    catch (Exception ex)
    {
      throw ErrorAndExit(ex.ToString());
    }
  }

  private static DeserializedPatch DeserializePatch(XmlElement element)
  {
    var id = $"{(element.ParentNode as XmlElement)?.GetAttribute("Id")}/{element.GetAttribute("Path")}";
    try
    {
      var serializer = AssetEx.GetSerializer<XmlPatch>();
      using var reader = new XmlNodeReader(element);
      var patch = (XmlPatch)serializer.Deserialize(reader);
      patch.Id = id;
      XmlOpElementPopulator.Populate(element, patch);
      return new(id, element, patch, null);
    }
    catch (Exception ex)
    {
      return new(id, element, null, ex?.InnerException ?? ex);
    }
  }

  private static void LoadData()
  {
    RootDoc = new XmlDocument();
    RootDoc.LoadXml("<Root/>");
    RootNode = RootDoc.DocumentElement;

    foreach (var mod in ModLibrary.Manifest.Mods)
      LoadModData(mod);
  }

  private static Exception ErrorAndExit(string error, XmlElement elementLoc = null, string strLoc = null)
  {
    Console.Error.WriteLine(error);
    var errTask = new PopupTask(new ErrorPopup(error, elementLoc, strLoc));
    while (errTask.Show)
      errTask.OnFrame();
    Environment.Exit(1);
    throw new InvalidOperationException();
  }

  private static void LoadModData(ModEntry modEntry)
  {
    var tomlPath = Path.Combine("Content", modEntry.Id, "mod.toml");
    var dirPath = Filepath.CorrectSeparators(Path.GetFullPath(Path.GetDirectoryName(tomlPath) ?? ""));
    ModToml mod;
    try
    {
      mod = TomletMain.To<ModToml>(File.ReadAllText(tomlPath));
    }
    catch (Exception ex)
    {
      throw ErrorAndExit(ex.ToString(), strLoc: Path.GetFullPath(tomlPath));
    }

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
    try
    {
      var doc = new XmlDocument();
      doc.Load(fullPath);

      var docNode = (XmlElement)RootDoc.ImportNode(doc.DocumentElement, true);
      modNode.AppendChild(docNode);

      docNode.SetAttribute("Path", Filepath.CorrectSeparators(path));
      docNode.SetAttribute("PathKey", fullPath.ToLowerInvariant());
    }
    catch (Exception ex)
    {
      ErrorAndExit(ex.ToString(), strLoc: fullPath);
    }
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