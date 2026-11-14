using Godot;

public partial class ExitLadder : Area2D, IInteractable
{
	private Label _promptLabel;
	private Level _level;

	public void Setup(Level level)
	{
		_level = level;
		Name = "ExitLadder";
		Scale = new Vector2(3.0f, 3.0f);
		CollisionLayer = 0;
		CollisionMask = 16; // Слой 5 коллизий (для детекта Area2DPickWeapon игрока)

		var collision = new CollisionShape2D();
		var circle = new CircleShape2D();
		circle.Radius = 15.0f;
		collision.Shape = circle;
		AddChild(collision);

		var aura = DungeonVisuals.CreateRuneAura(15.0f, new Color(0.32f, 0.96f, 0.65f, 0.78f), 6, -1);
		AddChild(aura);

		var ladderGlowTex = GameResourceCache.GetRadialGlowTexture(
			new Color(0.45f, 1.0f, 0.72f, 0.9f),
			Colors.Transparent,
			1.0f,
			64,
			new Vector2(0.85f, 0.85f));
		GameResourceCache.CreateGlowLight(
			this,
			"ExitGlow",
			Vector2.Zero,
			new Color(0.38f, 0.96f, 0.66f, 0.95f),
			0.82f,
			ladderGlowTex,
			shadowEnabled: false);

		_promptLabel = new Label();
		_promptLabel.Text = "Go deeper [E]";
		_promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_promptLabel.Position = new Vector2(-40, -25);
		_promptLabel.Size = new Vector2(80, 15);
		_promptLabel.AddThemeColorOverride("font_color", new Color(0.55f, 1.0f, 0.72f));
		
		var font = GD.Load<Font>("res://assets/fonts/silver/Silver.ttf");
		if (font != null) {
			_promptLabel.AddThemeFontOverride("font", font);
			_promptLabel.AddThemeFontSizeOverride("font_size", 8);
		}
		_promptLabel.AddThemeConstantOverride("letter_spacing", 2);
		DungeonVisuals.ApplyPixelOutline(_promptLabel, 2);
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
