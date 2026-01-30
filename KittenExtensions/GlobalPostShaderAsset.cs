using KSA;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;

namespace KittenExtensions;

[KxAsset("GlobalPostShader")]
public class GlobalPostShaderAsset : ShaderEx
{
    [XmlAttribute]
    public int RenderPassId = 0;

    [XmlAttribute]
    public int SubpassId = 0;

    [XmlAttribute]
    public bool RequiresUniqueRenderpass = false;

    [XmlAttribute]
    public string VertexShaderID = "ScreenspaceVert";

    public static List<GlobalPostShaderAsset> AllShaders = new();
    public static SortedDictionary<int, SortedDictionary<int, List<GlobalPostShaderAsset>>> ShadersByPassAndSubpass = new();

    public ShaderReference VertexShader => ModLibrary.Get<ShaderReference>(VertexShaderID);

    public override void OnDataLoad(Mod mod)
    {
        base.OnDataLoad(mod);
        AllShaders.Add(this);

        if (!ShadersByPassAndSubpass.TryGetValue(RenderPassId, out SortedDictionary<int, List<GlobalPostShaderAsset>> value1))
        {
            value1 = new();
            ShadersByPassAndSubpass[RenderPassId] = value1;
        }
        if (!value1.TryGetValue(SubpassId, out List<GlobalPostShaderAsset> value))
        {
            value = new();
            value1[SubpassId] = value;
        }

        value.Add(this);
    }
}