using System;
namespace EnlightenMAUI.Models;

public class XAxisOption
{
    public string name { get; set; }
    public string unit { get; set; }

    public XAxisOption() { }

    public string displayName { get => $"{name} ({unit})"; }

    public override string ToString() => displayName;
}
