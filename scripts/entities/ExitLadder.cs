using Godot;

public partial class ExitLadder : Area2D, IInteractable
{
	private Label _promptLabel;
	private Level _level;

	public void Setup(Level level)
	{
		_level = level;
		Name = "ExitLadder";
		CollisionLayer = 0;
		CollisionMask = 16; // Слой 5 коллизий (для детекта Area2DPickWeapon игрока)

		var collision = new CollisionShape2D();
		var circle = new CircleShape2D();
		circle.Radius = 15.0f;
		collision.Shape = circle;
		AddChild(collision);

		_promptLabel = new Label();
		_promptLabel.Text = "Go deeper [E]";
		_promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_promptLabel.Position = new Vector2(-40, -25);
		_promptLabel.Size = new Vector2(80, 15);
		
		var font = GD.Load<Font>("res://assets/fonts/monogram/monogram.ttf");
		if (font != null) {
			_promptLabel.AddThemeFontOverride("font", font);
			_promptLabel.AddThemeFontSizeOverride("font_size", 8);
		}
		_promptLabel.Visible = false;
		AddChild(_promptLabel);

		AreaEntered += OnAreaEntered;
		AreaExited += OnAreaExited;
	}

	public void Interact(Player player)
	{
		player.UnregisterInteractable(this);
		_level.DescendStairs();
	}

	public Vector2 GetInteractionPosition()
	{
		return GlobalPosition;
	}

	public void ShowPrompt(bool show)
	{
		if (_promptLabel != null) {
			_promptLabel.Visible = show;
		}
	}

	private void OnAreaEntered(Area2D area)
	{
		if (area.GetParent() is Player player) {
			player.RegisterInteractable(this);
		}
	}

	private void OnAreaExited(Area2D area)
	{
		if (area.GetParent() is Player player) {
			player.UnregisterInteractable(this);
		}
	}
}
