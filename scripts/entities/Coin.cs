using Godot;

public partial class Coin : PickupItem
{
	private static AudioStream _coinSound;

	protected override string LightName => "CoinGlow";
	protected override Color LightColor => new Color(1.0f, 0.85f, 0.25f, 1.0f);
	protected override Color GlowInnerColor => new Color(1.0f, 0.96f, 0.65f, 1.0f);
	protected override Color GlowMidColor => new Color(1.0f, 0.78f, 0.20f, 0.55f);
	protected override string VisualNodeName => "AnimatedSprite2D";

	protected override AudioStream GetPickupSound()
	{
		return _coinSound ??= GD.Load<AudioStream>("res://assets/sounds/coin_collecting.wav");
	}

	protected override void ApplyPickupEffect(Player player)
	{
		player.CoinCount++;
	}
}
