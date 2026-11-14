using Godot;

public partial class ExitLadder : Area2D, IInteractable
{
	private Label _promptLabel;
	private Level _level;

	public void Setup(Level level)
	{
		_level = level;
		Name = "ExitLadder";
		Scale = Vector2.One;
		CollisionLayer = 0;
		CollisionMask = 16; // Слой 5 коллизий (для детекта Area2DPickWeapon игрока)

		var collision = new CollisionShape2D();
		var circle = new CircleShape2D();
		circle.Radius = 15.0f;
		collision.Shape = circle;
		AddChild(collision);

		var aura = DungeonVisuals.CreateRuneAura(15.0f, new Color(0.32f, 0.96f, 0.65f, 0.78f), 6, -1);
		AddChild(aura);

		var ladderGlowTex = GameResourceCache.GetRadialGlowTexture(
			new Color(0.45f, 1.0f, 0.72f, 0.9f),
			Colors.Transparent,
			1.0f,
			64,
			new Vector2(0.85f, 0.85f));
		GameResourceCache.CreateGlowLight(
			this,
			"ExitGlow",
			Vector2.Zero,
			new Color(0.38f, 0.96f, 0.66f, 0.95f),
			0.82f,
			ladderGlowTex,
			shadowEnabled: false);

		_promptLabel = new Label();
		_promptLabel.Text = "Go deeper [E]";
		_promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_promptLabel.Position = new Vector2(-40, -18);
		_promptLabel.Size = new Vector2(80, 15);
		_promptLabel.AddThemeColorOverride("font_color", new Color(0.55f, 1.0f, 0.72f));
		
		var font = GD.Load<Font>("res://assets/fonts/silver/Silver.ttf");
		if (font != null) {
			_promptLabel.AddThemeFontOverride("font", font);
			_promptLabel.AddThemeFontSizeOverride("font_size", 8);
		}
		_promptLabel.AddThemeConstantOverride("letter_spacing", 2);
		DungeonVisuals.ApplyPixelOutline(_promptLabel, 2);
		_promptLabel.Visible = false;
		AddChild(_promptLabel);

		AreaEntered += OnAreaEntered;
		AreaExited += OnAreaExited;
	}

	private readonly System.Collections.Generic.HashSet<long> _readyPeers = new System.Collections.Generic.HashSet<long>();
	private bool _graceTimerActive = false;
	private float _graceRemaining = 0.0f;
	private int _lastBroadcastSec = -1;
	private bool _descending = false;
	private const float CoopGraceDuration = 10.0f;

	private bool IsCoopSession => NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession;
	private bool IsServerAuthority => NetworkManager.Instance == null || NetworkManager.Instance.IsServerAuthority;

	public override void _Process(double delta)
	{
		if (!IsCoopSession || !IsServerAuthority || !_graceTimerActive || _descending) return;

		_graceRemaining -= (float)delta;
		if (_graceRemaining <= 0.0f)
		{
			TriggerDescendOnServer();
			return;
		}

		int ceilSec = Mathf.CeilToInt(_graceRemaining);
		if (ceilSec != _lastBroadcastSec)
		{
			_lastBroadcastSec = ceilSec;
			var alive = NetworkManager.GetAlivePlayers(GetTree());
			Rpc(nameof(RpcSyncLadderReadyState), true, _readyPeers.Count, alive.Count, _graceRemaining);
		}
	}

	public void Interact(Player player)
	{
		if (!IsCoopSession)
		{
			player.UnregisterInteractable(this);
			_level.DescendStairs();
			return;
		}

		if (player == null || !player.IsLocalPlayer || player.IsDead || _descending) return;
		player.UnregisterInteractable(this);
		ShowPrompt(false);

		if (IsServerAuthority)
		{
			MarkPeerReadyOnServer(player.PeerId);
		}
		else
		{
			RpcId(1, nameof(RpcRequestMarkReady));
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcRequestMarkReady()
	{
		if (!IsServerAuthority || _descending) return;
		long senderPeerId = Multiplayer.GetRemoteSenderId();
		Player p = NetworkManager.GetPlayerByPeerId(GetTree(), senderPeerId);
		if (p != null && !p.IsDead)
		{
			MarkPeerReadyOnServer(senderPeerId);
		}
	}

	private void MarkPeerReadyOnServer(long peerId)
	{
		_readyPeers.Add(peerId);
		if (!_graceTimerActive)
		{
			_graceTimerActive = true;
			_graceRemaining = CoopGraceDuration;
			_lastBroadcastSec = Mathf.CeilToInt(_graceRemaining);
		}
		RefreshReadyCheckOnServer();
	}

	public void RefreshReadyCheckOnServer()
	{
		if (!IsCoopSession || !IsServerAuthority || _descending) return;
		var alive = NetworkManager.GetAlivePlayers(GetTree());
		var aliveIds = new System.Collections.Generic.HashSet<long>();
		foreach (var p in alive)
		{
			aliveIds.Add(p.PeerId);
		}
		_readyPeers.RemoveWhere(id => !aliveIds.Contains(id));

		int totalAlive = alive.Count;
		int readyCount = _readyPeers.Count;
		if (totalAlive > 0 && readyCount >= totalAlive)
		{
			TriggerDescendOnServer();
			return;
		}

		if (readyCount == 0)
		{
			_graceTimerActive = false;
			_graceRemaining = 0.0f;
			_lastBroadcastSec = -1;
		}

		Rpc(nameof(RpcSyncLadderReadyState), _graceTimerActive && readyCount > 0, readyCount, totalAlive, _graceRemaining);
	}

	private void TriggerDescendOnServer()
	{
		if (_descending) return;
		_descending = true;
		_graceTimerActive = false;
		Rpc(nameof(RpcSyncLadderReadyState), false, 0, 1, 0.0f);
		_level?.DescendStairs();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcSyncLadderReadyState(bool visible, int readyCount, int totalAlive, float secondsLeft)
	{
		int sender = Multiplayer.GetRemoteSenderId();
		if (IsCoopSession && sender != 0 && sender != 1) return;
		var hud = GetTree()?.GetFirstNodeInGroup("HUD") as HUD;
		hud?.SetLadderReadyBanner(visible, readyCount, totalAlive, secondsLeft);
	}

	public Vector2 GetInteractionPosition()
	{
		return GlobalPosition;
	}

	public void ShowPrompt(bool show)
	{
		if (_promptLabel != null) {
			_promptLabel.Visible = show;
		}
	}

	private void OnAreaEntered(Area2D area)
	{
		if (area.GetParent() is Player player) {
			if (IsCoopSession && (!player.IsLocalPlayer || player.IsDead)) return;
			player.RegisterInteractable(this);
		}
	}

	private void OnAreaExited(Area2D area)
	{
		if (area.GetParent() is Player player) {
			player.UnregisterInteractable(this);
		}
	}
}
