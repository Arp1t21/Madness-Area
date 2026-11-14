using Godot;

public partial class ManaCrystal : PickupItem
{
	private static AudioStream _manaSound;

	protected override string LightName => "ManaGlow";
	protected override Color LightColor => new Color(0.32f, 0.82f, 1.0f, 1.0f);
	protected override Color GlowInnerColor => new Color(0.85f, 0.98f, 1.0f, 1.0f);
	protected override Color GlowMidColor => new Color(0.28f, 0.75f, 1.0f, 0.55f);
	protected override string VisualNodeName => "Sprite2D";

	protected override AudioStream GetPickupSound()
	{
		return _manaSound ??= GD.Load<AudioStream>("res://assets/sounds/buff_collecting.wav");
	}

	protected override void ApplyPickupEffect(Player player)
	{
		if (NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession)
		{
			if (NetworkManager.Instance.IsServerAuthority)
			{
				player?.Rpc(nameof(Player.RpcRestoreMana), 8.0f);
			}
			return;
		}
		float maxMana = player.MaxManaEffective;
		player.CurrentMana = Mathf.Min(maxMana, player.CurrentMana + 8.0f);
	}
}
