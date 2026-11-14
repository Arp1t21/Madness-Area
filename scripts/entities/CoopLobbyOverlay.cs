using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Full-screen Obsidian &amp; Brass Co-op Lobby overlay opened from MainMenu.
/// Handles ENet Host/Join setup, hero selection, ready state, and party roster display.
/// </summary>
public partial class CoopLobbyOverlay : Control
{
	private readonly Action _onClosed;
	private Font _titleFont;
	private Font _mainFont;

	private LineEdit _nameInput;
	private LineEdit _portHostInput;
	private LineEdit _ipJoinInput;
	private LineEdit _portJoinInput;
	private Control _connectSection;
	private Control _sessionSection;
	private Label _sessionHeaderLabel;
	private VBoxContainer _partyListBox;
	private Label _statusLabel;
	private Button _actionBtn;
	private readonly Button[] _heroButtons = new Button[4];
	private static readonly string[] HeroNames = { "Knight", "Magician", "Rogue", "Berserker" };
	private static readonly Color[] HeroAccents =
	{
		new Color(1.0f, 0.72f, 0.24f),
		new Color(0.32f, 0.80f, 1.0f),
		new Color(0.34f, 0.92f, 0.56f),
		new Color(0.96f, 0.34f, 0.38f)
	};

	private string _localHero = "Knight";
	private bool _localReady = false;

	public CoopLobbyOverlay(Action onClosed)
	{
		_onClosed = onClosed;
		Name = "CoopLobbyRoot";
	}

	public override void _Ready()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_titleFont = GD.Load<Font>("res://assets/fonts/silver/Silver.ttf");
		_mainFont = GD.Load<Font>("res://assets/fonts/fff-forward/FFFFORWA.TTF");

		Vector2 viewSize = GetViewport().GetVisibleRect().Size;
		float W = viewSize.X;
		float H = viewSize.Y;

		var backdrop = new ColorRect
		{
			Color = new Color(0.03f, 0.032f, 0.05f, 0.94f)
		};
		backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(backdrop);

		var vig = new ColorRect
		{
			Material = DungeonVisuals.GetVignetteMaterial(0.75f, 0.55f),
			MouseFilter = MouseFilterEnum.Ignore
		};
		vig.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(vig);

		var title = new Label
		{
			Text = "CO-OP DUNGEON LOBBY (2-4 PLAYERS)",
			HorizontalAlignment = HorizontalAlignment.Center,
			Position = new Vector2(0, 14),
			Size = new Vector2(W, 44)
		};
		title.AddThemeFontOverride("font", _titleFont);
		title.AddThemeFontSizeOverride("font_size", 42);
		title.AddThemeColorOverride("font_color", DungeonVisuals.ArcaneCyan);
		DungeonVisuals.ApplyPixelOutline(title, 3);
		AddChild(title);

		float contentTop = 68.0f;
		float contentH = Mathf.Max(260.0f, H - 134.0f);
		float leftW = Mathf.Clamp(W * 0.48f, 340.0f, 480.0f);
		float rightX = 28.0f + leftW + 16.0f;
		float rightW = Mathf.Max(260.0f, W - rightX - 28.0f);

		var leftPanel = CreateFramedPanel(new Vector2(28.0f, contentTop), new Vector2(leftW, contentH), DungeonVisuals.BrassDim);
		AddChild(leftPanel);
		BuildLeftColumn(leftPanel, leftW, contentH);

		var rightPanel = CreateFramedPanel(new Vector2(rightX, contentTop), new Vector2(rightW, contentH), DungeonVisuals.ArcaneCyan.Darkened(0.35f));
		AddChild(rightPanel);
		BuildRightColumn(rightPanel, rightW, contentH);

		var backBtn = new Button
		{
			Text = "< BACK",
			Position = new Vector2(28.0f, H - 56.0f),
			CustomMinimumSize = new Vector2(146, 40),
			Size = new Vector2(146, 40)
		};
		DungeonVisuals.StyleButton(backBtn, DungeonVisuals.IronBorder.Lightened(0.35f), _titleFont, 26, 5);
		backBtn.Pressed += CloseOverlay;
		AddChild(backBtn);

		_actionBtn = new Button
		{
			Text = "START RUN >",
			Position = new Vector2(W - 258.0f, H - 56.0f),
			CustomMinimumSize = new Vector2(230, 40),
			Size = new Vector2(230, 40),
			Visible = false
		};
		DungeonVisuals.StyleButton(_actionBtn, DungeonVisuals.Emerald, _titleFont, 26, 5, new Color(0.06f, 0.16f, 0.10f, 0.98f));
		_actionBtn.Pressed += OnActionPressed;
		AddChild(_actionBtn);

		if (NetworkManager.Instance != null)
		{
			NetworkManager.Instance.LobbyUpdated += RefreshUI;
			NetworkManager.Instance.StatusMessage += OnStatusMessage;
			NetworkManager.Instance.ConnectionFailed += RefreshUI;
			NetworkManager.Instance.ServerDisconnected += RefreshUI;
		}

		RefreshUI();
		MainMenu.ConnectButtonSounds(this);
	}

	public override void _ExitTree()
	{
		if (NetworkManager.Instance != null)
		{
			NetworkManager.Instance.LobbyUpdated -= RefreshUI;
			NetworkManager.Instance.StatusMessage -= OnStatusMessage;
			NetworkManager.Instance.ConnectionFailed -= RefreshUI;
			NetworkManager.Instance.ServerDisconnected -= RefreshUI;
		}
	}

	private Panel CreateFramedPanel(Vector2 pos, Vector2 size, Color borderCol)
	{
		var panel = new Panel
		{
			Position = pos,
			Size = size
		};
		panel.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
			DungeonVisuals.ObsidianPanel, borderCol, 2, 6, 8));
		DungeonVisuals.AttachFrameAccent(panel, borderCol.Lightened(0.2f), 5);
		return panel;
	}

	private void BuildLeftColumn(Panel parent, float w, float h)
	{
		float pad = 16.0f;
		float innerW = w - pad * 2.0f;

		var nameRow = new HBoxContainer
		{
			Position = new Vector2(pad, 14),
			Size = new Vector2(innerW, 30)
		};
		nameRow.AddThemeConstantOverride("separation", 10);
		parent.AddChild(nameRow);

		var nameLbl = CreateLabel("YOUR NAME:", 20, DungeonVisuals.Parchment);
		nameLbl.CustomMinimumSize = new Vector2(115, 28);
		nameRow.AddChild(nameLbl);

		_nameInput = CreateStyledInput(SettingsManager.CoopPlayerName, innerW - 125);
		_nameInput.MaxLength = 14;
		_nameInput.FocusExited += SaveCoopNameSetting;
		_nameInput.TextSubmitted += _ => SaveCoopNameSetting();
		nameRow.AddChild(_nameInput);

		// Connection form (shown while disconnected)
		_connectSection = new VBoxContainer
		{
			Position = new Vector2(pad, 54),
			Size = new Vector2(innerW, 166)
		};
		_connectSection.AddThemeConstantOverride("separation", 8);
		parent.AddChild(_connectSection);

		var hostHeader = CreateLabel("HOST NEW SESSION", 20, DungeonVisuals.BrassGold);
		_connectSection.AddChild(hostHeader);

		var hostRow = new HBoxContainer();
		hostRow.AddThemeConstantOverride("separation", 8);
		_connectSection.AddChild(hostRow);

		var portLbl = CreateLabel("PORT:", 18, DungeonVisuals.MutedSlate);
		portLbl.CustomMinimumSize = new Vector2(54, 28);
		hostRow.AddChild(portLbl);

		_portHostInput = CreateStyledInput(NetworkManager.DefaultPort.ToString(), 80);
		hostRow.AddChild(_portHostInput);

		var hostBtn = new Button
		{
			Text = "HOST LOBBY",
			CustomMinimumSize = new Vector2(innerW - 150, 32),
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		DungeonVisuals.StyleButton(hostBtn, DungeonVisuals.BrassGold, _titleFont, 21, 4, DungeonVisuals.ObsidianCard, 8, 3);
		hostBtn.Pressed += OnHostPressed;
		hostRow.AddChild(hostBtn);

		var joinHeader = CreateLabel("JOIN EXISTING SESSION", 20, DungeonVisuals.ArcaneCyan);
		_connectSection.AddChild(joinHeader);

		var joinRow = new HBoxContainer();
		joinRow.AddThemeConstantOverride("separation", 6);
		_connectSection.AddChild(joinRow);

		_ipJoinInput = CreateStyledInput("127.0.0.1", innerW * 0.44f);
		_ipJoinInput.PlaceholderText = "Host IP";
		joinRow.AddChild(_ipJoinInput);

		_portJoinInput = CreateStyledInput(NetworkManager.DefaultPort.ToString(), 68);
		_portJoinInput.PlaceholderText = "Port";
		joinRow.AddChild(_portJoinInput);

		var joinBtn = new Button
		{
			Text = "JOIN",
			CustomMinimumSize = new Vector2(88, 32),
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		DungeonVisuals.StyleButton(joinBtn, DungeonVisuals.ArcaneCyan, _titleFont, 21, 4, DungeonVisuals.ObsidianCard, 8, 3);
		joinBtn.Pressed += OnJoinPressed;
		joinRow.AddChild(joinBtn);

		// Active session header (shown while connected)
		_sessionSection = new VBoxContainer
		{
			Position = new Vector2(pad, 54),
			Size = new Vector2(innerW, 110),
			Visible = false
		};
		_sessionSection.AddThemeConstantOverride("separation", 10);
		parent.AddChild(_sessionSection);

		_sessionHeaderLabel = CreateLabel("CONNECTED TO LOBBY", 22, DungeonVisuals.Emerald);
		_sessionSection.AddChild(_sessionHeaderLabel);

		var leaveBtn = new Button
		{
			Text = "DISCONNECT / LEAVE LOBBY",
			CustomMinimumSize = new Vector2(innerW, 34)
		};
		DungeonVisuals.StyleButton(leaveBtn, DungeonVisuals.Crimson, _titleFont, 20, 4, new Color(0.18f, 0.05f, 0.06f, 0.95f), 8, 4);
		leaveBtn.Pressed += OnLeaveLobbyPressed;
		_sessionSection.AddChild(leaveBtn);

		// Hero selection grid
		float heroTop = Mathf.Max(228.0f, h - 134.0f);
		var heroHeader = CreateLabel("SELECT YOUR HERO", 20, DungeonVisuals.BrassGold);
		heroHeader.Position = new Vector2(pad, heroTop - 28.0f);
		heroHeader.Size = new Vector2(innerW, 24);
		parent.AddChild(heroHeader);

		var heroGrid = new GridContainer
		{
			Columns = 2,
			Position = new Vector2(pad, heroTop),
			Size = new Vector2(innerW, 92)
		};
		heroGrid.AddThemeConstantOverride("h_separation", 8);
		heroGrid.AddThemeConstantOverride("v_separation", 8);
		parent.AddChild(heroGrid);

		for (int i = 0; i < 4; i++)
		{
			int idx = i;
			var btn = new Button
			{
				Text = HeroNames[i].ToUpperInvariant(),
				CustomMinimumSize = new Vector2((innerW - 8.0f) * 0.5f, 38),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			btn.Pressed += () => OnHeroSelected(HeroNames[idx]);
			heroGrid.AddChild(btn);
			_heroButtons[i] = btn;
		}
	}

	private void BuildRightColumn(Panel parent, float w, float h)
	{
		float pad = 16.0f;
		float innerW = w - pad * 2.0f;

		var partyHeader = CreateLabel("PARTY ROSTER (MAX 4)", 24, DungeonVisuals.BrassGold);
		partyHeader.Position = new Vector2(pad, 14);
		partyHeader.Size = new Vector2(innerW, 28);
		parent.AddChild(partyHeader);

		_partyListBox = new VBoxContainer
		{
			Position = new Vector2(pad, 52),
			Size = new Vector2(innerW, h - 118.0f)
		};
		_partyListBox.AddThemeConstantOverride("separation", 8);
		parent.AddChild(_partyListBox);

		_statusLabel = new Label
		{
			Text = "Host a lobby or enter host IP to join.",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			Position = new Vector2(pad, h - 54.0f),
			Size = new Vector2(innerW, 42)
		};
		_statusLabel.AddThemeFontOverride("font", _titleFont);
		_statusLabel.AddThemeFontSizeOverride("font_size", 18);
		_statusLabel.AddThemeColorOverride("font_color", DungeonVisuals.MutedSlate);
		parent.AddChild(_statusLabel);
	}

	private Label CreateLabel(string text, int fontSize, Color color)
	{
		var lbl = new Label
		{
			Text = text,
			VerticalAlignment = VerticalAlignment.Center
		};
		lbl.AddThemeFontOverride("font", _titleFont);
		lbl.AddThemeFontSizeOverride("font_size", fontSize);
		lbl.AddThemeColorOverride("font_color", color);
		DungeonVisuals.ApplyPixelOutline(lbl, 2);
		return lbl;
	}

	private LineEdit CreateStyledInput(string initialText, float width)
	{
		var edit = new LineEdit
		{
			Text = initialText,
			CustomMinimumSize = new Vector2(width, 30),
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		edit.AddThemeFontOverride("font", _titleFont);
		edit.AddThemeFontSizeOverride("font_size", 20);
		edit.AddThemeColorOverride("font_color", DungeonVisuals.Parchment);
		edit.AddThemeStyleboxOverride("normal", DungeonVisuals.CreatePillStyle(
			DungeonVisuals.ObsidianDeep, DungeonVisuals.IronBorder, 1, 3, 8, 3));
		edit.AddThemeStyleboxOverride("focus", DungeonVisuals.CreatePillStyle(
			DungeonVisuals.ObsidianCard, DungeonVisuals.ArcaneCyan, 2, 3, 8, 3));
		return edit;
	}

	private void SaveCoopNameSetting()
	{
		if (_nameInput == null) return;
		string sanitized = NetworkManager.SanitizePlayerName(_nameInput.Text, "Adventurer");
		SettingsManager.CoopPlayerName = sanitized;
		SettingsManager.SaveSettings();
	}

	private void OnHostPressed()
	{
		SaveCoopNameSetting();
		int port = ParsePort(_portHostInput.Text);
		string name = _nameInput.Text;
		_localReady = true;
		Error err = NetworkManager.Instance.HostGame(port, name, _localHero);
		if (err == Error.Ok)
		{
			RefreshUI();
		}
	}

	private void OnJoinPressed()
	{
		SaveCoopNameSetting();
		int port = ParsePort(_portJoinInput.Text);
		string ip = _ipJoinInput.Text;
		string name = _nameInput.Text;
		_localReady = false;
		Error err = NetworkManager.Instance.JoinGame(ip, port, name, _localHero);
		if (err == Error.Ok)
		{
			RefreshUI();
		}
	}

	private void OnLeaveLobbyPressed()
	{
		_localReady = false;
		NetworkManager.Instance?.Disconnect(returnToMenu: false);
		OnStatusMessage("Left lobby.");
		RefreshUI();
	}

	private void OnHeroSelected(string heroName)
	{
		_localHero = heroName;
		NetworkManager.Instance?.SelectCharacter(heroName);
		RefreshHeroButtons();
	}

	private void OnActionPressed()
	{
		var nm = NetworkManager.Instance;
		if (nm == null || !nm.IsCoopSession) return;

		if (nm.IsHost)
		{
			if (!nm.CanStartCoopRun())
			{
				OnStatusMessage("All connected players must click READY first.");
				return;
			}
			nm.StartCoopRun();
		}
		else
		{
			_localReady = !_localReady;
			nm.SetLocalReady(_localReady);
			RefreshUI();
		}
	}

	private void OnStatusMessage(string msg)
	{
		if (IsInstanceValid(_statusLabel))
		{
			_statusLabel.Text = msg;
		}
	}

	private void RefreshUI()
	{
		var nm = NetworkManager.Instance;
		bool inSession = nm != null && nm.IsCoopSession;
		bool isHost = nm != null && nm.IsHost;

		if (IsInstanceValid(_connectSection)) _connectSection.Visible = !inSession;
		if (IsInstanceValid(_sessionSection)) _sessionSection.Visible = inSession;
		if (IsInstanceValid(_nameInput)) _nameInput.Editable = !inSession;

		if (inSession && IsInstanceValid(_sessionHeaderLabel))
		{
			_sessionHeaderLabel.Text = isHost
				? $"HOSTING LOBBY (PORT {ParsePort(_portHostInput.Text)})"
				: "CONNECTED TO HOST";
		}

		RefreshHeroButtons();
		RefreshPartyList();

		if (IsInstanceValid(_actionBtn))
		{
			_actionBtn.Visible = inSession;
			if (isHost)
			{
				bool canStart = nm.CanStartCoopRun();
				_actionBtn.Text = "START CO-OP RUN >";
				_actionBtn.Disabled = !canStart;
				DungeonVisuals.StyleButton(_actionBtn, DungeonVisuals.Emerald, _titleFont, 25, 5,
					canStart ? new Color(0.06f, 0.18f, 0.10f, 0.98f) : DungeonVisuals.ObsidianDeep);
			}
			else if (inSession)
			{
				_actionBtn.Disabled = false;
				_actionBtn.Text = _localReady ? "READY (CLICK TO CANCEL)" : "MARK READY";
				Color accent = _localReady ? DungeonVisuals.BrassGold : DungeonVisuals.ArcaneCyan;
				DungeonVisuals.StyleButton(_actionBtn, accent, _titleFont, 22, 5, DungeonVisuals.ObsidianCard);
			}
		}
	}

	private void RefreshHeroButtons()
	{
		for (int i = 0; i < _heroButtons.Length; i++)
		{
			var btn = _heroButtons[i];
			if (!IsInstanceValid(btn)) continue;
			bool selected = string.Equals(HeroNames[i], _localHero, StringComparison.OrdinalIgnoreCase);
			Color accent = HeroAccents[i];
			Color bg = selected
				? new Color(accent.R * 0.18f + 0.06f, accent.G * 0.18f + 0.06f, accent.B * 0.18f + 0.08f, 0.98f)
				: DungeonVisuals.ObsidianDeep;
			DungeonVisuals.StyleButton(btn, selected ? accent : DungeonVisuals.IronBorder, _titleFont, 20, 4, bg, 6, 4);
		}
	}

	private void RefreshPartyList()
	{
		if (!IsInstanceValid(_partyListBox)) return;
		foreach (Node child in _partyListBox.GetChildren())
		{
			child.QueueFree();
		}

		var nm = NetworkManager.Instance;
		var players = nm != null && nm.IsCoopSession
			? nm.GetOrderedLobbyPlayers()
			: new List<NetworkManager.LobbyPlayerInfo>();

		for (int slot = 0; slot < NetworkManager.MaxPlayers; slot++)
		{
			var card = new PanelContainer
			{
				CustomMinimumSize = new Vector2(0, 48),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};

			if (slot < players.Count)
			{
				var p = players[slot];
				bool isHostSlot = p.PeerId == 1L;
				Color accent = GetHeroAccent(p.CharacterName);
				card.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
					DungeonVisuals.ObsidianCard, accent.Darkened(0.15f), 2, 4, 3));

				var row = new HBoxContainer();
				row.AddThemeConstantOverride("separation", 10);

				var margin = new MarginContainer();
				margin.AddThemeConstantOverride("margin_left", 10);
				margin.AddThemeConstantOverride("margin_right", 10);
				margin.AddThemeConstantOverride("margin_top", 4);
				margin.AddThemeConstantOverride("margin_bottom", 4);
				margin.AddChild(row);
				card.AddChild(margin);

				string youTag = (nm != null && p.PeerId == nm.LocalPeerId) ? " (YOU)" : "";
				var nameLbl = CreateLabel($"{p.PlayerName}{youTag}", 20, DungeonVisuals.Parchment);
				nameLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				row.AddChild(nameLbl);

				var heroLbl = CreateLabel(p.CharacterName.ToUpperInvariant(), 19, accent);
				heroLbl.CustomMinimumSize = new Vector2(88, 24);
				heroLbl.HorizontalAlignment = HorizontalAlignment.Center;
				row.AddChild(heroLbl);

				string stateText = isHostSlot ? "[HOST]" : (p.IsReady ? "[READY]" : "[WAIT]");
				Color stateCol = isHostSlot ? DungeonVisuals.BrassGold : (p.IsReady ? DungeonVisuals.Emerald : DungeonVisuals.MutedSlate);
				var stateLbl = CreateLabel(stateText, 19, stateCol);
				stateLbl.CustomMinimumSize = new Vector2(68, 24);
				stateLbl.HorizontalAlignment = HorizontalAlignment.Right;
				row.AddChild(stateLbl);
			}
			else
			{
				card.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
					DungeonVisuals.ObsidianDeep, DungeonVisuals.IronBorder.Darkened(0.35f), 1, 4, 1));
				var emptyLbl = CreateLabel($"SLOT #{slot + 1} — OPEN", 18, DungeonVisuals.IronBorder.Lightened(0.25f));
				emptyLbl.HorizontalAlignment = HorizontalAlignment.Center;
				card.AddChild(emptyLbl);
			}

			_partyListBox.AddChild(card);
		}
	}

	private static Color GetHeroAccent(string hero)
	{
		for (int i = 0; i < HeroNames.Length; i++)
		{
			if (string.Equals(HeroNames[i], hero, StringComparison.OrdinalIgnoreCase))
				return HeroAccents[i];
		}
		return DungeonVisuals.BrassGold;
	}

	private static int ParsePort(string text)
	{
		if (int.TryParse(text, out int p) && p >= 1024 && p <= 65535) return p;
		return NetworkManager.DefaultPort;
	}

	private void CloseOverlay()
	{
		SaveCoopNameSetting();
		NetworkManager.Instance?.Disconnect(returnToMenu: false);
		_onClosed?.Invoke();
		QueueFree();
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel"))
		{
			CloseOverlay();
			GetViewport().SetInputAsHandled();
		}
	}
}
