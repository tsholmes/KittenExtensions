
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
using XPP.Doc;
using XPP.Patch;

namespace KittenExtensions.Patch;

public static partial class XmlPatcher
{
  private static readonly PatchDomain Domain = new();
  private static PatchExecutor Executor;

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
      var node = Domain.Mods.FirstOrDefault(m => m.Id == mod.Id).File(Path.GetFullPath(filePath));
      if (node.Valid)
      {
        var serializer = AssetEx.GetSerializer<T>();
        using var reader = new XPDocReader(node);
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

    PatchDebugPopup debugPopup = null;

    try
    {
      if (DebugEnabled())
      {
        var doc = new XmlDocument();
        doc.Load(new XPDocReader(Domain.Doc.LatestRoot));
        doc.Save(Path.Combine(Constants.DocumentsFolderPath, "root.xml"));
        var patchTask = new PopupTask(debugPopup = new PatchDebugPopup());
        while (patchTask.Show)
          patchTask.OnFrame();
      }
      else
        RunPatches();
    }
    catch (Exception ex)
    {
      debugPopup?.Active = false;
      throw ErrorAndExit(ex.ToString(), Executor?.Next?.Patch ?? XPNodeRef.Invalid);
    }
  }

  private static void RunPatches()
  {
    try
    {
      Executor.StepToEnd();
      if (Executor.HasError)
        throw ErrorAndExit(Executor.Next.Error.ToString(), Executor.Next.Patch);
    }
    catch (Exception ex)
    {
      throw ErrorAndExit(ex.ToString(), Executor?.Next?.Patch ?? XPNodeRef.Invalid);
    }
  }

  private static void LoadData()
  {
    foreach (var mod in ModLibrary.Manifest.Mods)
      LoadModData(mod);
    Executor = Domain.Executor();
  }

  private static Exception ErrorAndExit(string error, XPNodeRef elementLoc = default, string strLoc = null)
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

    var patchMod = Domain.AddMod(modEntry.Id);
    patchMod.Node.SetAttribute("Name", mod.Name);
    patchMod.Node.SetAttribute("Path", PatchDomain.NormalizePath(dirPath));

    foreach (var path in mod.PlanetMeshCollections)
      LoadModFile(patchMod, dirPath, path);
    foreach (var path in mod.SystemTemplates)
      LoadModFile(patchMod, dirPath, path);
    foreach (var path in mod.Assets)
      LoadModFile(patchMod, dirPath, path);
    foreach (var path in mod.Patches)
      LoadModFile(patchMod, dirPath, path);
  }

  private static void LoadModFile(PatchDomain.PatchMod mod, string dirPath, string path)
  {
    var fullPath = Filepath.CorrectSeparators(Path.GetFullPath(Path.Combine(dirPath, path)));
    try
    {
      var fileElement = mod.ImportFile(fullPath);
      fileElement.SetAttribute("RelPath", path);
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