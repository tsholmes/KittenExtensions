
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Brutal;
using Brutal.ImGuiApi;
using Brutal.Memory;
using Brutal.Numerics;
using Brutal.Pointers;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using RenderCore;

namespace KittenExtensions;

public class ImGuiBaseRenderer : RenderTechnique
{
  private static VkShaderModule? VertModule => field ??=
    (VkShaderModule)typeof(ImGuiBackendVulkanImpl).GetField(
      "_shaderModuleVert", BindingFlags.Instance | BindingFlags.NonPublic
    ).GetValue(ImGuiBackend.Vulkan);
  private static VkShaderModule? FragModule => field ??=
    (VkShaderModule)typeof(ImGuiBackendVulkanImpl).GetField(
      "_shaderModuleFrag", BindingFlags.Instance | BindingFlags.NonPublic
    ).GetValue(ImGuiBackend.Vulkan);
  private static VkPipelineLayout? BasePipelineLayout => field ??=
    (VkPipelineLayout)typeof(ImGuiBackendVulkanImpl).GetField(
      "_pipelineLayout", BindingFlags.Instance | BindingFlags.NonPublic
    ).GetValue(ImGuiBackend.Vulkan);

  private static ShaderReference[] BaseShaders => field ??= [
    MakeShader(VkShaderStageFlags.VertexBit, VertModule),
    MakeShader(VkShaderStageFlags.FragmentBit, FragModule)
  ];

  private static readonly PtrOwner<VkPipelineColorBlendAttachmentState> baseBlendAttachment = new(new VkPipelineColorBlendAttachmentState
  {
    BlendEnable = true,
    SrcColorBlendFactor = VkBlendFactor.SrcAlpha,
    DstColorBlendFactor = VkBlendFactor.OneMinusSrcAlpha,
    ColorBlendOp = VkBlendOp.Add,
    SrcAlphaBlendFactor = VkBlendFactor.One,
    DstAlphaBlendFactor = VkBlendFactor.OneMinusSrcAlpha,
    AlphaBlendOp = VkBlendOp.Add,
    ColorWriteMask = VkColorComponentFlags.RBit | VkColorComponentFlags.GBit | VkColorComponentFlags.BBit | VkColorComponentFlags.ABit
  });
  private static readonly PtrOwner<VkPipelineColorBlendStateCreateInfo> baseBlendCreate = new(new VkPipelineColorBlendStateCreateInfo
  {
    AttachmentCount = 1,
    Attachments = baseBlendAttachment,
  });

  private static ShaderReference MakeShader(VkShaderStageFlags stage, VkShaderModule? module)
  {
    var shader = new ShaderReference() { Stage = stage };
    typeof(ShaderReference).GetProperty("Shader").GetSetMethod(true).Invoke(shader, [module]);
    return shader;
  }

  private RenderTarget renderTarget;
  private readonly VkRenderPass renderPass;

  private readonly BufferPair[] buffers;
  private int nextBuffer = 0;

  public Framebuffer.FramebufferAttachment Target => renderTarget.ColorImage;

  public ImGuiBaseRenderer(Renderer renderer, VkExtent2D extent)
    : base(nameof(ImGuiBaseRenderer), renderer, Program.MainPass, BaseShaders)
  {
    buffers = new BufferPair[renderer.ImageCount];

    renderTarget = new(renderer, extent, VkFormat.R8G8B8A8UNorm, VkFormat.Undefined);
    renderPass = renderTarget.CreateRenderPass();
    renderTarget.BuildFramebuffer(renderPass);

    PipelineLayout = BasePipelineLayout.Value;

    RebuildFrameResources();
  }

  protected override VertexInput MakeVertexInput()
  {
    var vi = new VertexInput(1, 3);
    vi.AddBinding(0, ByteSize.Of<ImDrawVert>(), VkVertexInputRate.Vertex);
    vi.AddAttribute(0, 0, VkFormat.R32G32SFloat, ByteSize.Of(Marshal.OffsetOf<ImDrawVert>("pos")));
    vi.AddAttribute(1, 0, VkFormat.R32G32SFloat, ByteSize.Of(Marshal.OffsetOf<ImDrawVert>("uv")));
    vi.AddAttribute(2, 0, VkFormat.R8G8B8A8UNorm, ByteSize.Of(Marshal.OffsetOf<ImDrawVert>("col")));
    return vi;
  }

  protected override void OnRebuildFrameResources() =>
    CreatePipeline(
      VkPrimitiveTopology.TriangleList,
      VkCullModeFlags.None,
      VkFrontFace.CounterClockwise,
      VkPolygonMode.Fill,
      default,
      baseBlendCreate,
      out Pipeline);

  public void Rebuild(VkExtent2D extent)
  {
    Renderer.Device.WaitIdle();
    renderTarget.Dispose();
    renderTarget = new(Renderer, extent, VkFormat.R8G8B8A8UNorm, VkFormat.Undefined);
    renderTarget.BuildFramebuffer(renderPass);

    RebuildFrameResources();
  }

  private void SetupBuffers(ImDrawListPtr drawList, BufferPair bufs)
  {
    var device = Renderer.Device;

    var vertexBuf = bufs.Vertex ??= new();
    var indexBuf = bufs.Index ??= new();

    vertexBuf.EnsureSize(
      Renderer,
      ByteSize.Of<ImDrawVert>(drawList.VtxBuffer.Count).AlignTo(vertexBuf.Alignment),
      VkBufferUsageFlags.VertexBufferBit);
    indexBuf.EnsureSize(
      Renderer,
      ByteSize.Of<ushort>(drawList.IdxBuffer.Count).AlignTo(indexBuf.Alignment),
      VkBufferUsageFlags.IndexBufferBit);

    var vertexPtr = vertexBuf.Map<ImDrawVert>(device);
    var indexPtr = indexBuf.Map<ushort>(device);

    Mem.Copy(drawList.VtxBuffer.Data, vertexPtr, drawList.VtxBuffer.Count);
    Mem.Copy(drawList.IdxBuffer.Data, indexPtr, drawList.IdxBuffer.Count);

    device.FlushMappedMemoryRanges([vertexBuf.Range, indexBuf.Range]);

    vertexBuf.Unmap(device);
    indexBuf.Unmap(device);
  }

  // returns [pxbounds, uvbounds]
  public unsafe float2x4 Render(CommandBuffer commandBuffer, ImDrawDataPtr drawData, ImDrawListPtr drawList)
  {
    var displaySize = drawData.DisplaySize;
    var displayPos = drawData.DisplayPos;
    var framebufferScale = drawData.FramebufferScale;
    var scale = new float2(2f) / displaySize;
    var translate = -float2.One - displayPos * scale;

    var bufs = buffers[nextBuffer] ??= new();
    nextBuffer = (nextBuffer + 1) % buffers.Length;

    var size = new float2(renderTarget.Extent.Width, renderTarget.Extent.Height);
    var extent = new VkExtent2D(renderTarget.Extent.Width, renderTarget.Extent.Height);
    var rect = new VkRect2D(extent);
    commandBuffer.BeginRenderPass(new VkRenderPassBeginInfo
    {
      RenderPass = renderPass,
      Framebuffer = renderTarget.FrameBuffer,
      RenderArea = rect,
    }, VkSubpassContents.Inline);

    SetupBuffers(drawList, bufs);

    commandBuffer.BindPipeline(VkPipelineBindPoint.Graphics, Pipeline);
    commandBuffer.BindVertexBuffer(0, bufs.Vertex.Buffer);
    commandBuffer.BindIndexBuffer(bufs.Index.Buffer, ByteSize.Zero, VkIndexType.Uint16);

    commandBuffer.SetViewport(0, [new VkViewport
    {
      Width = extent.Width,
      Height = extent.Height,
      MinDepth = 0f,
      MaxDepth = 1f,
    }]);

    Span<float> pushConsts = [scale.X, scale.Y, translate.X, translate.Y];
    commandBuffer.PushConstants(
      PipelineLayout, VkShaderStageFlags.VertexBit, 0,
      MemoryMarshal.Cast<float, byte>(pushConsts));

    var rstate = new RenderState
    {
      CommandBuffer = commandBuffer,
      Pipeline = Pipeline,
      PipelineLayout = PipelineLayout,
    };
    var platformIo = ImGui.GetPlatformIO();
    platformIo.Renderer_RenderState = (IntPtr)(&rstate);

    var pxBounds = new float4();
    var uvBounds = new float4();
    var first = true;

    var curTex = default(VkDescriptorSet);
    for (var i = 0; i < drawList.CmdBuffer.Count; ++i)
    {
      var cmd = drawList.CmdBuffer[i];
      if (cmd.UserCallback != IntPtr.Zero)
        continue;
      var clipMin = float2.Max(float2.Zero, (cmd.ClipRect.XY - displayPos) * framebufferScale);
      var clipMax = float2.Min(
        new float2(extent.Width, extent.Height),
        (cmd.ClipRect.ZW - displayPos) * framebufferScale
      );
      var clipSize = clipMax - clipMin;
      if (clipSize.X <= 0 || clipSize.Y <= 0)
        continue;

      commandBuffer.SetScissor(0, [new VkRect2D
      {
        Offset = new((int)clipMin.X, (int)clipMin.Y),
        Extent = new((int)clipSize.X, (int)clipSize.Y),
      }]);
      var texId = new VkDescriptorSet((IntPtr)cmd.GetTexID());
      if (texId.VkHandle != curTex.VkHandle)
      {
        commandBuffer.BindDescriptorSets(VkPipelineBindPoint.Graphics, PipelineLayout, 0, [texId], default);
        curTex = texId;
      }
      commandBuffer.DrawIndexed((int)cmd.ElemCount, 1, (int)cmd.IdxOffset, (int)cmd.VtxOffset, 0);

      foreach (var idx in drawList.IdxBuffer.Data.Offset((int)cmd.IdxOffset).AsSpan((int)cmd.ElemCount))
      {
        var v = drawList.VtxBuffer[idx + (int)cmd.VtxOffset].pos;
        v = float2.Clamp(v, clipMin + displayPos, clipMax + displayPos);
        var uv = (v - displayPos) / size;

        if (first)
        {
          pxBounds = new float4(v, v);
          uvBounds = new float4(uv, uv);
          first = false;
        }
        else
        {
          pxBounds.XY = float2.Min(pxBounds.XY, v);
          pxBounds.ZW = float2.Max(pxBounds.ZW, v);
          uvBounds.XY = float2.Min(uvBounds.XY, uv);
          uvBounds.ZW = float2.Max(uvBounds.ZW, uv);
        }
      }
    }

    platformIo.Renderer_RenderState = IntPtr.Zero;

    commandBuffer.EndRenderPass();

    return new float2x4(pxBounds, uvBounds);
  }

  private struct RenderState
  {
    public CommandBuffer CommandBuffer;
    public VkPipeline Pipeline;
    public VkPipelineLayout PipelineLayout;
  }

  private class BufferPair
  {
    public RenderBuffer Vertex;
    public RenderBuffer Index;
  }

  private class RenderBuffer
  {
    public VkDeviceMemory Memory;
    public ByteSize Size;
    public VkBuffer Buffer;
    public ByteSize Alignment = ByteSize.Of(256);

    public void EnsureSize(Renderer renderer, ByteSize newSize, VkBufferUsageFlags usage)
    {
      if (Size >= newSize)
        return;

      var device = renderer.Device;

      if (Buffer.IsNotNull())
        device.DestroyBuffer(Buffer, null);
      if (Memory.IsNotNull())
        device.FreeMemory(Memory, null);

      Size = newSize.AlignTo(Alignment);
      Buffer = device.CreateBuffer(new VkBufferCreateInfo
      {
        Size = Size,
        Usage = usage,
        SharingMode = VkSharingMode.Exclusive,
      }, null);

      var memReq = device.GetBufferMemoryRequirements(Buffer);
      Alignment = ByteSize.Max(Alignment, memReq.Alignment);
      Memory = device.AllocateMemory(new VkMemoryAllocateInfo
      {
        AllocationSize = memReq.Size,
        MemoryTypeIndex = MemoryType(
          renderer.PhysicalDevice, VkMemoryPropertyFlags.HostVisibleBit, memReq.MemoryTypeBits),
      }, null);
      device.BindBufferMemory(Buffer, Memory, ByteSize.Zero);
    }

    public Ptr<T> Map<T>(Device device) where T : unmanaged => device.MapMemory<T>(Memory);
    public void Unmap(Device device) => device.UnmapMemory(Memory);

    public VkMappedMemoryRange Range => new() { Memory = Memory, Size = VK.WHOLE_SIZE };

    private static int MemoryType(PhysicalDevice physicalDevice, VkMemoryPropertyFlags properties, int typeBits)
    {
      var memProps = physicalDevice.GetMemoryProperties();
      for (var i = 0; i < memProps.MemoryTypeCount; i++)
      {
        if ((memProps.MemoryTypes[i].PropertyFlags & properties) == properties && (typeBits & (1 << i)) != 0)
          return i;
      }
      return -1;
    }
  }
}