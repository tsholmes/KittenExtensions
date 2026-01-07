
using System.Collections.Generic;
using System.Xml;
using System.Xml.XPath;

namespace KittenExtensions.Patch;

public abstract class OpExecContext(XPathNavigator Nav)
{
  public readonly XPathNavigator Nav = Nav;

  public abstract OpExecContext WithNav(XPathNavigator nav);
  public abstract OpExecution Execution(XmlOp op);
  public abstract OpAction Action(
    OpActionType Type, XmlNode Target, object Source = null, OpPosition Pos = OpPosition.Default);

  public abstract void End();
}

public class DefaultOpExecContext(XPathNavigator Nav) : OpExecContext(Nav)
{
  private XmlOp op = null;

  public override OpExecContext WithNav(XPathNavigator nav) =>
    new DefaultOpExecContext(nav);

  public override OpExecution Execution(XmlOp op) => new(op, new DefaultOpExecContext(Nav) { op = op });
  public override OpAction Action(
    OpActionType Type, XmlNode Target, object Source = null, OpPosition Pos = OpPosition.Default
  ) => new(op, this, Type, Target, Source, Pos);

  public override void End() { }
}

public class DebugOpExecContext : OpExecContext
{
  public readonly DebugOpExecContext Parent;
  private OpExecution exec;
  private OpAction action;
  public readonly List<DebugOpExecContext> Children = [];

  public OpExecution ContextExec => exec;
  public OpAction ContextAction => action;

  public bool Ended { get; set; } = false;

  public DebugOpExecContext(XPathNavigator nav) : base(nav)
  {
    Parent = null;
    exec = null;
    action = null;
  }

  private DebugOpExecContext(DebugOpExecContext parent, XPathNavigator nav) : base(nav)
  {
    Parent = parent;
    exec = parent.exec;
    action = null;
  }

  public override OpExecContext WithNav(XPathNavigator nav)
  {
    var child = new DebugOpExecContext(this, nav);
    Children.Add(child);
    return child;
  }

  public override OpExecution Execution(XmlOp op)
  {
    var child = new DebugOpExecContext(this, Nav);
    child.exec = new(op, child);
    Children.Add(child);
    return child.exec;
  }

  public override OpAction Action(
    OpActionType Type, XmlNode Target,
    object Source = null, OpPosition Pos = OpPosition.Default)
  {
    var child = new DebugOpExecContext(this, Nav);
    child.action = new(child.exec.Op, child, Type, Target, Source, Pos);
    Children.Add(child);
    return child.action;
  }

  public override void End() => Ended = true;
}