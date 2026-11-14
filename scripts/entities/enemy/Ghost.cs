using Godot;

public partial class Ghost : Enemy
{
	protected override Color WarningLabelColor => new Color(0.2f, 0.6f, 1.0f);

	private PointLight2D _ghostLight;
	private float _glowPulseTime = 0.0f;

	public override void _Ready()
	{
		base._Ready();
		if (_sprite != null)
		{
			_sprite.Material = GameResourceCache.GetUnshadedMaterial();
			_sprite.SelfModulate = new Color(1.25f, 1.38f, 1.55f, 1.0f);
		}
		_ghostLight = SetupGlowLight(
			"GhostSpectralLight",
			new Vector2(0, -6),
			new Color(0.55f, 0.92f, 1.0f, 1.0f),
			1.45f,
			new Color(0.95f, 0.99f, 1.0f, 1.0f),
			new Color(0.5f, 0.88f, 1.0f, 0.72f),
			0.50f,
			70);
	}

	protected override void UpdateEnemyClientVisualTick(double delta)
	{
		if (_ghostLight != null && GodotObject.IsInstanceValid(_ghostLight))
		{
			_glowPulseTime += (float)delta * 3.5f;
			_ghostLight.Energy = 1.40f + Mathf.Sin(_glowPulseTime) * 0.20f;
		}
	}

	protected override void TickEnemyAI(double delta)
	{
		if (StepEnemyActionState(delta))
		{
			RunRangedKitingAI(delta, 150.0f, 1.0f, 105.0f, 12.0f, 0.65f, 0.55f);
		}

		MoveAndSlide();
	}
}
