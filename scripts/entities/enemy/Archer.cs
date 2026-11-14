using Godot;

public partial class Archer : Enemy
{
	protected override Color WarningLabelColor => new Color(1.0f, 0.5f, 0.1f);

	public override void _PhysicsProcess(double delta)
	{
		if (StepEnemyActionState(delta))
		{
			RunRangedKitingAI(delta, 120.0f, 1.0f, 85.0f, 12.0f, 0.65f, 0.55f);
		}

		MoveAndSlide();
	}
}
