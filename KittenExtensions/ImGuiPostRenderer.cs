
using System;
using System.Runtime.InteropServices;
using Brutal;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using KSA.Rendering;
using RenderCore;

namespace KittenExtensions;

public class ImGuiPostRenderer : RenderTechnique
{
  public readonly ImGuiBaseRenderer BaseRenderer;
  private RenderTarget renderTarget;
  private readonly VkRenderPass renderPass;

  private readonly DescriptorSetLayoutEx bindingLayout;
  private readonly VkDescriptorSet bindingSet;

  public ImTextureRef ImGuiTexture { get; private set; }
  public Framebuffer.FramebufferAttachment Target => renderTarget.ColorImage;

  public unsafe ImGuiPostRenderer(Renderer renderer, VkExtent2D extent, ShaderReference vert, ShaderEx frag)
    : base(nameof(ImGuiPostRenderer), renderer, Program.MainPass, [vert, frag])
  {
    BaseRenderer = new(renderer, extent);
    renderTarget = new(renderer, extent, VkFormat.R8G8B8A8UNorm, VkFormat.Undefined);
    renderPass = renderTarget.CreateRenderPass();
    renderTarget.BuildFramebuffer(renderPass);

    ImGuiTexture = ImGuiBackend.Vulkan.AddTexture(
      Program.LinearClampedSampler,
      renderTarget.ColorImage.ImageView);

    var device = renderer.Device;
    DescriptorPool = frag.CreateDescriptorPool(device, VkDescriptorType.CombinedImageSampler);
    bindingLayout = frag.CreateDescriptorSetLayout(device, new VkDescriptorSetLayoutBinding
    {
      Binding = 0,
      DescriptorType = VkDescriptorType.CombinedImageSampler,
      DescriptorCount = 1,
      StageFlags = VkShaderStageFlags.FragmentBit,
    });
    bindingSet = device.AllocateDescriptorSet(DescriptorPool, bindingLayout);

    VkDescriptorImageInfo* inputInfo = stackalloc VkDescriptorImageInfo[1];
    inputInfo[0] = new VkDescriptorImageInfo
    {
      ImageLayout = VkImageLayout.ShaderReadOnlyOptimal,
      ImageView = BaseRenderer.Target.ImageView,
      Sampler = Program.LinearClampedSampler,
    };

    frag.UpdateDescriptorSets(device, new VkWriteDescriptorSet
    {
      DescriptorType = VkDescriptorType.CombinedImageSampler,
      DstBinding = 0,
      DescriptorCount = 1,
      DstSet = bindingSet,
      ImageInfo = inputInfo,
    });

    var pushConst = new VkPushConstantRange()
    {
      StageFlags = VkShaderStageFlags.VertexBit,
      Offset = ByteSize.Zero,
      Size = ByteSize.Of<float>(8),
    };

    PipelineLayout = device.CreatePipelineLayout([bindingLayout], [pushConst], null);

    RebuildFrameResources();
  }

  protected override VertexInput MakeVertexInput() => null;

  protected override void OnRebuildFrameResources() => CreatePipeline(
    renderPass,
    VkPrimitiveTopology.TriangleStrip,
    VkCullModeFlags.BackBit,
    VkFrontFace.CounterClockwise,
    VkPolygonMode.Fill,
    RenderingPresets.ReverseZDepthStencil.NoDepthTest,
    Presets.BlendState.BlendNone,
    out Pipeline
  );

  public unsafe void Rebuild(VkExtent2D extent)
  {
    BaseRenderer.Rebuild(extent);

    ImGuiBackend.Vulkan.RemoveTexture(ImGuiTexture);
    renderTarget.Dispose();
    renderTarget = new(Renderer, extent, VkFormat.R8G8B8A8UNorm, VkFormat.Undefined);
    renderTarget.BuildFramebuffer(renderPass);
    ImGuiTexture = ImGuiBackend.Vulkan.AddTexture(
      Program.LinearClampedSampler,
      renderTarget.ColorImage.ImageView);

    var inputInfo = new VkDescriptorImageInfo
    {
      ImageLayout = VkImageLayout.ShaderReadOnlyOptimal,
      ImageView = BaseRenderer.Target.ImageView,
      Sampler = Program.LinearClampedSampler,
    };
    Renderer.Device.UpdateDescriptorSets([
      new VkWriteDescriptorSet
      {
        DescriptorType = VkDescriptorType.CombinedImageSampler,
        DstBinding = 0,
        DescriptorCount = 1,
        DstSet = bindingSet,
        ImageInfo = &inputInfo,
      }
    ], []);

    RebuildFrameResources();
  }

  public void Render(CommandBuffer commandBuffer, ImDrawDataPtr drawData, ImDrawListPtr drawList)
  {
    var bounds = BaseRenderer.Render(commandBuffer, drawData, drawList);

    var pxBounds = bounds.X;
    var uvBounds = bounds.Y;

    commandBuffer.TransitionImages2([
      new ImageTransition(
        BaseRenderer.Target,
        ImageBarrierInfo.Presets.ColorAttachment,
        ImageBarrierInfo.Presets.ShaderReadOnlyVertex
      )]);

    var extent = new VkExtent2D(renderTarget.Extent.Width, renderTarget.Extent.Height);
    var rect = new VkRect2D(extent);
    commandBuffer.BeginRenderPass(new VkRenderPassBeginInfo
    {
      RenderPass = renderPass,
      Framebuffer = renderTarget.FrameBuffer,
      RenderArea = rect,
    }, VkSubpassContents.Inline);

    commandBuffer.BindPipeline(VkPipelineBindPoint.Graphics, Pipeline);

    commandBuffer.SetViewport(0, [new VkViewport
    {
      Width = extent.Width,
      Height = extent.Height,
      MinDepth = 0f,
      MaxDepth = 1f,
    }]);
    commandBuffer.SetScissor(0, [rect]);

    Span<float4> pushConsts = [pxBounds, uvBounds];
    commandBuffer.PushConstants(
      PipelineLayout, VkShaderStageFlags.VertexBit, 0,
      MemoryMarshal.Cast<float4, byte>(pushConsts));

    commandBuffer.BindDescriptorSets(VkPipelineBindPoint.Graphics, PipelineLayout, 0, [bindingSet], []);

    commandBuffer.Draw(4, 1, 0, 0);

    commandBuffer.EndRenderPass();

    // replace draw list with image draw
    var vcount = drawList.VtxBuffer.Count;
    var icount = drawList.IdxBuffer.Count;
    drawList.ResetForNewFrame();
    drawList.PushClipRectFullScreen();
    drawList.AddImage(ImGuiTexture, pxBounds.XY, pxBounds.ZW, uvBounds.XY, uvBounds.ZW);
    drawData.TotalVtxCount -= vcount - drawList.VtxBuffer.Count;
    drawData.TotalIdxCount -= icount - drawList.IdxBuffer.Count;
  }
}