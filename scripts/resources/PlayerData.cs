using Godot;

[GlobalClass]
public partial class PlayerData : Resource
{
	[Export] public string CharacterName = "Новый герой";
	
	[ExportGroup("Base Stats")]
	[Export] public int MaxHealth = 100;
	[Export] public float HealthRegen = 0.0f;
	[Export] public int MaxMana = 100;
	[Export] public float ManaRegen = 0.0f;
	[Export] public float WalkSpeed = 70.0f;
	[Export] public float RunMultiplier = 1.8f;

	[ExportGroup("Resistances")]
	[Export] public float MeleeResistance = 0.0f; // к мечам, рукам и тд
	[Export] public float RangedResistance = 0.0f; // к пулям, лукам, лазерам
	[Export] public float EffectResistance = 0.0f; // к стану, оцепенению и тд
	
	// Список карточек оружия, которые этот герой получит автоматически на старте!
	[Export] public Godot.Collections.Array<WeaponData> StartingWeapons;
	
	// Визуал
	[Export] public SpriteFrames CharacterAnimations;
}
