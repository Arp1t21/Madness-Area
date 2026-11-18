using Godot;
using System;

/// <summary>
/// Светящийся факел подземелья без какой-либо физики касания (нет ни StaticBody2D, ни Area2D).
/// Освещает стены и пол вокруг себя мягким теплым пламенем.
/// </summary>
public partial class DungeonTorch : Node2D
{
	private PointLight2D _torchLight;
	private float _flickerTime;
	private float _baseEnergy = 0.76f;

	public override void _Ready()
	{
		Scale = Vector2.One;
		ZIndex = 1;
		_flickerTime = (float)GD.RandRange(0.0, 10.0);

		SetupLight();
		SetupEmbers();
	}

	private void SetupLight()
	{
		var gradTex = GameResourceCache.GetRadialGlowTexture(
			new Color(1.0f, 0.92f, 0.72f, 1.0f),
			new Color(1.0f, 0.68f, 0.28f, 0.55f),
			0.42f,
			112);

		_torchLight = GameResourceCache.CreateGlowLight(
			this,
			"TorchLight",
			new Vector2(0, -6),
			new Color(1.0f, 0.74f, 0.36f, 1.0f),
			_baseEnergy,
			gradTex,
			shadowEnabled: false,
			itemCullMask: 3);
	}

	private void SetupEmbers()
	{
		var embers = new CpuParticles2D();
		embers.Name = "Embers";
		embers.Position = new Vector2(0, -7);
		embers.Amount = 6;
		embers.Lifetime = 0.65f;
		embers.Explosiveness = 0.0f;
		embers.Randomness = 0.5f;
		embers.LocalCoords = false;
		embers.EmissionShape = CpuParticles2D.EmissionShapeEnum.Sphere;
		embers.EmissionSphereRadius = 1.5f;
		embers.Direction = new Vector2(0, -1);
		embers.Spread = 25.0f;
		embers.Gravity = new Vector2(0, -14.0f);
		embers.InitialVelocityMin = 4.0f;
		embers.InitialVelocityMax = 10.0f;
		embers.ScaleAmountMin = 0.8f;
		embers.ScaleAmountMax = 1.4f;

		var ramp = new Gradient();
		ramp.Colors = new Color[]
		{
			new Color(1.0f, 0.85f, 0.3f, 0.9f),
			new Color(1.0f, 0.35f, 0.05f, 0.0f)
		};
		ramp.Offsets = new float[] { 0.0f, 1.0f };
		embers.ColorRamp = ramp;

		AddChild(embers);
	}

	public override void _Process(double delta)
	{
		_flickerTime += (float)delta * 7.5f;
		if (_torchLight != null && GodotObject.IsInstanceValid(_torchLight))
		{
			float wave = Mathf.Sin(_flickerTime) * 0.06f + Mathf.Cos(_flickerTime * 2.3f) * 0.03f;
			_torchLight.Energy = _baseEnergy + wave;
		}
		QueueRedraw();
	}

	public override void _Draw()
	{
		// Пиксель-арт крепление и древко факела (в стиле 16x16 тайлсета, без коллизий)
		Color ironDark = new Color(0.20f, 0.22f, 0.26f);
		Color ironLight = new Color(0.42f, 0.45f, 0.52f);
		Color woodDark = new Color(0.38f, 0.21f, 0.10f);
		Color woodLight = new Color(0.55f, 0.33f, 0.16f);
		Color headCoal = new Color(0.24f, 0.15f, 0.12f);

		// Железное кольцо-держатель
		DrawRect(new Rect2(-2, -1, 4, 2), ironDark);
		DrawRect(new Rect2(-1, -1, 2, 1), ironLight);

		// Деревянная рукоять
		DrawRect(new Rect2(-1, -4, 2, 6), woodDark);
		DrawRect(new Rect2(-1, -4, 1, 5), woodLight);

		// Осмоленная верхушка факела
		DrawRect(new Rect2(-2, -5, 4, 2), headCoal);

		// Анимированное пиксельное пламя (4 фазы)
		int frame = Mathf.PosMod(Mathf.FloorToInt(_flickerTime * 1.4f), 4);
		int sway = (frame == 1) ? 1 : (frame == 3 ? -1 : 0);
		int topExtra = (frame % 2 == 0) ? 1 : 0;

		Color outerFlame = new Color(0.96f, 0.32f, 0.07f, 0.95f);
		Color midFlame = new Color(1.0f, 0.68f, 0.14f, 0.98f);
		Color coreFlame = new Color(1.0f, 0.95f, 0.62f, 1.0f);

		// Внешний язык пламени
		DrawRect(new Rect2(-2, -9 - topExtra, 4, 4 + topExtra), outerFlame);
		DrawRect(new Rect2(-1 + sway, -11 - topExtra, 2, 2), outerFlame);

		// Среднее пламя
		DrawRect(new Rect2(-1, -8 - topExtra, 3, 3), midFlame);
		DrawRect(new Rect2(sway, -10, 1, 2), midFlame);

		// Яркое ядро
		DrawRect(new Rect2(-1, -7, 2, 2), coreFlame);
	}
}
