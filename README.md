# KittenExtensions

Utilities for extending KSA assets.

Current Features:
- Allows adding new xml asset types that can be used by any mod XML
- Adds a `<ShaderEx>` asset that allows adding additional texture and uniform buffer bindings to the fragment shader of a gauge component
- Adds a `<GaugeCanvasEx>` asset that allows adding a post-processing shader to the rendered gauge
- Adds a `<ImGuiShader>` asset that allows running a custom shader for a specific window

## Installation

- Required [Starmap](https://github.com/StarMapLoader/StarMap)
- Download zip from [Releases](https://github.com/tsholmes/KittenExtensions/releases/latest) and extract into game `Content` folder
- Add to `manifest.toml` in `%USER%/my games/Kitten Space Agency`
    ```toml
    [[mods]]
    id = "KittenExtensions"
    enabled = true
    ```

## Modder Usage

### XML Extensions

To add a new XML asset type, first add the KittenExtensions attributes to your assembly. At least one of these attributes must be defined in the **same** assembly as the classes for the XML types you are adding.
```cs
#pragma warning disable CS9113
using System;
namespace KittenExtensions
{
  [AttributeUsage(AttributeTargets.Class)]
  internal class KxAssetAttribute(string xmlElement) : Attribute;
  [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
  internal class KxAssetInjectAttribute(Type parent, string member, string xmlElement) : Attribute;
}
```

Then make your custom XML asset type. For top-level assets it should extend at least `SerializedId`, but more likely you will want to extend an existing asset type. For types that are only used as an option for an existing field, it only needs to extend the type of the field.

```cs
[KxAsset("ShaderEx")] // add <ShaderEx> tag to root <Assets> element
[KxAssetInject(
  // inject into the GaugeComponent.FragmentShader field as <FragmentEx>
  typeof(GaugeComponent), nameof(GaugeComponent.FragmentShader), "FragmentEx"
)]
public class ShaderEx : ShaderReference
{
  // your custom type here
}
```

```cs
// Allow specifying gauge box color as <HexColor Hex="FFFFFF" />
[KxAssetInject(typeof(GaugeBoxReference), nameof(GaugeBoxReference.Color), "HexColor")]
public class HexColor : ColorReference
{
  [XmlAttribute("Hex")]
  public string Hex = "FFFFFF";

  public override void OnDataLoad(Mod mod)
  {
    var hexVal = uint.Parse(Hex, NumberStyles.HexNumber);
    
    R = (float)((hexVal >> 16) & 0xFF) / 0xFF;
    G = (float)((hexVal >> 8) & 0xFF) / 0xFF;
    B = (float)((hexVal >> 0) & 0xFF) / 0xFF;
  }
}
```

### Shader Extensions

Additional bindings can be added to a shader by using the `ShaderEx` top-level tag, or the `FragmentEx` tag in a gauge component. Top-level defined shaders will still only have the additional bindings injected when used as a gauge component fragment shader

```xml
<Assets>
  <ShaderEx Id="MyFragmentShader" Path="MyShader.frag">
    <TextureBinding Path="Texture1.png" />
    <TextureBinding Path="Texture2.png" />
    <MyBuffer Id="MyBuf" Size="1" />
  </ShaderEx>
</Assets>
```

```xml
<Component>
  <FragmentEx Path="MyShader.frag">
    <TextureBinding Path="Texture1.png" />
    <TextureBinding Path="Texture2.png" />
    <MyBuffer Id="MyBuf" Size="1" />
  </FragmentEx>
</Component>
```

The additional bindings will be available in the fragment shader on set 1, starting from binding 1 (binding 0 will be the existing gauge font atlas)

```glsl
// in MyShader.frag
layout(set = 1, binding = 1) uniform sampler2D texture1;
layout(set = 1, binding = 2) uniform sampler2D texture2;
layout(set = 1, binding = 3) uniform MyBuffer {
  float v1;
  float v2;
};
```

#### Uniform Buffers

To use uniform buffers, first add the uniform buffer attributes to your assembly. At least one of these attributes must be defined in the **same** assembly as the uniform buffer struct.
```cs
#pragma warning disable CS9113
using System;
namespace KittenExtensions
{

  [AttributeUsage(AttributeTargets.Struct)]
  internal class KxUniformBufferAttribute(string xmlElement) : Attribute;

  [AttributeUsage(AttributeTargets.Field)]
  internal class KxUniformBufferLookupAttribute() : Attribute;

  // You can use your own delegate types as long as the signature matches one of these
  public delegate BufferEx KxBufferLookup(KeyHash hash);
  public delegate MappedMemory KxMemoryLookup(KeyHash hash);
  public delegate Span<T> KxSpanLookup<T>(KeyHash hash) where T : unmanaged;
  public unsafe delegate T* KxPtrLookup<T>(KeyHash hash) where T : unmanaged;
}
```

Then make your custom uniform buffer type.
```cs
// <MyBuffer Id="MyBuf" Size="1" />, where Size is the number of sequential MyBufferUbo elements in the buffer
[KxUniformBuffer("MyBuffer")]
[StructLayout(LayoutKind.Sequential, Pack=1)]
public struct MyBufferUbo
{
  public float V1;
  public float V2;

  // lookup delegate fields must be static fields on the buffer element type
  // the names and specific types of these are not relevant, as long as the delegate signature matches
  // these are not all required, but you will need at least one to be able to set the uniform data
  [KxUniformBufferLookup] public static KxBufferLoop LookupBuffer;
  [KxUniformBufferLookup] public static KxMemoryLookup LookupMemory;
  [KxUniformBufferLookup] public static KxSpanLookup<MyBufferUbo> LookupSpan; // gives a Span<T> of length Size
  [KxUniformBufferLookup] public static KxPtrLookup<MyBufferUbo> LookupPtr; // gives T* to first element
}
```

The buffers can then be accessed via a lookup function. `Id` is not required on the buffer xml element, but it is the only way you will be able to access the buffer.
```cs
Span<MyBufferUbo> data = MyBufferUbo.LookupSpan(KeyHash.Make("MyBuf"));
```

Buffers can be shared between shaders by specifying `Id` without `Size`.
```xml
<Assets>
  <ShaderEx Id="MyFragmentShader" Path="MyShader.frag">
    <MyBuffer Id="MyBuf" Size="1" />
  </ShaderEx>
  <ShaderEx Id="MyFragmentShader2" Path="MyShader2.frag">
    <MyBuffer Id="MyBuf" />
  </ShaderEx>
  <MyBuffer Id="MyBuf2" Size="1" />
  <ShaderEx Id="MyFragmentShader3" Path="MyShader.frag">
    <MyBuffer Id="MyBuf2" />
  </ShaderEx>
</Assets>
```

## GaugeCanvas Post-Processing

To add a post-processing shader to a Gauge, use the `<GaugeCanvasEx>` element and add a vertex and fragment shader to it. The included `GaugeVertexPost` vertex shader draws one rect covering the entire gauge, and can be used in most cases. The fragment shader is a `ShaderEx` asset that will have `layout(set=1, binding=0)` bound to the rendered gauge canvas, with custom bindings starting at `layout(set=1, binding=1)`.

```xml
<Assets>
  <GaugeCanvasEx>
    <PostVertex Id="GaugeVertexPost" />
    <PostFragment Path="MyPost.frag" />
  </GaugeCanvasEx>
</Assets>
```

```glsl
#version 450

layout(location = 0) in vec2 inUv;
layout(location = 0) out vec4 outColor;
layout(set = 1, binding = 0) uniform sampler2D gaugeCanvas;

void main()
{
  outColor = textureLod(gaugeCanvas, inUv, 0);
}
```

## ImGui Post-Processing

To add a post-processing shader to an ImGui window, use the `<ImGuiShader>` asset with a vertex and fragment shader specified. The included `ImGuiVertexPost` vertex shader draws one rect covering the bounding box of the imgui rendering calls, and can be used in most cases. The fragment shader is a `ShaderEx` asset that will have `layout(set=0, binding=0)` bound to the rendered ImGui window, with custom bindings starting at `layout(set=0, binding=1)`.

```xml
<Assets>
  <ImGuiShader Id="MyImGuiShader">
    <Vertex Id="ImGuiVertexPost" />
    <Fragment Path="MyImGuiShader.frag" />
  </ImGuiShader>
</Assets>
```

```glsl
#version 450 core

layout(location = 0) out vec4 outColor;
layout(set=0, binding=0) uniform sampler2D imguiTex; // rendered ImGui Window
layout(location = 0) in struct {
  vec2 Px; // screen pixel coord
  vec2 Uv; // screen uv coord
} In;
layout(location = 4) flat in vec4 PxRect; // bounding pixel rect for window
layout(location = 8) flat in vec4 UvRect; // bounding uv rect for window

void main()
{
  outColor = textureLod(imguiTex, In.Uv, 0);
}
```

Then add this helper class to your assembly[^kximgui].
```cs
using Brutal;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KittenExtensions;

internal static class KxImGui
{
  internal static readonly KeyHash MarkerKey = KeyHash.Make("KxImGuiShader");
  internal static unsafe void CustomShader(KeyHash key)
  {
    var data = new uint2(MarkerKey.Code, key.Code);
    ImGui.GetWindowDrawList().AddCallback(DummyCallback, (nint)(&data), ByteSize.Of<uint2>().Bytes);
  }
  private static unsafe void DummyCallback(ImDrawList* parent_list, ImDrawCmd* cmd) { }
}
```

Then in your ImGui code, call the `KxImGui.CustomShader` utility to set the custom shader for the currently rendering ImGui window (from the most recent `ImGui.Begin` call).

```cs
// matches Id attribute of the <ImGuiShader> element
// save this value so you aren't rehashing every frame
KeyHash myShader = KeyHash.Make("MyImGuiShader");

ImGui.Begin("My Window");
KxImGui.CustomShader(myShader);

// your window contents

ImGui.End();
```

### Limitations

The rendering data from ImGui does not include any window information, only a list of `ImDrawList`, so the shader will only be run on the draw list of the rendering window. This does not include child windows, so child window contents will be overlayed on top of the parent window after the custom shader is run.

[^kximgui]: The marker key must be the hash of the string `KxImGuiShader`, but this class does not need to exist in this form in order to function, it is just a utility.