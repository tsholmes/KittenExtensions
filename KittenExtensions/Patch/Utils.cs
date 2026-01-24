
using System;
using System.Collections;
// using System.Xml;
using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using RenderCore;
using XPP.Doc;

namespace KittenExtensions.Patch;

public ref struct LineBuilder(Span<char> buf)
{
  private readonly Span<char> buf = buf;
  private int length = 0;

  public int Length
  {
    get => length;
    set
    {
      if (value >= length)
        throw new IndexOutOfRangeException();
      length = value;
    }
  }

  public ReadOnlySpan<char> Line => buf[..length];

  public void Clear() => length = 0;

  public void Add(ReadOnlySpan<char> data)
  {
    data.CopyTo(buf[length..]);
    length += data.Length;
  }

  public void AddFirstLine(ReadOnlySpan<char> data)
  {
    var nl = data.IndexOf('\n');
    if (nl >= 0)
    {
      if (nl > 0 && data[nl] == '\r')
        nl--;
      data = data[..nl];
    }
    Add(data);
    if (nl >= 0)
      Add("...");
  }

  public void AddQuotedFirstLine(ReadOnlySpan<char> data)
  {
    Add('"');
    AddFirstLine(data);
    Add('"');
  }

  public void Add(char c)
  {
    buf[length++] = c;
  }

  public void Add<T>(T val, ReadOnlySpan<char> fmt = "g") where T : ISpanFormattable
  {
    val.TryFormat(buf[length..], out var len, fmt, null);
    length += len;
  }

  public void Add(XPName name)
  {
    if (!string.IsNullOrEmpty(name.Prefix))
    {
      Add(name.Prefix);
      Add(':');
    }
    Add(name.Local);
  }

  public void AddNodePath(XPNodeRef node, XPNodeRef relTo = default) =>
    AddNodePathInternal(node, relTo, true);

  private bool AddNodePathInternal(XPNodeRef node, XPNodeRef relTo, bool first = false)
  {
    if (!node.Valid)
      return false;
    if (node.SameAs(relTo))
    {
      if (first)
        Add('.');
      return first;
    }
    if (node.Parent.Valid && AddNodePathInternal(node.Parent, relTo))
      Add('/');
    switch (node.Type)
    {
      case XPType.Element:
        Add(node.Name);
        if (node.Attribute("Id") is XPNodeRef { Valid: true } idAttr)
        {
          Add("[@Id=\"");
          Add(idAttr.Value);
          Add("\"]");
        }
        break;
      case XPType.Text:
        Add("text()");
        break;
      case XPType.ProcInst:
        Add("processing-instruction(\"");
        Add(node.Name);
        Add("\")");
        break;
      case XPType.Attribute:
        Add('@');
        Add(node.Name);
        break;
      default:
        break;
    }
    return true;
  }

  public void NodeOneLine(XPNodeRef node)
  {
    switch (node.Type)
    {
      case XPType.Element:
        ElementOpen(node, !node.FirstContent.Valid);
        break;
      case XPType.Attribute:
        Add('@');
        Add(node.Name);
        Add('=');
        AddQuotedFirstLine(node.Value);
        break;
      case XPType.Comment:
        Add("<!-- ");
        AddFirstLine(node.Value);
        Add(" -->");
        break;
      case XPType.Text or XPType.CData:
        AddQuotedFirstLine(node.Value);
        break;
      case XPType.ProcInst:
        Add("<?");
        Add(node.Name);
        Add(' ');
        AddFirstLine(node.Value);
        Add("?>");
        break;
      default:
        break;
    }
  }

  public void NodeInline(XPNodeRef node)
  {
    if (node.Type is XPType.Element)
      ElementInline(node);
    else if (node.Type is XPType.Text)
      TextInline(node);
    else if (node.Type is XPType.Comment)
      CommentInline(node);
    else if (node.Type is XPType.ProcInst)
      ProcInline(node);
    else
      Add("???");
  }

  public void ElementInline(XPNodeRef el)
  {
    ElOpenStart(el.Name);
    ElAttrsInline(el);

    var child = el.FirstContent;
    if (!child.Valid)
    {
      ElOpenEnd(true);
      return;
    }

    ElOpenEnd();

    while (child.Valid)
    {
      NodeInline(child);
      child = child.NextSibling;
    }

    ElementClose(el.Name);
  }

  public void TextInline(XPNodeRef text)
  {
    Add(text.Value);
  }

  public void CommentInline(XPNodeRef comment)
  {
    Add("<!--");
    Add(comment.Value);
    Add("-->");
  }

  public void ProcInline(XPNodeRef proc)
  {
    Add("<?");
    Add(proc.Name);
    Add(' ');
    Add(proc.Value);
    Add("?>");
  }

  public void ElementOpen(XPNodeRef el, bool selfClose = false)
  {
    ElOpenStart(el.Name);
    ElAttrsInline(el);
    ElOpenEnd(selfClose);
  }

  public void ElementClose(XPName name)
  {
    Add("</");
    Add(name);
    Add('>');
  }

  public void ElOpenStart(XPName name)
  {
    Add('<');
    Add(name);
  }

  public void ElOpenEnd(bool selfClose = false)
  {
    if (selfClose)
      Add(" />");
    else
      Add('>');
  }

  private void ElAttrsInline(XPNodeRef parent)
  {
    var attr = parent.FirstAttr;
    while (attr.Valid)
    {
      Add(' ');
      ElAttr(attr.Name, attr.Value);
      attr = attr.NextSibling;
    }
  }

  public void ElAttr(XPName name, ReadOnlySpan<char> value)
  {
    Add(name);
    Add("=\"");
    Add(value);
    Add('"');
  }
}

// copied from SelectSystem
public class PopupTask(Popup popup) : SetupTaskBase
{
  private readonly Renderer renderer = Program.GetRenderer();
  private readonly Popup popup = popup;

  public bool Show => popup.Active;

  public void DrawUi()
  {
    if (!Show)
      return;
    ImGuiHelper.BlankBackground();
    Popup.DrawAll();
  }

  public unsafe void OnFrame()
  {
    if (!Program.IsMainThread())
      return;
    Glfw.PollEvents();
    if (Program.GetWindow().ShouldClose)
    {
      Environment.Exit(0);
    }
    else
    {
      ImGuiBackend.NewFrame();
      ImGui.NewFrame();
      ImGuiHelper.StartFrame();
      DrawUi();
      ImGui.Render();
      if (ImGui.GetIO().ConfigFlags.HasFlag(ImGuiConfigFlags.ViewportsEnable))
      {
        ImGui.UpdatePlatformWindows();
        ImGui.RenderPlatformWindowsDefault();
      }
      (FrameResult result, AcquiredFrame acquiredFrame) = renderer.TryAcquireNextFrame();
      if (result != FrameResult.Success)
        PartialRebuild();
      else
      {
        (FrameResources resources, CommandBuffer commandBuffer) = acquiredFrame;
        VkSubpassContents contents = VkSubpassContents.Inline;
        VkRenderPassBeginInfo pRenderPassBegin = new()
        {
          RenderPass = Program.MainPass.Pass,
          Framebuffer = resources.Framebuffer,
          RenderArea = new VkRect2D(renderer.Extent),
          ClearValues = (VkClearValue*)Program.MainPass.ClearValues.Ptr,
          ClearValueCount = 2
        };
        commandBuffer.Reset();
        commandBuffer.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
        commandBuffer.BeginRenderPass(in pRenderPassBegin, contents);
        ImGuiBackend.Vulkan.RenderDrawData(commandBuffer);
        commandBuffer.EndRenderPass();
        commandBuffer.End();
        if (renderer.TrySubmitFrame() == 0)
          return;
        PartialRebuild();
      }
    }
  }

  public void PartialRebuild()
  {
    renderer.Rebuild(GameSettings.GetPresentMode());
    renderer.Device.WaitIdle();
    Program.MainPass.Pass = renderer.MainRenderPass;
    Program.ScheduleRendererRebuild();
  }
}