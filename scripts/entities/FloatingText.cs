using Godot;
using System;

[GlobalClass]
public partial class FloatingText : Node2D
{
	private Label _label;
	private float _duration = 0.8f;

	public void Init(string text, Color color)
	{
		Material = GameResourceCache.GetUnshadedMaterial();
		ZIndex = 20;
		bool isEmphasized = !string.IsNullOrEmpty(text) && (text.Contains("CRIT") || text.Contains("!"));
		Vector2 baseScale = Vector2.One;
		Scale = baseScale * (isEmphasized ? 1.36f : 1.18f);
		_label = new Label();
		_label.Text = text;
		_label.AddThemeColorOverride("font_color", color);
		
		// Используем fff-forward для цифр урона (как для большого текста)
		var font = GameResourceCache.GetForwardFont();
		_label.AddThemeFontOverride("font", font);
		_label.AddThemeFontSizeOverride("font_size", 7);
		_label.AddThemeConstantOverride("letter_spacing", 1);
		DungeonVisuals.ApplyPixelOutline(_label, 1, new Color(0.03f, 0.02f, 0.04f, 0.98f), 1, 1);
		
		_label.HorizontalAlignment = HorizontalAlignment.Center;
		_label.VerticalAlignment = VerticalAlignment.Center;
		// Центрируем текст относительно ноды
		_label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
		AddChild(_label);

		// Плавный pop + подъем с затуханием
		var tween = CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(this, "scale", baseScale, 0.14f)
			.SetTrans(Tween.TransitionType.Back)
			.SetEase(Tween.EaseType.Out);
		tween.TweenProperty(this, "position", Position + new Vector2(0, -8), _duration)
			.SetTrans(Tween.TransitionType.Quad)
			.SetEase(Tween.EaseType.Out);
		tween.TweenProperty(this, "modulate:a", 0.0f, _duration * 0.72f)
			.SetDelay(_duration * 0.28f);
		tween.SetParallel(false);
		tween.TweenCallback(Callable.From(QueueFree));
	}
}
