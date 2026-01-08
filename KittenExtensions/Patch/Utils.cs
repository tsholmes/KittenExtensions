
using System;
using System.Collections;
using System.Xml;
using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using RenderCore;

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

  public void AddNodePath(XmlNode node, XmlNode relTo = null) => AddNodePathInternal(node, relTo);

  private bool AddNodePathInternal(XmlNode node, XmlNode relTo)
  {
    if (node == null || node == relTo)
      return false;
    if (node.ParentNode != null && AddNodePathInternal(node.ParentNode, relTo))
      Add('/');
    switch (node)
    {
      case XmlElement el:
        Add(el.Name);
        if (el.GetAttributeNode("Id") is XmlAttribute idAttr)
        {
          Add("[@Id=\"");
          Add(idAttr.Value);
          Add("\"]");
        }
        break;
      case XmlText:
        Add("text()");
        break;
      case XmlProcessingInstruction proc:
        Add("processing-instruction(\"");
        Add(proc.Name);
        Add("\")");
        break;
      case XmlAttribute attr:
        Add('@');
        Add(attr.Name);
        break;
      default:
        break;
    }
    return true;
  }

  public void AddXPathVal(object obj)
  {
    switch (obj)
    {
      case string srcString:
        AddQuotedFirstLine(srcString);
        break;
      case bool srcBool:
        Add(srcBool ? "true" : "false");
        break;
      case double srcDouble:
        Add(srcDouble, "f");
        break;
      case IList srcList:
        if (srcList.Count == 1)
        {
          switch (srcList[0])
          {
            case string elStr:
              AddQuotedFirstLine(elStr);
              break;
            case XmlNode elNode:
              NodeOneLine(elNode);
              break;
          }
        }
        else
        {
          for (var i = 0; i < 3 && i < srcList.Count; i++)
          {
            switch (srcList[i])
            {
              case string elStr:
                Add("\n  ");
                AddQuotedFirstLine(elStr);
                break;
              case XmlNode elNode:
                Add("\n  ");
                NodeOneLine(elNode);
                break;
            }
          }
          if (srcList.Count > 3)
            Add("\n  ...");
        }
        break;
    }
  }

  public void NodeOneLine(XmlNode node)
  {
    switch (node)
    {
      case XmlElement el:
        ElementOpen(el, !el.HasChildNodes);
        break;
      case XmlAttribute attr:
        Add('@');
        Add(attr.Name);
        Add('=');
        AddQuotedFirstLine(attr.Value);
        break;
      case XmlComment comment:
        Add("<!-- ");
        AddFirstLine(comment.Value);
        Add(" -->");
        break;
      case XmlCharacterData text:
        AddQuotedFirstLine(text.Value);
        break;
      case XmlProcessingInstruction proc:
        Add("<?");
        Add(proc.Name);
        Add(' ');
        AddFirstLine(proc.Value);
        Add("?>");
        break;
      default:
        break;
    }
  }

  public void NodeInline(XmlNode node)
  {
    if (node is XmlElement el)
      ElementInline(el);
    else if (node is XmlText text)
      TextInline(text);
    else if (node is XmlComment comment)
      CommentInline(comment);
    else if (node is XmlProcessingInstruction proc)
      ProcInline(proc);
    else
      Add("???");
  }

  public void ElementInline(XmlElement el)
  {
    ElOpenStart(el.Name);
    ElAttrsInline(el.Attributes);

    if (!el.HasChildNodes)
    {
      ElOpenEnd(true);
      return;
    }

    ElOpenEnd();

    var children = el.ChildNodes;
    for (var i = 0; i < children.Count; i++)
      NodeInline(children[i]);

    ElementClose(el.Name);
  }

  public void TextInline(XmlText text)
  {
    Add(text.Value);
  }

  public void CommentInline(XmlComment comment)
  {
    Add("<!--");
    Add(comment.Value);
    Add("-->");
  }

  public void ProcInline(XmlProcessingInstruction proc)
  {
    Add("<?");
    Add(proc.Name);
    Add(' ');
    Add(proc.Value);
    Add("?>");
  }

  public void ElementOpen(XmlElement el, bool selfClose = false)
  {
    ElOpenStart(el.Name);
    ElAttrsInline(el.Attributes);
    ElOpenEnd(selfClose);
  }

  public void ElementClose(ReadOnlySpan<char> name)
  {
    Add("</");
    Add(name);
    Add('>');
  }

  public void ElOpenStart(ReadOnlySpan<char> name)
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

  private void ElAttrsInline(XmlAttributeCollection attrs)
  {
    for (var i = 0; i < attrs.Count; i++)
    {
      var attr = attrs[i];
      Add(' ');
      ElAttr(attr.Name, attr.Value);
    }
  }

  public void ElAttr(ReadOnlySpan<char> name, ReadOnlySpan<char> value)
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
        ImGui.RenderPlatformWindowsDefault(IntPtr.Zero, IntPtr.Zero);
      }
      (FrameResult result, AcquiredFrame acquiredFrame1) = renderer.TryAcquireNextFrame();
      AcquiredFrame acquiredFrame2 = acquiredFrame1;
      if (result != FrameResult.Success)
        PartialRebuild();
      else
      {
        acquiredFrame1 = acquiredFrame2;
        (FrameResources resources, CommandBuffer commandBuffer) = acquiredFrame1;
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