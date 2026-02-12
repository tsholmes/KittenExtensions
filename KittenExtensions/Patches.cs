using Brutal.ImGuiApi;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using HarmonyLib;
using KittenExtensions.GlobalPostProcessing;
using KittenExtensions.Patch;
using KittenExtensions.PostProcessing;
using KSA;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace KittenExtensions;

[HarmonyPatch]
internal static class Patches
{
    [HarmonyPatch(typeof(Program), "Main"), HarmonyPrefix]
    internal static void Program_Main_Prefix()
    {
        // We want to load before main, but after all mod assemblies are loaded in
        AssetEx.Init();
    }

    private static FieldInfo CanvasRenderer_canvas =
      typeof(CanvasRenderer).GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic);

    [HarmonyPatch(
      typeof(CanvasRenderer), MethodType.Constructor, [typeof(GaugeCanvas), typeof(RendererContext)]
    ), HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> CanvasRenderer_Ctor_Transpile(
      IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);
        TranspileAddTexture("CanvasRenderer.ctor", matcher);
        return matcher.Instructions();
    }

    [HarmonyPatch(typeof(CanvasRenderer), nameof(CanvasRenderer.Resize)), HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> CanvasRenderer_Resize_Transpile(
      IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);

        matcher.MatchStartForward(CodeMatch.Calls(() => default(ImGuiBackendVulkanImpl).RemoveTexture(default)));
        matcher.ThrowIfInvalid($"could not find ImGuiVulkanBackendImpl.RemoveTexture call in CanvasRenderer.Resize");

        matcher.RemoveInstruction();
        matcher.InsertAndAdvance(
          new CodeInstruction(OpCodes.Ldarg_0),
          new CodeInstruction(OpCodes.Ldfld, CanvasRenderer_canvas),
          CodeInstruction.Call(() => VulkanRemoveTexture(default, default, default)));

        TranspileAddTexture("CanvasRenderer.Resize", matcher);

        return matcher.Instructions();
    }

    private static void VulkanAddTexture(
      CanvasRenderer canvasRenderer, ImGuiBackendVulkanImpl vulkan, VkSampler sampler,
      VkImageView imageView, VkImageLayout imageLayout, GaugeCanvas canvas)
    {
        if (canvas is not GaugeCanvasEx { HasPost: true })
            canvasRenderer.ImguiTextureID = vulkan.AddTexture(sampler, imageView, imageLayout);
    }

    private static void VulkanRemoveTexture(
      ImGuiBackendVulkanImpl vulkan, ImTextureRef texture, GaugeCanvas canvas)
    {
        if (canvas is not GaugeCanvasEx { HasPost: true })
            vulkan.RemoveTexture(texture);
    }

    private static void TranspileAddTexture(string name, CodeMatcher matcher)
    {
        matcher.MatchStartForward(
          CodeMatch.Calls(() => default(ImGuiBackendVulkanImpl).AddTexture(default, default)));
        matcher.ThrowIfInvalid($"could not find ImGuiVulkanBackendImpl.AddTexture call in {name}");

        matcher.RemoveInstruction(); // remove call
        matcher.RemoveInstruction(); // remove store
        matcher.InsertAndAdvance(
          new CodeInstruction(OpCodes.Ldarg_0),
          new CodeInstruction(OpCodes.Ldfld, CanvasRenderer_canvas),
          CodeInstruction.Call(() => VulkanAddTexture(default, default, default, default, default, default)));
    }

    [HarmonyPatch(typeof(GaugeCanvas), nameof(GaugeCanvas.PrepareCanvas)), HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> GaugeCanvas_PrepareCanvas_Transpile(
      IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);

        matcher.MatchStartForward(CodeMatch.Calls(() => default(GaugeCanvas).CacheTransform));
        matcher.ThrowIfInvalid("could not find GaugeCanvas.CacheTransform call in GaugeCanvas.PrepareCanvas");

        matcher.InsertAndAdvance(
          new CodeInstruction(OpCodes.Ldarg_1),
          CodeInstruction.Call(() => GaugeCanvas_CreatePost(default, default))
        );

        return matcher.Instructions();
    }

    private static GaugeCanvas GaugeCanvas_CreatePost(GaugeCanvas canvas, RendererContext context)
    {
        if (canvas is GaugeCanvasEx canvasEx && canvasEx.HasPost)
            canvasEx.PostRenderer = new(canvasEx, context, context, [canvasEx.Vertex.Get(), canvasEx.Fragment.Get()]);
        return canvas;
    }

    [HarmonyPatch(typeof(GaugeCanvas), nameof(GaugeCanvas.CacheTransform)), HarmonyPostfix]
    internal static void GaugeCanvas_CacheTransform_Postfix(GaugeCanvas __instance)
    {
        if (__instance is GaugeCanvasEx canvasEx && canvasEx.HasPost)
            canvasEx.PostRenderer.Resize(canvasEx.RenderResolution);
    }

    [HarmonyPatch(typeof(GaugeCanvas), nameof(GaugeCanvas.Render)), HarmonyPostfix]
    internal static void GaugeCanvas_Render_Postfix(
      GaugeCanvas __instance, CommandBuffer commandBuffer, Viewport activeViewport)
    {
        if (__instance is GaugeCanvasEx canvasEx && canvasEx.HasPost)
            canvasEx.PostRenderer.Render(commandBuffer, activeViewport);
    }

    [HarmonyPatch(typeof(Program), "RenderGame"), HarmonyTranspiler, HarmonyDebug]
    internal static IEnumerable<CodeInstruction> Program_RenderGame_Transpile(
      IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);

        matcher.MatchStartForward(
          CodeMatch.Calls(() => default(ImGuiBackendVulkanImpl).RenderDrawData(default)));
        matcher.ThrowIfInvalid("could not find ImGuiBackendVulkanImpl.RenderDrawData call in Program.RenderGame");

        matcher.MatchStartBackwards(
          CodeMatch.Calls(() => default(CommandBuffer).BeginRenderPass(default, default)));
        matcher.ThrowIfInvalid("could not find CommandBuffer.BeginRenderPass call in Program.RenderGame");

        matcher.RemoveInstruction();
        matcher.InsertAndAdvance(CodeInstruction.Call(() => ImGuiPreRender(default, default, default)));

        matcher.End();

        MethodInfo EndRenderPassMethod =
            AccessTools.Method(
                typeof(Brutal.VulkanApi.VkDeviceExtensions),
                "EndRenderPass"
            );

        matcher.MatchStartBackwards(
            new CodeMatch(
                instr => instr.opcode == OpCodes.Call &&
                         instr.operand is MethodInfo mi &&
                         mi.Name == "EndRenderPass"
            )
        );
        matcher.ThrowIfInvalid("EndRenderPass call not found");

        matcher.Advance(1);
        matcher.InsertAndAdvance(
            new CodeInstruction(OpCodes.Ldloc_1), // commandBuffer2
            new CodeInstruction(OpCodes.Ldloc_0), // frameResources
            new CodeInstruction(OpCodes.Ldc_I4_0),
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(GlobalPostShaderHandler), nameof(GlobalPostShaderHandler.RenderNow)))
        );

        return matcher.Instructions();
    }

    private static bool rebuilt = false;
    private static unsafe void ImGuiPreRender(
      CommandBuffer commandBuffer, in VkRenderPassBeginInfo beginInfo, VkSubpassContents contents)
    {
        Renderer renderer = Program.GetRenderer();

        if (!rebuilt)
        {
            Program.ScheduleRendererRebuild();
            rebuilt = true;
        }

        if (GlobalPostShaderHandler.offscreenTarget2 == null)
        {
            GlobalPostShaderHandler.offscreenTarget2 = new OffscreenTarget(
                renderer,
                renderer.Extent,
                renderer.ColorFormat,
                renderer.DepthFormat
            );
            GlobalPostShaderHandler.offscreenTarget2.BuildFramebuffer(Program.MainPass.Pass);
        }

        ImGuiRenderers.Render(Program.GetRenderer(), commandBuffer);

        PostProcessingHandler.RenderNow(commandBuffer);

        VkRenderPassBeginInfo beginInfo2 = new VkRenderPassBeginInfo();
        beginInfo2.RenderPass = Program.MainPass.Pass;
        beginInfo2.Framebuffer = GlobalPostShaderHandler.offscreenTarget2.FrameBuffer;
        beginInfo2.RenderArea = new VkRect2D(renderer.Extent);
        beginInfo2.ClearValues = (VkClearValue*)Program.MainPass.ClearValues.Ptr;
        beginInfo2.ClearValueCount = 2;

        commandBuffer.BeginRenderPass(in beginInfo2, contents);
    }

    [HarmonyPatch(typeof(Program), nameof(Program.RebuildRenderer)), HarmonyPostfix]
    internal static void Program_RebuildRenderer_Postfix()
    {
        ImGuiRenderers.RebuildAll();
        GlobalPostShaderHandler.Rebuild();
        PostProcessingHandler.Rebuild();
    }

    [HarmonyPatch(typeof(ModLibrary), nameof(ModLibrary.PrepareAll)), HarmonyPrefix]
    internal static void ModLibrary_PrepareAll_Prefix()
    {
        // PrepareManifest is called by StarMap, so we shouldn't need to do this here, but try anyways
        if (!ModLibrary.PrepareManifest())
            return;
        XmlPatcher.OnPrepare();
    }
}

[HarmonyPatch]
internal static class GaugeRendererPatch
{
    [HarmonyTargetMethod]
    internal static MethodBase TargetMethod() =>
      typeof(GaugeRenderer).GetConstructor([
        typeof(GaugeCanvas), typeof(GaugeComponent), typeof(RendererContext), typeof(Span<ShaderReference>)
      ]);

    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> GaugeRenderer_Ctor_Tranpsile(
      IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);

        Span<CodeInstruction> extraArgs = [
          new CodeInstruction(OpCodes.Ldarg_2) // add GaugeComponent arg
        ];
        var fr = new MatcherFindReplace(matcher, "GaugeRenderer.ctor", extraArgs);

        fr.FindReplace(
          typeof(DescriptorPoolExExtensions).GetMethod(nameof(DescriptorPoolExExtensions.CreateDescriptorPool)),
          typeof(GaugeRendererPatch).GetMethod(nameof(CreateDescriptorPool))
        );

        fr.FindReplace(
          typeof(DescriptorSetLayoutExExtensions).GetMethod(
            nameof(DescriptorSetLayoutExExtensions.CreateDescriptorSetLayout)),
          typeof(GaugeRendererPatch).GetMethod(nameof(CreateDescriptorSetLayout))
        );

        fr.FindReplace(
          typeof(VkDeviceExtensions).GetMethod(nameof(VkDeviceExtensions.UpdateDescriptorSets)),
          typeof(GaugeRendererPatch).GetMethod(nameof(UpdateDescriptorSets))
        );

        return matcher.Instructions();
    }

    public static DescriptorPoolEx CreateDescriptorPool(
      Device device, DescriptorPoolEx.CreateInfo createInfo, VkAllocator allocator, GaugeComponent component
    ) => ShaderEx.CreateDescriptorPool(component.FragmentShader, device, createInfo, allocator);

    public static DescriptorSetLayoutEx CreateDescriptorSetLayout(
      Device device, DescriptorSetLayoutEx.CreateInfo createInfo, VkAllocator allocator, GaugeComponent component
    ) => ShaderEx.CreateDescriptorSetLayout(component.FragmentShader, device, createInfo, allocator);

    public static void UpdateDescriptorSets(
      Device device,
      ReadOnlySpan<VkWriteDescriptorSet> pDescriptorWrites,
      ReadOnlySpan<VkCopyDescriptorSet> pDescriptorCopies,
      GaugeComponent component
    ) => ShaderEx.UpdateDescriptorSets(component.FragmentShader, device, pDescriptorWrites);
}

[HarmonyPatch]
internal static class XmlLoaderPatch
{
    [HarmonyTargetMethods]
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        yield return typeof(Mod).GetMethod("LoadAssetBundles");
        yield return typeof(Mod).GetMethod("LoadPlanetMeshes");
        yield return typeof(Mod).GetMethod("LoadSystems");
        yield return typeof(Mod).GetMethod("PrepareSystems");
    }

    private static readonly MethodInfo SourceMethod = typeof(XmlLoader).GetMethod(nameof(XmlLoader.Load)) ??
      throw new InvalidOperationException($"Could not find XmlLoader source method");

    private static readonly MethodInfo ReplacementMethod = typeof(XmlPatcher).GetMethod(nameof(XmlPatcher.Load)) ??
      throw new InvalidOperationException("Could not find XmlPatcher replacement method");

    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);

        matcher.Start();
        while (matcher.IsValid)
        {
            var inst = matcher.Instruction;
            if (IsDeserializeCall(inst, out var type))
                inst.operand = ReplacementMethod.MakeGenericMethod(type);

            matcher.Advance();
        }

        return matcher.Instructions();
    }

    private static bool IsDeserializeCall(CodeInstruction inst, out Type type)
    {
        type = default;
        if (inst.operand is not MethodInfo method)
            return false;
        if (!method.IsConstructedGenericMethod)
            return false;
        if (method.GetGenericMethodDefinition() != SourceMethod)
            return false;
        type = method.GetGenericArguments()[0];
        return true;
    }
}

public readonly ref struct MatcherFindReplace(
  CodeMatcher matcher, string name, Span<CodeInstruction> extraArgs = default)
{
    private readonly CodeMatcher matcher = matcher;
    private readonly string name = name;
    private readonly Span<CodeInstruction> extraArgs = extraArgs;

    public void FindReplace(MethodInfo from, MethodInfo to)
    {
        matcher.Start();
        matcher.MatchStartForward(CodeMatch.Calls(from));
        matcher.ThrowIfInvalid($"could not find call to {from} in {name}");

        // replace call
        matcher.Instruction.operand = to;

        // insert extra args before
        foreach (var arg in extraArgs)
            matcher.InsertAndAdvance(new CodeInstruction(arg));
    }
}