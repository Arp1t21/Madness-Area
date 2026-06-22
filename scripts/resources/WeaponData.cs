using Godot;

[GlobalClass]
public partial class WeaponData : Resource
{
	[Export] public string WeaponName = "Новое оружие";
	[Export] public int Damage = 10;
	[Export] public float AttackCooldown = 0.5f; 
	[Export] public float AttackRange = 100.0f;
	[Export] public float KnockbackForce = 15.0f;
	[Export] public float Recoil = 0.0f;
	[Export] public float ManaCost = 0.0f;
	[Export] public bool AllowMultiHit = false; // Можно ли ударить одного и того же врага дважды за один взмах
	[Export] public bool IsAutoAim = false; // Автоприцеливание на ближайшую цель
	[Export] public float OrientationOffset = 0.0f; // Смещение угла (в градусах)
	[Export] public float FlipOrientationOffset = 0.0f; // Смещение угла при взгляде влево

	[ExportGroup("Visuals")]
	[Export] public PackedScene WeaponVisualPrefab; // Сцена визуала взмаха (attack_ability.tscn)

	[ExportGroup("Ranged Only")]
	[Export] public bool IsRanged = false; 
	[Export] public PackedScene BulletPrefab; 
	[Export] public float BulletSpeed = 300.0f;
	[Export] public int BulletCount = 1; 
	[Export] public float Spread = 0.0f; 
	[Export] public bool IsPiercing = false; // Если true, пуля пробивает врагов насквозь
	[Export] public int MaxAmmo = 10;
	[Export] public float ReloadTime = 1.5f;
}
