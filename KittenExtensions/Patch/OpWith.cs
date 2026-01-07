
using System.Collections.Generic;

namespace KittenExtensions.Patch;

public class XmlWithOp : XmlOpCollection
{
  public override IEnumerable<OpExecution> Execute(OpExecContext ctx)
  {
    var childCtxs = new List<OpExecContext>();
    foreach (var match in ctx.Nav.Select(Path).ToNavList())
      childCtxs.Add(ctx.WithNav(match));

    foreach (var cctx in childCtxs)
    {
      foreach (var ex in base.Execute(cctx))
        yield return ex;
      cctx.End();
    }
  }
}