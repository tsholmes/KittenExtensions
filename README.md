# KittenExtensions

KSA Modding Utilities

NOTE: This is still under development and the XML/API may change. It is also likely going to be split into 2 mods (xml patching/assets in one, shaders in another)

Current Features:
- Allows modders to write patches that alter any XML file (including in Core or other mods)
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


## XML Patching
**NOTE: The patch format is not finalized and may change**

Uses the [XPathPatch](https://github.com/tsholmes/XPathPatch) library.

To patch game xml files, add a new patch file entry to your mod.toml
```toml
# MyMod/mod.toml
name = "MyMod"

patches = [ "MyPatch.xml" ]
```
and make the corresponding patch file.
```xml
<!-- MyMod/MyPatch.xml -->
<Patch>
  <!-- patches -->
</Patch>
```

### GameData Document
Patch operations run against the GameData xml document, constructed from xml files from all enabled mods
```xml
<Root>
  <!-- each enabled mod in same order as manifest -->
  <Mod Id="Core">
    <!-- each asset/system/meshcollection/patch file loaded in and RelPath attribute added -->
    <System Id="Sol" RelPath="SolSystem.xml">
      <!-- rest of file contents -->
    </System>
    <Assets RelPath="Astronomicals.xml">
      <!-- ... -->
    </Assets>
  </Mod>
  <Mod Id="MyMod">
    <!-- ... -->
    <Patch RelPath="MyPatch.xml">
      <!-- ... -->
    </Patch>
  </Mod>
</Root>
```

If you want to inspect the GameData document after patching is done, you can enable the debug flag in your mod manifest:
```toml
# %USER%/my games/Kitten Space Agency/manifest.toml

[[mods]]
id = "KittenExtensions"
enabled = true
debug = true
```
This will show a debug view that allows you to step through patches on startup. It also saves a `root.xml` file to the same directory as the `manifest.toml` which contains the unpatched GameData document.

### Patch Execution
After loading the GameData document, each `<Patch>` file under each `<Mod>` is executed in order. The list of `<Mod>` and `<Patch>` nodes are loaded at the start, so reordering or removing those elements will not affect the patches run. The data inside a `<Patch>` however is read at the time it is executed, so patches may alter patches that will be executed after it. Each operation element in each `<Patch>` is executed with the `<Root>` node as the context (starting with an XPath of `/Root/`, not `/`).

### Patch Operations

#### Path Attribute

The `Path` attribute must be a valid XPath 1.0 expression ([RFC](https://www.w3.org/TR/1999/REC-xpath-19991116/), [Wiki](https://en.wikipedia.org/wiki/XPath)). It must resolve to a `node-set` as defined in the spec.

The `Path` is executed from the current context node (`<Root>` unless inside a `<With>` op).

#### Pos Attribute
 `<Copy>` operations have a `Pos` attribute that defines where the update should occur. This value has different meaning depending on the selected node.

| `Pos` | Element | Text/Attribute |
| --- | --- | --- |
| `Replace` | replace entire element | replace value |
| `Append` | add to end of children | add to end of value |
| `Prepend` | add to beginning of children | add to beginning of value |
| `Before` | insert as previous sibling | Invalid |
| `After` | insert as following sibling | Invalid |

#### `<Copy>`
Patches xml nodes at `Path` (defaults to context node) with the value at `From` (defaults `<Copy>` element contents) using the given `Pos` (defaults to `Replace`). 
```xml
<Copy Path="Target XPath" From="Source XPath" Pos="Pos" />
<Copy Path="Target XPath" Pos="Pos">
  <!-- From Xml -->
</Copy>
```

#### `<Merge>`
Merges xml nodes at `Path` (defaults to context node) with the value at `From` (defaults to `<Merge>` element contents). See the [Merging](#merging) section below for details on merge semantics.
```xml
<Merge Path="Target XPath" From="Source XPath" Pos="Pos" />
<Merge Path="Target XPath" Pos="Pos">
  <!-- From Xml -->
</Merge>
```

#### `<Delete>`
Deletes xml nodes at `Path` (defaults to context node).
```xml
<Delete Path="Target XPath" />
```

#### `<If>` `<IfAny>` `<IfNone>`
Executes a set of operations depending on the result of the `Path` expression. Child operations are run with the same context node as the `<If>` operation.
```xml
<If Path="XPath Expression">
  <Any>
    <!-- any patch op element -->
    <!-- runs when Path is true, non-zero and non-NaN, a non-empty string, or a non-empty node-set -->
  </Any>
  <None>
    <!-- any patch op element -->
    <!-- runs when Path is false, zero or NaN, an empty string, or an empty node-set -->
  </None>
</If>
<IfAny Path="XPath Expression">
  <!-- any patch op element -->
  <!-- equivalient to <If><Any>...</Any></If> -->
</IfAny>
<IfNone Path="XPath Expression">
  <!-- any patch op element -->
  <!-- equivalient to <If><None>...</None></If> -->
</IfNone>
```

#### `<With>`
Executes a set of operations using the `Path` nodes as the context. When `Path` selects multiple nodes, the child contents will be run **once for each selected node**.
```xml
<With Path="Context XPath">
  <!-- any patch op element -->
</With>
```

#### `<SetVar>`
Saves the result of the `Path` expression (default `<SetVar>` contents) to a variable with the given `Name`.
```xml
<!-- sets $myvar0 to the string 'Value' -->
<SetVar Name="myvar0">Value</SetVar>
<!-- sets $myvar to the sum of all attribute values holding positive numbers -->
<SetVar Name="myvar1" Path="sum(//@*[.>0])" />
<!-- overwrites $myvar1, referencing its last value in the expression -->
<SetVar Name="myvar1" Path="$myvar1*2" />
<!-- stores a node list in $myvar2 -->
<SetVar Name="myvar2" Path="Mod/Assets" />
<!-- use the stored $myvar2 node list in a path expression -->
<With Path="$myvar2/Character">
  <!-- ... -->
</With>
```

#### Merging
When using the `<Merge>` operation, each selected source element is merged with each selected target element.
- All attributes are copied from the source element (replacing the existing value if present)
- Each child of the source is matched with a child node in the target
  - If the source element has an `Id` attribute, it matches with the first child element of the target with the same element name and `Id`
  - If the source element does not have an `Id`, it matches with the first child element of the target with the same element name
  - The attribute used to match can be configured for each source element with a `_MergeId` attribute
    - `_MergeId="*"` means any matching element name
    - `_MergeId="-"` means never match
    - `_MergeId="AttrName"` means match non-empty values of the `AttrName` attribute
  - If a match is not found (and for all text elements), the source child node is added as a child of the target element
  - The position the child is inserted can be controlled with a `_MergePos` attribute on the source parent element
    - `_MergePos="Append"` (default) adds unmatched nodes after existing children
    - `_MergePos="Prepend"` adds unmatched nodes before existing children

```xml
<!-- Merging -->
<Source _MergePos="Prepend" A="B">
  <X Id="1">a</X>
  <X Id="2" Name="Y" _MergeId="Name">b</X>
  <X Id="3" _MergeId="-">c</X>
</Source>
<!-- Into -->
<Target>
  <X Id="1">d</X>
  <X Id="2">e</X>
  <X Id="3" Name="Y" Z="true">f</X>
</Target>
<!-- Produces -->
<Target A="B">
  <X Id="3">c</X> <!-- Source X 3 prepended since _MergeId set to not match -->
  <X Id="1">a</X>
  <X Id="2">e</X> <!-- not matched since _MergeId set to Name -->
  <X id="2" Name="Y" Z="true">b</X> <!-- Source X 2 merged into Target X 3 matched by Name -->
</Target>
```

### Examples
Copy a planet from `Core` into a custom `<System>`
```xml
<Patch>
  <!-- set context to MyMod -->
  <With Path="Mod[@Id='MyMod']">
    <!-- Copy Venus from core system into custom system (assuming MySystem already exists) -->
    <Copy
      Path="System[@Id='MySystem']"
      From="/Root/Mod[@Id='Core']/System[@Id='SolSystem']/AtmosphericBody[@Id='Venus']"
      Pos="Append"
    />
    <!-- set context to the copied Venus -->
    <With Path="System/AtmosphericBody[@Id='Venus']">
      <!-- Change Id to MyVenus -->
      <Copy Path="@Id" Pos="Prepend">My</Copy>
      <!-- Make it a little heavier -->
      <Copy Path="Mass/@Earths">1</Copy>
      <!-- Remove stratus clouds -->
      <Remove Path="Clouds/CloudType[@Name='Stratus']" />
    </With>
  </With>
</Patch>
```

Adjust orbit colors of planets based on current value
```xml
<Patch>
  <!-- run for each PlanetaryBody and AtmosphericBody anywhere in the GameData document -->
  <With Path="//PlanetaryBody | //AtmosphericBody">
    <If Path="sum(Color/@*) > 1.5">
      <Any>
        <!-- if R+G+B of orbit color is >1.5 make each RGB val brighter -->
        <With Path="Color/@*">
          <Copy From="1-(1-.)*0.5" />
        </With>
      </Any>
      <None>
        <!-- otherwise make each RGB val darker -->
        <With Path="Color/@*">
          <Copy From=".*0.5" />
        </With>
      </None>
    </If>
  </With>
</Patch>
```

## Asset Extensions

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