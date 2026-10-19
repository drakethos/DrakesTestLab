namespace DrakesTestLab.UI.Toolbox;

internal abstract class ToolboxTab
{
    public abstract string Title { get; }

    /// <summary>Optional suffix shown on the tab button, e.g. an error count.</summary>
    public virtual string Badge => "";

    public virtual void Draw()
    {
    }

    /// <summary>Called every frame (even with the window closed) so toggles like infinite stamina keep working.</summary>
    public virtual void Tick()
    {
    }
}
