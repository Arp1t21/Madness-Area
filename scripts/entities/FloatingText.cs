using Godot;
using System;

[GlobalClass]
public partial class FloatingText : Node2D
{
	private Label _label;
	private float _duration = 0.8f;

	public void Init(string text, Color color)
	{
		Scale = new Vector2(3.0f, 3.0f);
		_label = new Label();
		_label.Text = text;
		_label.AddThemeColorOverride("font_color", color);
		
		// Используем fff-forward для цифр урона (как для большого текста)
		var font = GD.Load<Font>("res://assets/fonts/fff-forward/FFFFORWA.TTF");
		_label.AddThemeFontOverride("font", font);
		_label.AddThemeFontSizeOverride("font_size", 8);
		_label.AddThemeConstantOverride("letter_spacing", 3);
		
		_label.HorizontalAlignment = HorizontalAlignment.Center;
		_label.VerticalAlignment = VerticalAlignment.Center;
		// Центрируем текст относительно ноды
		_label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
		AddChild(_label);

		// Анимация затухания и подъема
		var tween = CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(this, "position", Position + new Vector2(0, -20), _duration);
		tween.TweenProperty(this, "modulate:a", 0.0f, _duration);
		tween.SetParallel(false);
		tween.TweenCallback(Callable.From(QueueFree));
	}
}
