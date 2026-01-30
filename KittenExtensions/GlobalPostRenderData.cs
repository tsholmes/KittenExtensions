using Brutal;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using System;
using System.Collections.Generic;
using System.Linq;
using static KSA.Framebuffer;

namespace KittenExtensions
{
    public class GlobalPostRenderData : IComparable<GlobalPostRenderData>, IDisposable
    {
        public int SubPassCount { get; private set; }
        public bool UniqueRenderPass { get; private set; }
        public int RenderPassIndex { get; private set; }
        public RenderPassState RenderPass { get; private set; }
        public FramebufferAttachment[] Attachments { get; private set; }
        public FramebufferAttachment SourceAttachment { get; private set; }
        public FramebufferAttachment TargetAttachment => Attachments.LastOrDefault();
        public VkFramebuffer Framebuffer { get; private set; }
        public SortedDictionary<int, GlobalPostRenderer> PostProcessShaders { get; private set; }


        public void Dispose()
        {
            Renderer renderer = Program.GetRenderer();
            foreach (var attachment in Attachments)
            {
                renderer.Device.DestroyImageView(attachment.ImageView, null);
                renderer.Device.DestroyImage(attachment.Image, null);
                renderer.Device.FreeMemory(attachment.Memory, null);
            }
            renderer.Device.DestroyFramebuffer(Framebuffer, null);
            foreach (var shader in PostProcessShaders.Values)
            {
                shader.Dispose();
            }
        }


        public unsafe void Render(CommandBuffer commandBuffer)
        {
            Renderer renderer = Program.GetRenderer();
            VkExtent2D extent = renderer.Extent;

            commandBuffer.PipelineBarrier(
                srcStageMask: VkPipelineStageFlags.ColorAttachmentOutputBit,
                dstStageMask: VkPipelineStageFlags.ColorAttachmentOutputBit,
                dependencyFlags: VkDependencyFlags.None,
                pMemoryBarriers: ReadOnlySpan<VkMemoryBarrier>.Empty,
                pBufferMemoryBarriers: ReadOnlySpan<VkBufferMemoryBarrier>.Empty,
                pImageMemoryBarriers: new VkImageMemoryBarrier[]
                {
                    new VkImageMemoryBarrier
                    {
                        SrcAccessMask = VkAccessFlags.ColorAttachmentWriteBit,
                        DstAccessMask = VkAccessFlags.ShaderReadBit,
                        OldLayout = VkImageLayout.ColorAttachmentOptimal,
                        NewLayout = VkImageLayout.ShaderReadOnlyOptimal,
                        SrcQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
                        DstQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
                        Image = SourceAttachment.Image,
                        SubresourceRange = SourceAttachment.SubresourceRange
                    }
                }
            );

            if (UniqueRenderPass)
            {
                PostProcessShaders[0].RenderSinglePass(commandBuffer, RenderPass, Framebuffer);
            }
            else
            {
                commandBuffer.BeginRenderPass(new VkRenderPassBeginInfo
                {
                    RenderPass = RenderPass.Pass,
                    Framebuffer = Framebuffer,
                    RenderArea = new VkRect2D(extent),
                }, VkSubpassContents.Inline);

                for (int i = 0; i < SubPassCount; i++)
                {
                    PostProcessShaders[i].RenderSubpass(commandBuffer);

                    if (i < SubPassCount - 1) commandBuffer.NextSubpass(VkSubpassContents.Inline);
                }

                commandBuffer.EndRenderPass();
            }
        }

        public int CompareTo(GlobalPostRenderData other)
        {
            return RenderPassIndex.CompareTo(other.RenderPassIndex);
        }

        public unsafe GlobalPostRenderData(List<GlobalPostShaderAsset> shaders, FramebufferAttachment source)
        {
            int uniqueRenderPassCount = shaders.Count(x => x.RequiresUniqueRenderpass);
            if (uniqueRenderPassCount > 1 || uniqueRenderPassCount == 1 && shaders.Count > 1)
            {
                throw new Exception("Multiple unique renderpass shaders in the same pass are not supported.");
            }

            Renderer renderer = Program.GetRenderer();
            RenderPassIndex = shaders[0].RenderPassId;
            SubPassCount = shaders.Count;
            SourceAttachment = source;

            VkImageSubresourceRange subresourceRange = new VkImageSubresourceRange();
            {
                subresourceRange.AspectMask = VkImageAspectFlags.ColorBit;
                subresourceRange.LevelCount = 1;
                subresourceRange.LayerCount = 1;
                subresourceRange.BaseMipLevel = 0;
                subresourceRange.BaseArrayLayer = 0;
            };

            VkImageCreateInfo imageCreateInfo = new VkImageCreateInfo
            {
                ImageType = VkImageType._2D,
                Flags = VkImageCreateFlags.None,
                Format = renderer.ColorFormat,
                Extent = new VkExtent3D
                {
                    Width = renderer.Extent.Width,
                    Height = renderer.Extent.Height,
                    Depth = 1
                },
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = VkSampleCountFlags._1Bit,
                Usage = VkImageUsageFlags.ColorAttachmentBit |
                        VkImageUsageFlags.InputAttachmentBit |
                        VkImageUsageFlags.TransferSrcBit |
                        VkImageUsageFlags.SampledBit
            };

            if (SubPassCount == 1 && shaders[0].RequiresUniqueRenderpass)
            {
                UniqueRenderPass = true;

                Attachments = new FramebufferAttachment[1]; // Sampler2D as input, ColorAttachment as output

                FramebufferAttachment attachment = new FramebufferAttachment()
                {
                    Image = renderer.Device.CreateImage(imageCreateInfo, null),
                    Format = renderer.ColorFormat,
                };

                attachment.Memory = renderer.Device.AllocateMemory(renderer.PhysicalDevice, attachment.Image, VkMemoryPropertyFlags.DeviceLocalBit, null);
                renderer.Device.BindImageMemory(attachment.Image, attachment.Memory, (ByteSize64)ByteSize.Zero);
                attachment.SubresourceRange = subresourceRange;

                VkImageViewCreateInfo imageViewCreateInfo = new VkImageViewCreateInfo
                {
                    Image = attachment.Image,
                    ViewType = VkImageViewType._2D,
                    Format = renderer.ColorFormat,
                    SubresourceRange = subresourceRange
                };

                attachment.ImageView = renderer.Device.CreateImageView(imageViewCreateInfo, null);
                Attachments[0] = attachment;

                RenderPass = GlobalPostRenderer.CreateSingleRenderPass(renderer);

                VkImageView* views = stackalloc VkImageView[1];
                views[0] = attachment.ImageView;
                VkFramebufferCreateInfo fbInfo = new VkFramebufferCreateInfo
                {
                    RenderPass = RenderPass.Pass,
                    AttachmentCount = 1,
                    Attachments = views,
                    Width = renderer.Extent.Width,
                    Height = renderer.Extent.Height,
                    Layers = 1
                };
                Framebuffer = renderer.Device.CreateFramebuffer(fbInfo, null);

                PostProcessShaders = new SortedDictionary<int, GlobalPostRenderer> {
                    {
                        0, new GlobalPostRenderer(renderer, source, RenderPass, shaders[0], uniqueRenderpass: true)
                    } 
                };
            }
            else
            {
                Attachments = new FramebufferAttachment[SubPassCount + 1];
                Attachments[0] = source;

                for (int i = 1; i <= SubPassCount; i++)
                {
                    FramebufferAttachment attachment = new FramebufferAttachment()
                    {
                        Image = renderer.Device.CreateImage(imageCreateInfo, null),
                        Format = renderer.ColorFormat,
                    };

                    attachment.Memory = renderer.Device.AllocateMemory(renderer.PhysicalDevice, attachment.Image, VkMemoryPropertyFlags.DeviceLocalBit, null);
                    renderer.Device.BindImageMemory(attachment.Image, attachment.Memory, (ByteSize64)ByteSize.Zero);
                    attachment.SubresourceRange = subresourceRange;

                    VkImageViewCreateInfo imageViewCreateInfo = new VkImageViewCreateInfo
                    {
                        Image = attachment.Image,
                        ViewType = VkImageViewType._2D,
                        Format = renderer.ColorFormat,
                        SubresourceRange = subresourceRange
                    };

                    attachment.ImageView = renderer.Device.CreateImageView(imageViewCreateInfo, null);
                    Attachments[i] = attachment;
                }

                RenderPass = GlobalPostRenderer.CreateMultiRenderPass(renderer, SubPassCount);

                VkImageView* views = stackalloc VkImageView[SubPassCount + 1];
                for (int i = 0; i <= SubPassCount; i++) views[i] = Attachments[i].ImageView;

                VkFramebufferCreateInfo fbInfo = new VkFramebufferCreateInfo
                {
                    RenderPass = RenderPass.Pass,
                    AttachmentCount = SubPassCount + 1,
                    Attachments = views,
                    Width = renderer.Extent.Width,
                    Height = renderer.Extent.Height,
                    Layers = 1
                };
                Framebuffer = renderer.Device.CreateFramebuffer(fbInfo, null);

                shaders.Sort((a, b) => a.SubpassId.CompareTo(b.SubpassId));
                PostProcessShaders = new SortedDictionary<int, GlobalPostRenderer>();
                for (int i = 0; i < shaders.Count; i++)
                {
                    GlobalPostShaderAsset shader = shaders[i];
                    FramebufferAttachment input = Attachments[i];
                    GlobalPostRenderer finalPostRenderer = new GlobalPostRenderer(renderer, input, RenderPass, shader, subPass: i);
                    PostProcessShaders.Add(i, finalPostRenderer);
                }
            }
        }
    }
}
