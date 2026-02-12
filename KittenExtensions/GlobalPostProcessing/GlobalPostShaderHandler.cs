using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using System;
using System.Collections.Generic;
using System.Linq;
using static KSA.Framebuffer;

namespace KittenExtensions.GlobalPostProcessing;

internal static class GlobalPostShaderHandler
{
    public static OffscreenTarget offscreenTarget2;
    private static List<GlobalPostRenderData> ShaderData = [];

    public static unsafe void Rebuild()
    {
        foreach (var shader in ShaderData) shader.Dispose();
        ShaderData.Clear();

        FramebufferAttachment source = offscreenTarget2.ColorImage;

        foreach (var passKvp in GlobalPostShaderAsset.ShadersByPassAndSubpass)
        {
            List<GlobalPostShaderAsset> uniqueSubpassShaders = passKvp.Value.SelectMany(kvp => kvp.Value).Where(s => s.RequiresUniqueRenderpass).ToList();
            uniqueSubpassShaders.ForEach(s =>
            {
                GlobalPostRenderData renderData = new GlobalPostRenderData([s], source);
                ShaderData.Add(renderData);
                source = renderData.TargetAttachment;
            });

            List<GlobalPostShaderAsset> subpassShaders = passKvp.Value.SelectMany(kvp => kvp.Value).Where(s => !s.RequiresUniqueRenderpass).ToList();
            if (subpassShaders.Count > 0)
            {
                GlobalPostRenderData renderData = new GlobalPostRenderData(subpassShaders, source);
                ShaderData.Add(renderData);
                source = renderData.TargetAttachment;
            }
        }
    }

    /// <summary>
    /// Called after the UI renderpass has finished
    /// </summary>
    public static unsafe void RenderNow(CommandBuffer commandBuffer, FrameResources destFrameResources, int dynamicOffset = 0)
    {
        Renderer renderer = Program.GetRenderer();
        VkExtent2D extent = renderer.Extent;

        FramebufferAttachment lastAttachment = offscreenTarget2.ColorImage;

        foreach (var shaderData in ShaderData)
        {
            shaderData.Render(commandBuffer);
            lastAttachment = shaderData.TargetAttachment;
        }

        // Copy the result to the destination framebuffer's color attachment
        commandBuffer.CopyImage(
            srcImage: lastAttachment.Image,
            srcImageLayout: VkImageLayout.ColorAttachmentOptimal,
            dstImage: destFrameResources.ColorImage,
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