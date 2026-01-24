
using System;
using System.Collections.Generic;
using System.Reflection;
using Brutal;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Core;
using KSA;
using KSA.Rendering;

namespace KittenExtensions;

public static class ImGuiRenderers
{
  private static readonly uint MarkerHash = KeyHash.Make("KxImGuiShader").Code;
  private static readonly int MinMarkerSize = (int)ByteSize.Of<uint2>().Bytes;

  private static readonly List<ViewportRenderer> viewports = [];
  private static readonly Dictionary<ImGuiID, ViewportRenderer> viewportsByID = [];
  private static readonly Dictionary<KeyHash, List<ImGuiPostRenderer>> orphanedRenderers = [];
  private static readonly List<ImGuiPostRenderer> activeRenderers = [];

  private static Action<ImTextureDataPtr> UpdateTexture => field ??=
    typeof(ImGuiBackendVulkanImpl)
      .GetMethod("UpdateTexture", BindingFlags.Instance | BindingFlags.NonPublic)
      .CreateDelegate<Action<ImTextureDataPtr>>(ImGuiBackend.Vulkan);

  public static void Render(Renderer renderer, CommandBuffer commandBuffer)
  {
    activeRenderers.Clear();
    foreach (var viewport in viewports)
      viewport.Reset();

    var platformIO = ImGui.GetPlatformIO();
    foreach (var pviewport in platformIO.Viewports)
    {
      var extent = new VkExtent2D((int)pviewport.Size.X, (int)pviewport.Size.Y);
      if (!viewportsByID.TryGetValue(pviewport.ID, out var viewport))
        viewports.Add(viewport = viewportsByID[pviewport.ID] = new(pviewport));

      viewport.Active = true;
      viewport.EnsureSize(extent);
    }
    var activeCount = 0;
    for (var i = 0; i < viewports.Count; i++)
    {
      var viewport = viewports[i];
      if (viewport.Active)
      {
        viewports[activeCount++] = viewport;
        continue;
      }
      viewportsByID.Remove(viewport.ID);
      viewport.Orphan();
    }
    if (activeCount < viewports.Count)
      viewports.RemoveRange(activeCount, viewports.Count - activeCount);

    var textures = ImGui.GetDrawData().Textures;
    if (textures.IsNotNull())
    {
      foreach (var tex in textures.Value)
      {
        if (tex.Status != 0)
          UpdateTexture(tex);
      }
    }

    foreach (var viewport in viewports)
    {
      if ((viewport.Viewport.Flags & ImGuiViewportFlags.IsMinimized) != 0)
        continue;

      var drawData = viewport.Viewport.DrawData;
      foreach (var drawList in drawData.CmdLists)
      {
        if (FindCustomRenderer(drawList) is not uint key)
          continue;
        var r = viewport.GetRenderer(renderer, key);
        activeRenderers.Add(r);

        r.Render(commandBuffer, drawData, drawList);
      }
    }

    Span<ImageTransition> transitions = stackalloc ImageTransition[activeRenderers.Count];
    for (var i = 0; i < activeRenderers.Count; i++)
      transitions[i] = new(
        activeRenderers[i].Target,
        ImageBarrierInfo.Presets.ColorAttachment,
        ImageBarrierInfo.Presets.ShaderReadOnlyFragment);

    commandBuffer.TransitionImages2(transitions);
  }

  public static void RebuildAll()
  {
    foreach (var viewport in viewports)
      viewport.RebuildAll();
  }

  private static unsafe uint? FindCustomRenderer(ImDrawListPtr drawList)
  {
    var remData = drawList._CallbacksDataBuf.Count;

    for (var i = 0; i < drawList.CmdBuffer.Count && remData > 0; i++)
    {
      if (IsCustomRenderer(ref drawList.CmdBuffer.Data[i], out uint index))
        return index;
      remData -= drawList.CmdBuffer.Data[i].UserCallbackDataSize;
    }
    return null;
  }

  private static unsafe bool IsCustomRenderer(ref ImDrawCmd cmd, out uint index)
  {
    index = 0xFFFFFFFF;
    if (cmd.UserCallback == IntPtr.Zero)
      return false;
    if (cmd.UserCallbackDataSize < MinMarkerSize)
      return false;

    var data = *(uint2*)cmd.UserCallbackData;
    if (data[0] != MarkerHash)
      return false;
    index = data[1];
    return true;
  }

  private class ViewportRenderer(ImGuiViewportPtr Viewport)
  {
    public readonly ImGuiViewportPtr Viewport = Viewport;
    public readonly ImGuiID ID = Viewport.ID;
    public readonly Dictionary<KeyHash, RendererList> Renderers = [];
    public bool Active = false;
    public VkExtent2D Size = new((int)Viewport.Size.X, (int)Viewport.Size.Y);

    public void Reset()
    {
      Active = false;
      foreach (var rlist in Renderers.Values)
        rlist.Reset();
    }

    public void EnsureSize(VkExtent2D size)
    {
      if (size.Width == Size.Width && size.Height == Size.Height)
        return;
      Size = size;
      RebuildAll();
    }

    public void RebuildAll()
    {
      foreach (var rlist in Renderers.Values)
        rlist.RebuildAll(Size);
    }

    public void Orphan()
    {
      foreach (var (hash, rlist) in Renderers)
      {
        if (!orphanedRenderers.TryGetValue(hash, out var olist))
          olist = orphanedRenderers[hash] = [];
        olist.AddRange(rlist.Renderers);
      }
    }

    public ImGuiPostRenderer GetRenderer(Renderer renderer, uint key)
    {
      var hash = new KeyHash(key);
      if (!Renderers.TryGetValue(hash, out var rlist))
        rlist = Renderers[hash] = new(ImGuiShaderReference.AllShaders.Get(hash));

      return rlist.Next(renderer, Size);
    }
  }

  private class RendererList(ImGuiShaderReference shaders)
  {
    public readonly KeyHash Key = shaders.Hash;
    public readonly ShaderReference Vertex = shaders.Vertex.Get();
    public readonly ShaderEx Fragment = shaders.Fragment.Get<ShaderEx>();
    public int Count = 0;
    public readonly List<ImGuiPostRenderer> Renderers = [];

    public void Reset() => Count = 0;
    public void RebuildAll(VkExtent2D extent)
    {
      foreach (var r in Renderers)
        r.Rebuild(extent);
    }

    public ImGuiPostRenderer Next(Renderer renderer, VkExtent2D extent)
    {
      if (Count == Renderers.Count)
      {
        if (orphanedRenderers.TryGetValue(Key, out var olist) && olist.Count > 0)
        {
          // take from orphans first if any available
          var r = olist[^1];
          olist.RemoveAt(olist.Count - 1);

          r.Rebuild(extent);
          Renderers.Add(r);
        }
        else
          Renderers.Add(new(renderer, extent, Vertex, Fragment));
      }

      return Renderers[Count++];
    }
  }
}
