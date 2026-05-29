using Godot;

[Tool]
[GlobalClass]
public partial class GolfTeeBoxDefinition : Resource
{
    [Export] public string TeeColor { get; set; } = "Black";
    [Export] public Vector2 Position { get; set; } = Vector2.Zero;

    public GolfTeeBoxDefinition DuplicateBox()
    {
        return new GolfTeeBoxDefinition
        {
            TeeColor = TeeColor,
            Position = Position
        };
    }
}

