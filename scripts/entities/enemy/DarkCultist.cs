using Godot;

public partial class DarkCultist : Enemy
{
	protected override Color WarningLabelColor => new Color(0.45f, 1.0f, 0.35f);

	private PointLight2D _cultistLight;

	public override void _Ready()
	{
		base._Ready();
		_cultistLight = SetupGlowLight(
			"CultistLight",
			new Vector2(0, -8),
			new Color(0.45f, 0.95f, 0.42f, 1.0f),
			0.72f,
			new Color(0.65f, 1.0f, 0.55f, 1.0f),
			new Color(0.3f, 0.85f, 0.35f, 0.45f),
			0.45f,
			78);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (StepEnemyActionState(delta))
		{
			RunRangedKitingAI(delta, 145.0f, 135.0f / 145.0f, 90.0f, 14.0f, 0.7f, 0.6f);
		}

		MoveAndSlide();
	}
}
