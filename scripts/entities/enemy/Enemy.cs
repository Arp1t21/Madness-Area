using Godot;

public partial class Enemy : CharacterBody2D
{
	[Export] public EnemyData Config;
	public float CurrentHealth;
	protected AnimatedSprite2D _sprite;

	public float DamageMultiplier { get; protected set; } = 1.0f;

	public override void _Ready()
	{
		Scale = new Vector2(3.0f, 3.0f);
		AddToGroup("enemy");
		_sprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
		
		var level = GetTree().GetFirstNodeInGroup("Level") as Level;
		int levelIndex = level != null ? level.CurrentLevelIndex : 1;

		float healthMultiplier = 1.0f + (levelIndex - 1) * 0.15f;
		DamageMultiplier = 1.0f + (levelIndex - 1) * 0.15f;

		if (Config != null) {
			CurrentHealth = Config.MaxHealth * healthMultiplier;
		}
	}

	// Метод для получения позиции игрока
	public Vector2 GetPlayerPosition() {
		var player = GetTree().GetFirstNodeInGroup("Player") as Node2D;
		return player != null ? player.GlobalPosition : GlobalPosition;
	}

	public virtual void TakeDamage(float amount, Vector2 knockbackSource, float knockbackForce = 15.0f, bool isMelee = true) {
		if (CurrentHealth <= 0) return;
		
		CurrentHealth -= amount;
		GD.Print($"{Config?.EnemyName} получил урон: {amount}. ХП: {CurrentHealth}");

		FlashRed();
		ApplyKnockback(knockbackSource, knockbackForce);
		SpawnDamageNumber(amount);

		if (CurrentHealth <= 0) {
			Die();
		}
	}

	protected void FlashRed() {
		Color flashColor = Config?.FlashColor ?? new Color(1, 0, 0);
		Modulate = flashColor;
		var timer = GetTree().CreateTimer(0.1f);
		timer.Timeout += () => Modulate = new Color(1, 1, 1);
	}

	protected void ApplyKnockback(Vector2 source, float force) {
		Vector2 dir = (GlobalPosition - source).Normalized();
		Velocity = dir * force * 30.0f;
		MoveAndSlide();
	}

	protected void SpawnDamageNumber(float amount) {
		var ft = new FloatingText();
		GetParent().AddChild(ft);
		float offsetX = (float)GD.RandRange(-15.0f, 15.0f) * 3f;
		float offsetY = (float)GD.RandRange(-5.0f, 5.0f) * 3f;
		ft.GlobalPosition = GlobalPosition + new Vector2(offsetX, -30.0f + offsetY);
		Color textColor = Config?.DamageNumberColor ?? new Color(1, 1, 0);
		ft.Init(((int)amount).ToString(), textColor);
	}

	protected virtual void Die() {
		GD.Print($"{Config?.EnemyName} погиб!");
		
		if (Config != null) {
			if (GD.Randf() <= Config.CoinDropChance) {
				CallDeferred("SpawnCoin");
			}
			if (GD.Randf() <= Config.ManaCrystalDropChance) {
				CallDeferred("SpawnManaCrystal");
			}
		}

		QueueFree();
	}

	protected void SpawnCoin() {
		var coinPrefab = GD.Load<PackedScene>("res://scene/objects/coin.tscn");
		if (coinPrefab != null) {
			var coin = coinPrefab.Instantiate() as Node2D;
			coin.GlobalPosition = GlobalPosition;
			GetParent().AddChild(coin);
		}
	}

	protected void SpawnManaCrystal() {
		var crystalPrefab = GD.Load<PackedScene>("res://scene/objects/mana_crystal.tscn");
		if (crystalPrefab != null) {
			var crystal = crystalPrefab.Instantiate() as Node2D;
			crystal.GlobalPosition = GlobalPosition;
			GetParent().AddChild(crystal);
		}
	}

	// Метод, который заставляет моба двигаться к цели
	public void MoveTowards(Vector2 target, double delta) {
		if (Config == null) return;
		Vector2 direction = GlobalPosition.DirectionTo(target);
		Velocity = direction * Config.Speed * 3f;
		MoveAndSlide();
	}
}
