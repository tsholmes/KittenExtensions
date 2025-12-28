
using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;

namespace KittenExtensions;

[KxAsset("ShaderEx")]
[KxAssetInject(typeof(GaugeComponent), nameof(GaugeComponent.FragmentShader), "FragmentEx")]
public class ShaderEx : ShaderReference, IBinder
{
  [XmlElement("TextureBinding", typeof(TextureBindingReference))]
  public List<SerializedId> XmlBindings = [];

  [XmlIgnore]
  public List<IShaderBinding> Bindings;

  public override void OnDataLoad(Mod mod)
  {
    base.OnDataLoad(mod);
    foreach (var binding in XmlBindings)
      binding.OnDataLoad(mod);

    ModLibrary.RegisterBinder(this);
  }

  public void Bind(Renderer renderer, StagingPool stagingPool)
  {
    // FileReference already implements ILoader and uses an internal virtual method we can't override
    // Bind happens after Load, so all the bindings should be ready here as well
    Bindings = new(XmlBindings.Count);
    foreach (var binding in XmlBindings)
      Bindings.Add(((IShaderBinding)binding).Get());
  }

  public static DescriptorPoolEx CreateDescriptorPool(
    ShaderReference shader,
    Device device,
    DescriptorPoolEx.CreateInfo defaultCreate,
    VkAllocator allocator = null)
  {
    if (shader.Get() is ShaderEx frag)
      return frag.CreateDescriptorPool(device, defaultCreate.PoolSizes[0].Type, allocator);
    return device.CreateDescriptorPool(defaultCreate, allocator);

  }
  public DescriptorPoolEx CreateDescriptorPool(
    Device device,
    VkDescriptorType baseType,
    VkAllocator allocator = null)
  {
    Span<VkDescriptorPoolSize> poolSizes = stackalloc VkDescriptorPoolSize[TYPE_COUNT];
    for (var i = 0; i < TYPE_COUNT; i++)
      poolSizes[i] = new VkDescriptorPoolSize { Type = DESCRIPTOR_TYPES[i], DescriptorCount = 0 };

    // always have one base image input (gauge font atlas or subpass 1 output)
    poolSizes[TypeIndex(baseType)].DescriptorCount = 1;

    foreach (var binding in Bindings)
      poolSizes[TypeIndex(binding.DescriptorType)].DescriptorCount += binding.DescriptorCount;

    var nonZero = 0;
    for (var i = 0; i < TYPE_COUNT; i++)
      if (poolSizes[i].DescriptorCount > 0)
        poolSizes[nonZero++] = poolSizes[i];

    poolSizes = poolSizes[..nonZero];

    return device.CreateDescriptorPool(new DescriptorPoolEx.CreateInfo
    {
      MaxSets = 1,
      PoolSizes = poolSizes,
    }, allocator);
  }

  public static DescriptorSetLayoutEx CreateDescriptorSetLayout(
    ShaderReference shader,
    Device device,
    DescriptorSetLayoutEx.CreateInfo defaultCreate,
    VkAllocator allocator = null)
  {
    if (shader.Get() is ShaderEx frag)
      return frag.CreateDescriptorSetLayout(device, defaultCreate.Bindings[0], allocator);
    return device.CreateDescriptorSetLayout(defaultCreate, allocator);
  }
  public DescriptorSetLayoutEx CreateDescriptorSetLayout(
    Device device,
    VkDescriptorSetLayoutBinding baseBinding,
    VkAllocator allocator = null)
  {
    var bindingCount = Bindings.Count;
    Span<VkDescriptorSetLayoutBinding> bindings = stackalloc VkDescriptorSetLayoutBinding[1 + bindingCount];
    bindings[0] = baseBinding;
    for (var i = 0; i < bindingCount; i++)
    {
      var binding = Bindings[i];
      bindings[i + 1] = new VkDescriptorSetLayoutBinding
      {
        Binding = i + 1,
        DescriptorType = binding.DescriptorType,
        DescriptorCount = binding.DescriptorCount,
        StageFlags = VkShaderStageFlags.FragmentBit,
      };
    }
    return device.CreateDescriptorSetLayout(
      new DescriptorSetLayoutEx.CreateInfo { Bindings = bindings }, allocator);
  }

  public static void UpdateDescriptorSets(
    ShaderReference shader, Device device, ReadOnlySpan<VkWriteDescriptorSet> defaultWrites)
  {
    if (shader.Get() is ShaderEx frag)
      frag.UpdateDescriptorSets(device, defaultWrites[0]);
    else
      device.UpdateDescriptorSets(defaultWrites, []);
  }
  public unsafe void UpdateDescriptorSets(Device device, VkWriteDescriptorSet baseWrite)
  {
    var bindingCount = Bindings.Count;
    Span<int> writeCounts = [0, 0, 0];
    foreach (var binding in Bindings)
      writeCounts[(int)TypeWriteType(binding.DescriptorType)] += binding.DescriptorCount;

    VkDescriptorImageInfo* imageInfos =
      stackalloc VkDescriptorImageInfo[writeCounts[(int)WriteType.ImageInfo]];
    VkDescriptorBufferInfo* bufferInfos =
      stackalloc VkDescriptorBufferInfo[writeCounts[(int)WriteType.BufferInfo]];
    VkBufferView* texelBufferViews =
      stackalloc VkBufferView[writeCounts[(int)WriteType.TexelBufferView]];

    Span<VkWriteDescriptorSet> writes = stackalloc VkWriteDescriptorSet[bindingCount + 1];
    Span<int> writeIndices = [0, 0, 0];

    writes[0] = baseWrite;
    var dset = writes[0].DstSet;

    for (var i = 0; i < bindingCount; i++)
    {
      var binding = Bindings[i];
      var wtype = TypeWriteType(binding.DescriptorType);
      var start = writeIndices[(int)wtype];
      var count = binding.DescriptorCount;

      writes[i + 1] = new VkWriteDescriptorSet
      {
        DescriptorType = binding.DescriptorType,
        DstBinding = i + 1,
        DescriptorCount = count,
        DstSet = dset,
        ImageInfo = &imageInfos[writeIndices[(int)WriteType.ImageInfo]],
        BufferInfo = &bufferInfos[writeIndices[(int)WriteType.BufferInfo]],
        TexelBufferView = &texelBufferViews[writeIndices[(int)WriteType.TexelBufferView]],
      };

      binding.WriteDescriptors(wtype switch
      {
        WriteType.ImageInfo => new() { ImageInfo = new(&imageInfos[start], count) },
        WriteType.BufferInfo => new() { BufferInfo = new(&bufferInfos[start], count) },
        WriteType.TexelBufferView => new() { TexelBufferView = new(&texelBufferViews[start], count) },
        _ => throw new InvalidOperationException($"{wtype}"),
      });

      writeIndices[(int)wtype] += count;
    }

    device.UpdateDescriptorSets(writes, []);
  }

  private const int TYPE_COUNT = 3;
  private static readonly VkDescriptorType[] DESCRIPTOR_TYPES =
  [
    VkDescriptorType.CombinedImageSampler,
    VkDescriptorType.UniformBufferDynamic,
    VkDescriptorType.InputAttachment,
  ];
  private static int TypeIndex(VkDescriptorType type) => type switch
  {
    VkDescriptorType.CombinedImageSampler => 0,
    VkDescriptorType.UniformBufferDynamic => 1,
    VkDescriptorType.InputAttachment => 2,
    _ => throw new NotSupportedException($"{type}"),
  };

  private enum WriteType { ImageInfo = 0, BufferInfo = 1, TexelBufferView = 2 }
  private static WriteType TypeWriteType(VkDescriptorType type) => type switch
  {
    VkDescriptorType.CombinedImageSampler => WriteType.ImageInfo,
    VkDescriptorType.UniformBufferDynamic => WriteType.BufferInfo,
    VkDescriptorType.InputAttachment => WriteType.ImageInfo,
    _ => throw new NotSupportedException($"{type}"),
  };
}
