
using System.Xml.Serialization;
using KSA;

namespace KittenExtensions;

[KxAsset("GaugeCanvasEx")]
public class GaugeCanvasEx : GaugeCanvas
{
  [XmlElement("PostVertex")]
  public ShaderReference Vertex;
  [XmlElement("PostFragment")]
  public ShaderEx Fragment;

  public bool HasPost => Vertex != null && Fragment != null;

  [XmlIgnore]
  public PostProcessRenderer PostRenderer { get; set; }

  public override void OnDataLoad(Mod mod)
  {
    base.OnDataLoad(mod);
    if (HasPost)
    {
      Vertex.OnDataLoad(mod);
      Fragment.OnDataLoad(mod);
    }
  }
}