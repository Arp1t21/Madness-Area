using Godot;
using System;

/// <summary>
/// Shared dark-fantasy Obsidian &amp; Aged Brass UI styling, pixel-art shaders,
/// procedural VFX textures, and combat feel helpers.
/// </summary>
public static class DungeonVisuals
{
	// Core Obsidian & Brass Palette
	public static readonly Color ObsidianDeep = new Color(0.045f, 0.048f, 0.072f, 0.96f);
	public static readonly Color ObsidianPanel = new Color(0.068f, 0.072f, 0.105f, 0.96f);
	public static readonly Color ObsidianCard = new Color(0.095f, 0.10f, 0.145f, 0.96f);
	public static readonly Color ObsidianElevated = new Color(0.125f, 0.132f, 0.19f, 0.98f);
	public static readonly Color BrassGold = new Color(0.98f, 0.78f, 0.28f, 1.0f);
	public static readonly Color BrassDim = new Color(0.55f, 0.46f, 0.28f, 1.0f);
	public static readonly Color IronBorder = new Color(0.28f, 0.30f, 0.38f, 1.0f);
	public static readonly Color Parchment = new Color(0.95f, 0.92f, 0.86f, 1.0f);
	public static readonly Color MutedSlate = new Color(0.62f, 0.65f, 0.74f, 1.0f);
	public static readonly Color Crimson = new Color(0.92f, 0.22f, 0.28f, 1.0f);
	public static readonly Color CrimsonBright = new Color(1.0f, 0.36f, 0.36f, 1.0f);
	public static readonly Color ManaBlue = new Color(0.22f, 0.62f, 0.98f, 1.0f);
	public static readonly Color Emerald = new Color(0.32f, 0.90f, 0.56f, 1.0f);
	public static readonly Color ArcaneCyan = new Color(0.32f, 0.86f, 1.0f, 1.0f);

	private static Texture2D _groundShadowTex;
	private static Texture2D _pixelSquareTex;
	private static Texture2D _softOrbTex;
	private static Shader _hitFlashShader;
	private static Shader _vignetteShader;

	public static StyleBoxFlat CreatePanelStyle(
		Color bg,
		Color border,
		int borderWidth = 2,
		int cornerRadius = 4,
		int shadowSize = 5,
		Color? shadowColor = null)
	{
		return new StyleBoxFlat
		{
			BgColor = bg,
			BorderColor = border,
			BorderWidthLeft = borderWidth,
			BorderWidthTop = borderWidth,
			BorderWidthRight = borderWidth,
			BorderWidthBottom = borderWidth,
			CornerRadiusTopLeft = cornerRadius,
			CornerRadiusTopRight = cornerRadius,
			CornerRadiusBottomLeft = cornerRadius,
			CornerRadiusBottomRight = cornerRadius,
			ShadowColor = shadowColor ?? new Color(0f, 0f, 0f, 0.65f),
			ShadowSize = shadowSize,
			ShadowOffset = new Vector2(0, 2)
		};
	}

	public static StyleBoxFlat CreatePillStyle(
		Color bg,
		Color border,
		int borderWidth = 1,
		int cornerRadius = 3,
		int padH = 8,
		int padV = 4)
	{
		var sb = CreatePanelStyle(bg, border, borderWidth, cornerRadius, 2, new Color(0f, 0f, 0f, 0.45f));
		sb.ContentMarginLeft = padH;
		sb.ContentMarginRight = padH;
		sb.ContentMarginTop = padV;
		sb.ContentMarginBottom = padV;
		return sb;
	}

	/// <summary>
	/// Applies unified Obsidian &amp; Brass dungeon button styling across states.
	/// </summary>
	public static void StyleButton(
		Button btn,
		Color accent,
		Font font = null,
		int fontSize = 16,
		int cornerRadius = 3,
		Color? baseBg = null,
		int padH = 12,
		int padV = 6)
	{
		if (btn == null) return;

		Color normalBg = baseBg ?? ObsidianCard;
		Color hoverBg = normalBg.Lightened(0.14f);
		Color pressedBg = normalBg.Darkened(0.18f);
		Color disabledBg = new Color(0.07f, 0.07f, 0.09f, 0.72f);

		var normal = CreatePillStyle(normalBg, accent.Darkened(0.28f), 2, cornerRadius, padH, padV);
		var hover = CreatePillStyle(hoverBg, accent, 2, cornerRadius, padH, padV);
		hover.ShadowColor = new Color(accent.R, accent.G, accent.B, 0.28f);
		hover.ShadowSize = 4;
		var pressed = CreatePillStyle(pressedBg, accent.Lightened(0.2f), 2, cornerRadius, padH, padV);
		var disabled = CreatePillStyle(disabledBg, IronBorder.Darkened(0.35f), 1, cornerRadius, padH, padV);
		disabled.ShadowSize = 0;

		btn.AddThemeStyleboxOverride("normal", normal);
		btn.AddThemeStyleboxOverride("hover", hover);
		btn.AddThemeStyleboxOverride("pressed", pressed);
		btn.AddThemeStyleboxOverride("disabled", disabled);
		btn.AddThemeStyleboxOverride("focus", hover);

		if (font != null) btn.AddThemeFontOverride("font", font);
		if (fontSize > 0) btn.AddThemeFontSizeOverride("font_size", fontSize);

		btn.AddThemeColorOverride("font_color", Parchment);
		btn.AddThemeColorOverride("font_hover_color", accent.Lightened(0.25f));
		btn.AddThemeColorOverride("font_pressed_color", Colors.White);
		btn.AddThemeColorOverride("font_disabled_color", new Color(0.45f, 0.45f, 0.50f, 0.8f));
		btn.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
	}

	public static void ApplyPixelOutline(
		Label label,
		int outlineSize = 2,
		Color? outlineColor = null,
		int shadowX = 1,
		int shadowY = 1)
	{
		if (label == null) return;
		label.AddThemeConstantOverride("outline_size", outlineSize);
		label.AddThemeColorOverride("font_outline_color", outlineColor ?? new Color(0.03f, 0.03f, 0.05f, 0.95f));
		label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.78f));
		label.AddThemeConstantOverride("shadow_offset_x", shadowX);
		label.AddThemeConstantOverride("shadow_offset_y", shadowY);
	}

	public static void StyleProgressBar(
		ProgressBar bar,
		Color fillColor,
		Color bgColor,
		Color borderColor,
		int borderWidth = 1,
		int cornerRadius = 2)
	{
		if (bar == null) return;

		var bg = new StyleBoxFlat
		{
			BgColor = bgColor,
			BorderColor = borderColor,
			BorderWidthLeft = borderWidth,
			BorderWidthTop = borderWidth,
			BorderWidthRight = borderWidth,
			BorderWidthBottom = borderWidth,
			CornerRadiusTopLeft = cornerRadius,
			CornerRadiusTopRight = cornerRadius,
			CornerRadiusBottomLeft = cornerRadius,
			CornerRadiusBottomRight = cornerRadius,
			ShadowColor = new Color(0f, 0f, 0f, 0.55f),
			ShadowSize = 2
		};

		var fill = new StyleBoxFlat
		{
			BgColor = fillColor,
			CornerRadiusTopLeft = Mathf.Max(0, cornerRadius - 1),
			CornerRadiusTopRight = Mathf.Max(0, cornerRadius - 1),
			CornerRadiusBottomLeft = Mathf.Max(0, cornerRadius - 1),
			CornerRadiusBottomRight = Mathf.Max(0, cornerRadius - 1)
		};

		bar.AddThemeStyleboxOverride("background", bg);
		bar.AddThemeStyleboxOverride("fill", fill);
	}

	public static void StyleSlider(Slider slider, Color accent)
	{
		if (slider == null) return;

		var track = new StyleBoxFlat
		{
			BgColor = ObsidianDeep,
			BorderColor = IronBorder,
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 2,
			CornerRadiusTopRight = 2,
			CornerRadiusBottomLeft = 2,
			CornerRadiusBottomRight = 2,
			ContentMarginTop = 3,
			ContentMarginBottom = 3
		};
		var fill = new StyleBoxFlat
		{
			BgColor = accent,
			CornerRadiusTopLeft = 2,
			CornerRadiusTopRight = 2,
			CornerRadiusBottomLeft = 2,
			CornerRadiusBottomRight = 2
		};

		slider.AddThemeStyleboxOverride("slider", track);
		slider.AddThemeStyleboxOverride("grabber_area", fill);
		slider.AddThemeStyleboxOverride("grabber_area_highlight", fill);
		slider.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
	}

	public static ColorRect AddDivider(Control parent, Color? color = null, float height = 1.5f)
	{
		var line = new ColorRect
		{
			CustomMinimumSize = new Vector2(0, height),
			Color = color ?? new Color(BrassDim.R, BrassDim.G, BrassDim.B, 0.65f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		parent.AddChild(line);
		return line;
	}

	/// <summary>
	/// Adds crisp corner brass studs and an inner hairline accent to any panel or card.
	/// </summary>
	public static void AttachFrameAccent(Control parent, Color? cornerColor = null, int inset = 4)
	{
		if (parent == null) return;
		var overlay = new DungeonFrameOverlay
		{
			CornerColor = cornerColor ?? BrassGold,
			Inset = inset,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		parent.AddChild(overlay);
	}

	public static Texture2D GetGroundShadowTexture()
	{
		if (_groundShadowTex != null) return _groundShadowTex;

		const int w = 24;
		const int h = 12;
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		float cx = (w - 1) * 0.5f;
		float cy = (h - 1) * 0.5f;
		float rx = w * 0.48f;
		float ry = h * 0.45f;

		for (int y = 0; y < h; y++)
		{
			for (int x = 0; x < w; x++)
			{
				float dx = (x - cx) / rx;
				float dy = (y - cy) / ry;
				float d = Mathf.Sqrt(dx * dx + dy * dy);
				if (d <= 1.0f)
				{
					float alpha = (1.0f - d * d) * 0.72f;
					img.SetPixel(x, y, new Color(0.02f, 0.02f, 0.05f, alpha));
				}
			}
		}

		_groundShadowTex = ImageTexture.CreateFromImage(img);
		return _groundShadowTex;
	}

	public static Texture2D GetPixelSquareTexture()
	{
		if (_pixelSquareTex != null) return _pixelSquareTex;
		var img = Image.CreateEmpty(3, 3, false, Image.Format.Rgba8);
		img.Fill(Colors.White);
		_pixelSquareTex = ImageTexture.CreateFromImage(img);
		return _pixelSquareTex;
	}

	public static Texture2D GetSoftOrbTexture()
	{
		if (_softOrbTex != null) return _softOrbTex;
		const int size = 16;
		var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
		float c = (size - 1) * 0.5f;
		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				float d = new Vector2(x - c, y - c).Length() / c;
				if (d <= 1.0f)
				{
					float a = Mathf.Pow(1.0f - d, 2.0f);
					img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
				}
			}
		}
		_softOrbTex = ImageTexture.CreateFromImage(img);
		return _softOrbTex;
	}

	public static Shader GetHitFlashShader()
	{
		if (_hitFlashShader != null) return _hitFlashShader;
		_hitFlashShader = new Shader
		{
			Code = @"shader_type canvas_item;
uniform vec4 flash_color : source_color = vec4(1.0, 0.96, 0.92, 1.0);
uniform float flash_amount : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	vec4 tex = texture(TEXTURE, UV) * COLOR;
	vec3 col = mix(tex.rgb, flash_color.rgb, flash_amount);
	COLOR = vec4(col, tex.a);
}"
		};
		return _hitFlashShader;
	}

	public static ShaderMaterial CreateHitFlashMaterial()
	{
		var mat = new ShaderMaterial { Shader = GetHitFlashShader() };
		mat.SetShaderParameter("flash_amount", 0.0f);
		return mat;
	}

	public static ShaderMaterial GetVignetteMaterial(float intensity = 0.42f, float softness = 0.48f, Color? color = null)
	{
		if (_vignetteShader == null)
		{
			_vignetteShader = new Shader
			{
				Code = @"shader_type canvas_item;
uniform float intensity : hint_range(0.0, 1.0) = 0.42;
uniform float softness : hint_range(0.05, 1.0) = 0.48;
uniform vec4 vignette_color : source_color = vec4(0.01, 0.01, 0.025, 1.0);

void fragment() {
	vec2 centered = UV - vec2(0.5);
	float dist = length(centered) * 1.4142135;
	float vig = smoothstep(1.0 - softness, 1.05, dist);
	COLOR = vec4(vignette_color.rgb, vig * intensity * vignette_color.a);
}"
			};
		}
		var mat = new ShaderMaterial { Shader = _vignetteShader };
		mat.SetShaderParameter("intensity", intensity);
		mat.SetShaderParameter("softness", softness);
		mat.SetShaderParameter("vignette_color", color ?? new Color(0.01f, 0.01f, 0.025f, 1.0f));
		return mat;
	}

	public static Sprite2D AttachGroundShadow(Node2D target, Vector2 localOffset, Vector2 scale, float opacity = 0.55f)
	{
		if (target == null) return null;
		var shadow = target.GetNodeOrNull<Sprite2D>("GroundShadow");
		if (shadow != null) return shadow;

		shadow = new Sprite2D
		{
			Name = "GroundShadow",
			Texture = GetGroundShadowTexture(),
			Position = localOffset,
			Scale = scale,
			ZIndex = -1,
			Modulate = new Color(1f, 1f, 1f, opacity)
		};
		target.AddChild(shadow);
		return shadow;
	}

	public static void SpawnImpactSparks(
		Node worldParent,
		Vector2 worldPos,
		Vector2 direction,
		Color color,
		int count = 8,
		float speed = 130.0f,
		float spreadDeg = 70.0f,
		float particleScale = 1.6f)
	{
		if (worldParent == null || !worldParent.IsInsideTree()) return;

		var particles = new CpuParticles2D
		{
			GlobalPosition = worldPos,
			ZIndex = 12,
			Material = GameResourceCache.GetUnshadedMaterial(),
			Texture = GetPixelSquareTexture(),
			Emitting = true,
			OneShot = true,
			Explosiveness = 1.0f,
			Amount = count,
			Lifetime = 0.22f,
			Direction = direction == Vector2.Zero ? Vector2.Right : direction.Normalized(),
			Spread = spreadDeg,
			Gravity = new Vector2(0f, 120f),
			InitialVelocityMin = speed * 0.55f,
			InitialVelocityMax = speed * 1.15f,
			ScaleAmountMin = particleScale * 0.7f,
			ScaleAmountMax = particleScale * 1.3f,
			Color = color
		};

		var grad = new Gradient();
		grad.SetColor(0, color.Lightened(0.35f));
		grad.SetColor(1, new Color(color.R, color.G, color.B, 0f));
		particles.ColorRamp = grad;

		worldParent.AddChild(particles);

		var timer = worldParent.GetTree().CreateTimer(0.35f);
		timer.Timeout += () =>
		{
			if (GodotObject.IsInstanceValid(particles)) particles.QueueFree();
		};
	}

	public static void SpawnMuzzleFlash(Node worldParent, Vector2 worldPos, Color color, float radius = 8.0f)
	{
		if (worldParent == null || !worldParent.IsInsideTree()) return;

		var flash = new Sprite2D
		{
			GlobalPosition = worldPos,
			Texture = GetSoftOrbTexture(),
			Scale = new Vector2(radius * 0.22f, radius * 0.22f),
			ZIndex = 13,
			Material = GameResourceCache.GetUnshadedMaterial(),
			Modulate = color.Lightened(0.4f)
		};
		worldParent.AddChild(flash);

		var tween = flash.CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(flash, "scale", Vector2.One * (radius * 0.04f), 0.08f);
		tween.TweenProperty(flash, "modulate:a", 0.0f, 0.08f);
		tween.Chain().TweenCallback(Callable.From(() =>
		{
			if (GodotObject.IsInstanceValid(flash)) flash.QueueFree();
		}));
	}

	public static void SpawnDashAfterimage(Node worldParent, AnimatedSprite2D sourceSprite, Color tint, Vector2 worldScale)
	{
		if (worldParent == null || sourceSprite == null || sourceSprite.SpriteFrames == null) return;
		var tex = sourceSprite.SpriteFrames.GetFrameTexture(sourceSprite.Animation, sourceSprite.Frame);
		if (tex == null) return;

		var ghost = new Sprite2D
		{
			Texture = tex,
			GlobalPosition = sourceSprite.GlobalPosition,
			Scale = worldScale,
			FlipH = sourceSprite.FlipH,
			ZIndex = 1,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			Material = GameResourceCache.GetUnshadedMaterial(),
			Modulate = tint
		};
		worldParent.AddChild(ghost);

		var tween = ghost.CreateTween();
		tween.TweenProperty(ghost, "modulate:a", 0.0f, 0.22f);
		tween.TweenCallback(Callable.From(() =>
		{
			if (GodotObject.IsInstanceValid(ghost)) ghost.QueueFree();
		}));
	}

	public static void SpawnFootstepDust(Node worldParent, Vector2 worldPos, Color? tint = null)
	{
		if (worldParent == null || !worldParent.IsInsideTree()) return;

		var c = tint ?? new Color(0.55f, 0.50f, 0.45f, 0.65f);
		var particles = new CpuParticles2D
		{
			GlobalPosition = worldPos,
			ZIndex = 0,
			Texture = GetPixelSquareTexture(),
			Emitting = true,
			OneShot = true,
			Explosiveness = 1.0f,
			Amount = 3,
			Lifetime = 0.24f,
			Direction = Vector2.Up,
			Spread = 55.0f,
			Gravity = new Vector2(0f, -15f),
			InitialVelocityMin = 12f,
			InitialVelocityMax = 28f,
			ScaleAmountMin = 1.0f,
			ScaleAmountMax = 1.7f,
			Color = c
		};

		var grad = new Gradient();
		grad.SetColor(0, c);
		grad.SetColor(1, new Color(c.R, c.G, c.B, 0f));
		particles.ColorRamp = grad;

		worldParent.AddChild(particles);
		var timer = worldParent.GetTree().CreateTimer(0.32f);
		timer.Timeout += () =>
		{
			if (GodotObject.IsInstanceValid(particles)) particles.QueueFree();
		};
	}

	public static DungeonRuneAura CreateRuneAura(float radius, Color color, int spokeCount = 6, int zIndex = 0)
	{
		return new DungeonRuneAura
		{
			Radius = radius,
			AuraColor = color,
			SpokeCount = spokeCount,
			ZIndex = zIndex
		};
	}
}

/// <summary>
/// Draws crisp pixel brass corner rivets and inner top specular line on UI frames.
/// </summary>
public partial class DungeonFrameOverlay : Control
{
	public Color CornerColor { get; set; } = DungeonVisuals.BrassGold;
	public int Inset { get; set; } = 4;

	public override void _Draw()
	{
		Vector2 s = Size;
		if (s.X < 20 || s.Y < 20) return;

		Color c = new Color(CornerColor.R, CornerColor.G, CornerColor.B, 0.85f);
		Color highlight = new Color(1f, 1f, 1f, 0.08f);
		DrawLine(new Vector2(6, 2), new Vector2(s.X - 6, 2), highlight, 1.0f);

		float r = 2.0f;
		DrawRect(new Rect2(Inset, Inset, r, r), c);
		DrawRect(new Rect2(s.X - Inset - r, Inset, r, r), c);
		DrawRect(new Rect2(Inset, s.Y - Inset - r, r, r), c);
		DrawRect(new Rect2(s.X - Inset - r, s.Y - Inset - r, r, r), c);
	}
}

/// <summary>
/// Unshaded pulsing magical floor rune ring for interactive altars and exit ladders.
/// </summary>
public partial class DungeonRuneAura : Node2D
{
	public float Radius { get; set; } = 22.0f;
	public Color AuraColor { get; set; } = DungeonVisuals.ArcaneCyan;
	public int SpokeCount { get; set; } = 6;
	private float _phase = 0.0f;

	public override void _Ready()
	{
		Material = GameResourceCache.GetUnshadedMaterial();
	}

	public override void _Process(double delta)
	{
		_phase += (float)delta;
		QueueRedraw();
	}

	public override void _Draw()
	{
		float pulse = 0.65f + 0.35f * Mathf.Sin(_phase * 2.8f);
		Color outer = new Color(AuraColor.R, AuraColor.G, AuraColor.B, 0.28f * pulse);
		Color inner = new Color(AuraColor.R, AuraColor.G, AuraColor.B, 0.55f * pulse);

		DrawArc(Vector2.Zero, Radius, 0, Mathf.Tau, 32, outer, 1.4f, false);
		DrawArc(Vector2.Zero, Radius * 0.72f, 0, Mathf.Tau, 24, new Color(AuraColor.R, AuraColor.G, AuraColor.B, 0.16f * pulse), 1.0f, false);

		for (int i = 0; i < SpokeCount; i++)
		{
			float ang = _phase * 0.7f + (Mathf.Tau / SpokeCount) * i;
			Vector2 p1 = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (Radius * 0.72f);
			Vector2 p2 = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Radius;
			DrawLine(p1, p2, inner, 1.2f);
		}
	}
}
