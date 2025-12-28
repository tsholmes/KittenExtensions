
using System;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using RenderCore;

namespace KittenExtensions;

public class PostProcessRenderer : RenderTechnique
{
  private readonly GaugeCanvasEx canvas;

  private RenderTarget renderTarget;
  private readonly VkRenderPass renderPass;

  private readonly DescriptorSetLayoutEx bindingLayout;
  private readonly VkDescriptorSet bindingSet;

  public unsafe PostProcessRenderer(
    GaugeCanvasEx canvas,
    Renderer renderer,
    RenderPassState renderState,
    Span<ShaderReference> inShaderPaths)
  : base(nameof(PostProcessRenderer), renderer, renderState, inShaderPaths)
  {
    this.canvas = canvas;

    renderTarget = new(renderer, new(500, 500), VkFormat.R8G8B8A8UNorm, VkFormat.Undefined);
    renderPass = renderTarget.CreateRenderPass();
    renderTarget.BuildFramebuffer(renderPass);

    canvas.CanvasRenderer.ImguiTextureID = ImGuiBackend.Vulkan.AddTexture(
      Program.LinearClampedSampler, renderTarget.ColorImage.ImageView);

    if (inShaderPaths[1] is not ShaderEx frag)
      throw new InvalidOperationException($"expected ShaderEx fragment shader");
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
      ImageView = canvas.CanvasRenderer.RenderTarget.ColorImage.ImageView,
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

    PipelineLayout = device.CreatePipelineLayout(
      [GlobalShaderBindings.DescriptorSetLayout, bindingLayout], [], null);

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

  public unsafe void Resize(float2 size)
  {
    Renderer.Device.WaitIdle();
    ImGuiBackend.Vulkan.RemoveTexture(canvas.CanvasRenderer.ImguiTextureID);
    renderTarget.Dispose();
    renderTarget = new(Renderer, new((int)size.X, (int)size.Y), VkFormat.R8G8B8A8UNorm, VkFormat.Undefined);
    renderTarget.BuildFramebuffer(renderPass);
    canvas.CanvasRenderer.ImguiTextureID =
      ImGuiBackend.Vulkan.AddTexture(Program.LinearClampedSampler, renderTarget.ColorImage.ImageView);

    // also need to update the input binding

    VkDescriptorImageInfo* inputInfo = stackalloc VkDescriptorImageInfo[1];
    inputInfo[0] = new VkDescriptorImageInfo
    {
      ImageLayout = VkImageLayout.ShaderReadOnlyOptimal,
      ImageView = canvas.CanvasRenderer.RenderTarget.ColorImage.ImageView,
      Sampler = Program.LinearClampedSampler,
    };

    Renderer.Device.UpdateDescriptorSets([
      new VkWriteDescriptorSet
      {
        DescriptorType = VkDescriptorType.CombinedImageSampler,
        DstBinding = 0,
        DescriptorCount = 1,
        DstSet = bindingSet,
        ImageInfo = inputInfo,
      }
    ], []);
  }

  public void Render(CommandBuffer commandBuffer, Viewport viewport)
  {
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
      MaxDepth = 1f
    }]);
    commandBuffer.SetScissor(0, [rect]);

    commandBuffer.BindDescriptorSets(
      VkPipelineBindPoint.Graphics, PipelineLayout, 0,
      [GlobalShaderBindings.DescriptorSet, bindingSet],
      [GlobalShaderBindings.DynamicOffset(viewport.Index)]);

    commandBuffer.Draw(4, 1, 0, 0);

    commandBuffer.EndRenderPass();
  }
}