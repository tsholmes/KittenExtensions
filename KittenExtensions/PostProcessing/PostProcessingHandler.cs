using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static KSA.Framebuffer;

namespace KittenExtensions.PostProcessing;

internal static class PostProcessingHandler
{
    private static List<PostProcessingRenderData> ShaderData = [];

    public static unsafe void Rebuild()
    {
        foreach (var shader in ShaderData) shader.Dispose();
        ShaderData.Clear();

        OffscreenTarget offscreenTarget = typeof(Program).GetField("_offscreenTarget", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Program.Instance) as OffscreenTarget;

        FramebufferAttachment source = offscreenTarget.ColorImage;

        foreach (var passKvp in PostProcessingShaderAsset.ShadersByPassAndSubpass)
        {
            List<PostProcessingShaderAsset> uniqueSubpassShaders = passKvp.Value.SelectMany(kvp => kvp.Value).Where(s => s.RequiresUniqueRenderpass).ToList();
            uniqueSubpassShaders.ForEach(s =>
            {
                PostProcessingRenderData renderData = new PostProcessingRenderData([s], source, offscreenTarget.ColorImage.Format);
                ShaderData.Add(renderData);
                source = renderData.TargetAttachment;
            });

            List<PostProcessingShaderAsset> subpassShaders = passKvp.Value.SelectMany(kvp => kvp.Value).Where(s => !s.RequiresUniqueRenderpass).ToList();
            if (subpassShaders.Count > 0)
            {
                PostProcessingRenderData renderData = new PostProcessingRenderData(subpassShaders, source, offscreenTarget.ColorImage.Format);
                ShaderData.Add(renderData);
                source = renderData.TargetAttachment;
            }
        }
    }

    /// <summary>
    /// Called before the UI renderpass
    /// </summary>
    public static unsafe void RenderNow(CommandBuffer commandBuffer)
    {
        if (ShaderData.Count == 0) return;

        OffscreenTarget offscreenTarget = typeof(Program).GetField("_offscreenTarget", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Program.Instance) as OffscreenTarget;
        Renderer renderer = Program.GetRenderer();
        VkExtent2D extent = renderer.Extent;

        FramebufferAttachment lastAttachment = default;

        foreach (var shaderData in ShaderData)
        {
            shaderData.Render(commandBuffer);
            lastAttachment = shaderData.TargetAttachment;
        }

        // Copy the result back to the offscreen target
        commandBuffer.CopyImage(
            srcImage: lastAttachment.Image,
            srcImageLayout: VkImageLayout.ColorAttachmentOptimal,
            dstImage: offscreenTarget.ColorImage.Image,
            dstImageLayout: VkImageLayout.ColorAttachmentOptimal,
            pRegions: new ReadOnlySpan<VkImageCopy>(new VkImageCopy[]
            {
                new VkImageCopy
                {
                    SrcSubresource = new VkImageSubresourceLayers
                    {
                        AspectMask = VkImageAspectFlags.ColorBit,
                        MipLevel = 0,
                        BaseArrayLayer = 0,
                        LayerCount = 1
                    },
                    SrcOffset = new VkOffset3D(0, 0, 0),
                    DstSubresource = new VkImageSubresourceLayers
                    {
                        AspectMask = VkImageAspectFlags.ColorBit,
                        MipLevel = 0,
                        BaseArrayLayer = 0,
                        LayerCount = 1
                    },
                    DstOffset = new VkOffset3D(0, 0, 0),
                    Extent = new VkExtent3D
                    {
                        Width = renderer.Extent.Width,
                        Height = renderer.Extent.Height,
                        Depth = 1
                    }
                }
            })
        );
    }
}
