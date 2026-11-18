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

	[ExportGroup("Sounds")]
	[Export] public AudioStream AttackSound;
	[Export] public AudioStream ReloadSound;
	[Export] public AudioStream EquipSound;
	[Export] public AudioStream EmptySound;

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

	[ExportGroup("Laser Only")]
	[Export] public bool IsContinuousLaser = false;
	[Export] public float SplashRadius = 26.0f;
	[Export] public Color LaserColor = new Color(0.24f, 0.92f, 1.0f, 1.0f);

	// Путь к исходному .tres ресурсу при создании уникальной копии оружия для прокачки одного слота
	public string OriginalResourcePath { get; set; } = "";

	// Список применённых одиночных прокачек (порядок покупок) для пересчёта поверх свежих базовых статов .tres при загрузке
	public System.Collections.Generic.List<string> AppliedUpgrades { get; set; } = new System.Collections.Generic.List<string>();

	public string GetEffectiveResourcePath()
	{
		return !string.IsNullOrEmpty(ResourcePath) ? ResourcePath : OriginalResourcePath;
	}

	/// <summary>
	/// Creates a standalone WeaponData clone in managed C# without invoking C++ Resource.Duplicate()
	/// across the C++/Mono script boundary.
	/// </summary>
	public WeaponData CreateUniqueCopy()
	{
		string origPath = GetEffectiveResourcePath();
		var copy = new WeaponData
		{
			ResourceName = ResourceName,
			WeaponName = WeaponName,
			Damage = Damage,
			AttackCooldown = AttackCooldown,
			AttackRange = AttackRange,
			KnockbackForce = KnockbackForce,
			Recoil = Recoil,
			ManaCost = ManaCost,
			AllowMultiHit = AllowMultiHit,
			IsAutoAim = IsAutoAim,
			OrientationOffset = OrientationOffset,
			FlipOrientationOffset = FlipOrientationOffset,
			AttackSound = AttackSound,
			ReloadSound = ReloadSound,
			EquipSound = EquipSound,
			EmptySound = EmptySound,
			WeaponVisualPrefab = WeaponVisualPrefab,
			IsRanged = IsRanged,
			BulletPrefab = BulletPrefab,
			BulletSpeed = BulletSpeed,
			BulletCount = BulletCount,
			Spread = Spread,
			IsPiercing = IsPiercing,
			MaxAmmo = MaxAmmo,
			ReloadTime = ReloadTime,
			IsContinuousLaser = IsContinuousLaser,
			SplashRadius = SplashRadius,
			LaserColor = LaserColor,
			OriginalResourcePath = origPath,
			AppliedUpgrades = AppliedUpgrades != null
				? new System.Collections.Generic.List<string>(AppliedUpgrades)
				: new System.Collections.Generic.List<string>()
		};
		return copy;
	}
}
