using Godot;

[GlobalClass]
public partial class EnemyData : Resource
{
	[Export] public string EnemyName = "Гоблин";
	
	[ExportGroup("Base Stats")]
	[Export] public float Speed = 50.0f;
	[Export] public int MaxHealth = 30;
	[Export] public int Damage = 5;
	[Export] public float AttackRange = 35.0f; // Дистанция остановки перед ударом
	[Export] public float PreAttackDelay = 1.0f; // Сколько стоит и "тупит" ПЕРЕД ударом
	[Export] public float StopDuration = 1.0f; // На сколько замирает ПРИ атаке (общая пауза)

	[ExportGroup("Resistances")]
	[Export] public float MeleeResistance = 0.0f; // к мечам, рукам и тд
	[Export] public float RangedResistance = 0.0f; // к пулям, лукам, лазерам
	[Export] public float EffectResistance = 0.0f; // к стану, оцепенению и тд
	
	[ExportGroup("Rewards")]
	[Export(PropertyHint.Range, "0,1")] public float CoinDropChance = 0.2f;
	[Export(PropertyHint.Range, "0,1")] public float ManaCrystalDropChance = 0.1f;

	[ExportGroup("Visuals")]
	[Export] public Color DamageNumberColor = new Color(1, 1, 0); // Желтый
	[Export] public Color FlashColor = new Color(1, 0, 0); // Красный

	[ExportGroup("Archetype")]
	[Export] public bool IsRanged = false;
}
