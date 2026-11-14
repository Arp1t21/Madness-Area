using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Autoload singleton managing ENet co-op sessions, lobby state, authoritative queries,
/// and scene/floor synchronization across peers.
/// </summary>
public partial class NetworkManager : Node
{
	public const int DefaultPort = 7777;
	public const int MaxPlayers = GameLogic.MaxCoopPlayers;

	public static NetworkManager Instance { get; private set; }

	public class LobbyPlayerInfo
	{
		public long PeerId;
		public string PlayerName;
		public string CharacterName;
		public bool IsReady;
		public int SlotOrder;
	}

	[Signal] public delegate void LobbyUpdatedEventHandler();
	[Signal] public delegate void ConnectionSucceededEventHandler();
	[Signal] public delegate void ConnectionFailedEventHandler();
	[Signal] public delegate void ServerDisconnectedEventHandler();
	[Signal] public delegate void PlayerLeftRunEventHandler(long peerId, string playerName);
	[Signal] public delegate void StatusMessageEventHandler(string message);

	private readonly Dictionary<long, LobbyPlayerInfo> _lobbyPlayers = new Dictionary<long, LobbyPlayerInfo>();
	private readonly HashSet<long> _loadedPeers = new HashSet<long>();
	private int _nextSlotOrder = 0;

	private string _pendingLocalName = "Player";
	private string _pendingLocalCharacter = "Knight";
	private int _nextEntityNetId = 1;
	private double _levelSyncTimeout = -1.0;
	private bool _floorInitDispatched = false;

	public bool IsCoopSession { get; private set; } = false;
	public bool IsHost => IsCoopSession && Multiplayer.HasMultiplayerPeer() && Multiplayer.IsServer();
	public bool IsServerAuthority => !IsCoopSession || (Multiplayer.HasMultiplayerPeer() && Multiplayer.IsServer());
	public long LocalPeerId => IsCoopSession && Multiplayer.HasMultiplayerPeer() ? Multiplayer.GetUniqueId() : 1L;
	public bool RunStarted { get; private set; } = false;
	public ulong CurrentFloorSeed { get; set; } = 0;
	public int CurrentRunFloorIndex { get; set; } = 1;
	public string PendingDisconnectNotice { get; set; } = "";

	public IReadOnlyDictionary<long, LobbyPlayerInfo> LobbyPlayers => _lobbyPlayers;

	public int CoopPlayerCount => IsCoopSession ? Math.Max(1, _lobbyPlayers.Count) : 1;

	public int AllocateEntityNetId() => _nextEntityNetId++;

	public void ResetEntityNetIdCounter() => _nextEntityNetId = 1;

	public override void _Ready()
	{
		if (Instance != null && Instance != this)
		{
			QueueFree();
			return;
		}
		Instance = this;
		ProcessMode = ProcessModeEnum.Always;

		Multiplayer.PeerConnected += OnPeerConnected;
		Multiplayer.PeerDisconnected += OnPeerDisconnected;
		Multiplayer.ConnectedToServer += OnConnectedToServer;
		Multiplayer.ConnectionFailed += OnConnectionFailed;
		Multiplayer.ServerDisconnected += OnServerDisconnected;
	}

	public override void _Process(double delta)
	{
		if (!IsCoopSession || !IsHost || !RunStarted || _floorInitDispatched) return;

		if (_levelSyncTimeout > 0.0)
		{
			_levelSyncTimeout -= delta;
			if (_levelSyncTimeout <= 0.0)
			{
				TryDispatchFloorStart();
			}
		}
	}

	public static ulong GenerateRandomSeed()
	{
		return ((ulong)GD.Randi() << 32) | GD.Randi();
	}

	public static string SanitizePlayerName(string rawName, string fallback)
	{
		string trimmed = (rawName ?? "").Trim();
		if (string.IsNullOrEmpty(trimmed)) return fallback;
		return trimmed.Length > 14 ? trimmed.Substring(0, 14) : trimmed;
	}

	public static string NormalizeHeroName(string rawHero)
	{
		if (string.Equals(rawHero, "Magician", StringComparison.OrdinalIgnoreCase)) return "Magician";
		if (string.Equals(rawHero, "Rogue", StringComparison.OrdinalIgnoreCase)) return "Rogue";
		if (string.Equals(rawHero, "Berserker", StringComparison.OrdinalIgnoreCase)) return "Berserker";
		return "Knight";
	}

	public Error HostGame(int port = DefaultPort, string playerName = "Host", string characterName = "Knight")
	{
		Disconnect(returnToMenu: false);

		var peer = new ENetMultiplayerPeer();
		Error err = peer.CreateServer(port, MaxPlayers - 1);
		if (err != Error.Ok)
		{
			EmitSignal(SignalName.StatusMessage, $"Failed to host on port {port}: {err}");
			return err;
		}

		Multiplayer.MultiplayerPeer = peer;
		IsCoopSession = true;
		RunStarted = false;
		_nextSlotOrder = 0;
		_lobbyPlayers.Clear();
		_loadedPeers.Clear();
		_floorInitDispatched = false;

		string cleanName = SanitizePlayerName(playerName, "Host");
		string cleanHero = NormalizeHeroName(characterName);
		_lobbyPlayers[1L] = new LobbyPlayerInfo
		{
			PeerId = 1L,
			PlayerName = cleanName,
			CharacterName = cleanHero,
			IsReady = true,
			SlotOrder = _nextSlotOrder++
		};

		EmitSignal(SignalName.LobbyUpdated);
		EmitSignal(SignalName.StatusMessage, $"Hosting lobby on port {port}. Waiting for players...");
		return Error.Ok;
	}

	public Error JoinGame(string address, int port = DefaultPort, string playerName = "Player", string characterName = "Knight")
	{
		Disconnect(returnToMenu: false);

		string hostIp = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
		_pendingLocalName = SanitizePlayerName(playerName, "Player");
		_pendingLocalCharacter = NormalizeHeroName(characterName);

		var peer = new ENetMultiplayerPeer();
		Error err = peer.CreateClient(hostIp, port);
		if (err != Error.Ok)
		{
			EmitSignal(SignalName.StatusMessage, $"Could not connect to {hostIp}:{port} ({err})");
			return err;
		}

		Multiplayer.MultiplayerPeer = peer;
		IsCoopSession = true;
		RunStarted = false;
		_lobbyPlayers.Clear();
		_loadedPeers.Clear();
		_floorInitDispatched = false;

		EmitSignal(SignalName.StatusMessage, $"Connecting to {hostIp}:{port}...");
		return Error.Ok;
	}

	public void Disconnect(bool returnToMenu = false)
	{
		if (Multiplayer.HasMultiplayerPeer())
		{
			Multiplayer.MultiplayerPeer.Close();
			Multiplayer.MultiplayerPeer = null;
		}

		IsCoopSession = false;
		RunStarted = false;
		_floorInitDispatched = false;
		_levelSyncTimeout = -1.0;
		_nextSlotOrder = 0;
		_nextEntityNetId = 1;
		_lobbyPlayers.Clear();
		_loadedPeers.Clear();

		Engine.TimeScale = 1.0f;
		var tree = GetTree();
		if (tree != null)
		{
			tree.Paused = false;
			if (returnToMenu)
			{
				tree.ChangeSceneToFile("res://scene/levels/main_menu.tscn");
			}
		}

		EmitSignal(SignalName.LobbyUpdated);
	}

	public List<LobbyPlayerInfo> GetOrderedLobbyPlayers()
	{
		return _lobbyPlayers.Values.OrderBy(p => p.SlotOrder).ThenBy(p => p.PeerId).ToList();
	}

	public LobbyPlayerInfo GetLobbyPlayer(long peerId)
	{
		return _lobbyPlayers.TryGetValue(peerId, out var info) ? info : null;
	}

	public void SelectCharacter(string characterName)
	{
		string hero = NormalizeHeroName(characterName);
		_pendingLocalCharacter = hero;
		if (!IsCoopSession) return;

		if (IsHost)
		{
			if (_lobbyPlayers.TryGetValue(1L, out var hostInfo))
			{
				hostInfo.CharacterName = hero;
				BroadcastLobbyState();
			}
		}
		else if (Multiplayer.HasMultiplayerPeer())
		{
			RpcId(1L, MethodName.RpcClientSelectCharacter, hero);
		}
	}

	public void SetLocalReady(bool ready)
	{
		if (!IsCoopSession || IsHost) return;
		if (Multiplayer.HasMultiplayerPeer())
		{
			RpcId(1L, MethodName.RpcClientSetReady, ready);
		}
	}

	public bool CanStartCoopRun()
	{
		if (!IsHost || _lobbyPlayers.Count == 0) return false;
		foreach (var p in _lobbyPlayers.Values)
		{
			if (p.PeerId != 1L && !p.IsReady) return false;
		}
		return true;
	}

	public void StartCoopRun()
	{
		if (!IsHost || !CanStartCoopRun()) return;

		SaveManager.CurrentSave = null;
		SaveManager.ActiveSlotId = "";
		CurrentRunFloorIndex = 1;
		CurrentFloorSeed = GenerateRandomSeed();
		RunStarted = true;
		_loadedPeers.Clear();
		_floorInitDispatched = false;
		_nextEntityNetId = 1;

		Rpc(MethodName.RpcBeginLevelTransition, CurrentFloorSeed);
	}

	public void NotifyLocalLevelReady()
	{
		if (!IsCoopSession) return;

		if (IsHost)
		{
			_loadedPeers.Add(1L);
			_levelSyncTimeout = 2.5;
			TryDispatchFloorStart();
		}
		else if (Multiplayer.HasMultiplayerPeer())
		{
			RpcId(1L, MethodName.RpcPeerLevelLoaded);
		}
	}

	private void TryDispatchFloorStart()
	{
		if (!IsHost || _floorInitDispatched) return;

		bool allLoaded = _lobbyPlayers.Keys.All(id => _loadedPeers.Contains(id));
		if (!allLoaded && _levelSyncTimeout > 0.0) return;

		var level = GetTree()?.GetFirstNodeInGroup("Level") as Level;
		if (level == null || !IsInstanceValid(level)) return;

		_floorInitDispatched = true;
		_levelSyncTimeout = -1.0;
		level.ServerInitializeCoopFloor(CurrentRunFloorIndex, CurrentFloorSeed);
	}

	private void OnPeerConnected(long peerId)
	{
		if (!IsHost) return;

		if (RunStarted || _lobbyPlayers.Count >= MaxPlayers)
		{
			GD.Print($"Rejecting peer {peerId} (RunStarted={RunStarted}, Count={_lobbyPlayers.Count})");
			Multiplayer.MultiplayerPeer?.DisconnectPeer((int)peerId);
			return;
		}

		if (!_lobbyPlayers.ContainsKey(peerId))
		{
			_lobbyPlayers[peerId] = new LobbyPlayerInfo
			{
				PeerId = peerId,
				PlayerName = $"Player {peerId}",
				CharacterName = "Knight",
				IsReady = false,
				SlotOrder = _nextSlotOrder++
			};
			BroadcastLobbyState();
		}
	}

	private void OnPeerDisconnected(long peerId)
	{
		string leavingName = _lobbyPlayers.TryGetValue(peerId, out var info) ? info.PlayerName : $"Player {peerId}";
		_lobbyPlayers.Remove(peerId);
		_loadedPeers.Remove(peerId);

		if (RunStarted)
		{
			var tree = GetTree();
			if (tree != null)
			{
				var level = tree.GetFirstNodeInGroup("Level") as Level;
				level?.OnCoopPeerDisconnected(peerId, leavingName);
			}
			EmitSignal(SignalName.PlayerLeftRun, peerId, leavingName);
			if (IsHost && !_floorInitDispatched)
			{
				TryDispatchFloorStart();
			}
		}
		else if (IsHost)
		{
			BroadcastLobbyState();
		}

		EmitSignal(SignalName.LobbyUpdated);
	}

	private void OnConnectedToServer()
	{
		EmitSignal(SignalName.ConnectionSucceeded);
		EmitSignal(SignalName.StatusMessage, "Connected to lobby!");
		RpcId(1L, MethodName.RpcRegisterClientInfo, _pendingLocalName, _pendingLocalCharacter);
	}

	private void OnConnectionFailed()
	{
		Disconnect(returnToMenu: false);
		EmitSignal(SignalName.ConnectionFailed);
		EmitSignal(SignalName.StatusMessage, "Connection failed. Check IP/port and host availability.");
	}

	private void OnServerDisconnected()
	{
		bool wasInRun = RunStarted;
		PendingDisconnectNotice = "Host disconnected from the co-op session.";
		Disconnect(returnToMenu: wasInRun);
		EmitSignal(SignalName.ServerDisconnected);
		EmitSignal(SignalName.StatusMessage, PendingDisconnectNotice);
	}

	private void BroadcastLobbyState()
	{
		if (!IsHost) return;
		var ordered = GetOrderedLobbyPlayers();
		int count = ordered.Count;
		long[] peerIds = new long[count];
		string[] names = new string[count];
		string[] chars = new string[count];
		int[] ready = new int[count];
		int[] slots = new int[count];

		for (int i = 0; i < count; i++)
		{
			peerIds[i] = ordered[i].PeerId;
			names[i] = ordered[i].PlayerName;
			chars[i] = ordered[i].CharacterName;
			ready[i] = ordered[i].IsReady ? 1 : 0;
			slots[i] = ordered[i].SlotOrder;
		}

		Rpc(MethodName.RpcApplyLobbyState, peerIds, names, chars, ready, slots);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcRegisterClientInfo(string playerName, string characterName)
	{
		if (!IsHost) return;
		long senderId = Multiplayer.GetRemoteSenderId();
		if (RunStarted) return;

		if (!_lobbyPlayers.TryGetValue(senderId, out var info))
		{
			if (_lobbyPlayers.Count >= MaxPlayers) return;
			info = new LobbyPlayerInfo
			{
				PeerId = senderId,
				SlotOrder = _nextSlotOrder++
			};
			_lobbyPlayers[senderId] = info;
		}

		info.PlayerName = SanitizePlayerName(playerName, $"Player {senderId}");
		info.CharacterName = NormalizeHeroName(characterName);
		info.IsReady = false;
		BroadcastLobbyState();
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcClientSelectCharacter(string characterName)
	{
		if (!IsHost) return;
		long senderId = Multiplayer.GetRemoteSenderId();
		if (_lobbyPlayers.TryGetValue(senderId, out var info))
		{
			info.CharacterName = NormalizeHeroName(characterName);
			BroadcastLobbyState();
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcClientSetReady(bool ready)
	{
		if (!IsHost) return;
		long senderId = Multiplayer.GetRemoteSenderId();
		if (_lobbyPlayers.TryGetValue(senderId, out var info))
		{
			info.IsReady = ready;
			BroadcastLobbyState();
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcApplyLobbyState(long[] peerIds, string[] names, string[] chars, int[] ready, int[] slots)
	{
		_lobbyPlayers.Clear();
		int count = peerIds?.Length ?? 0;
		for (int i = 0; i < count; i++)
		{
			_lobbyPlayers[peerIds[i]] = new LobbyPlayerInfo
			{
				PeerId = peerIds[i],
				PlayerName = names[i],
				CharacterName = chars[i],
				IsReady = ready[i] != 0,
				SlotOrder = slots[i]
			};
		}
		EmitSignal(SignalName.LobbyUpdated);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcBeginLevelTransition(ulong floorSeed)
	{
		SaveManager.CurrentSave = null;
		SaveManager.ActiveSlotId = "";
		CurrentRunFloorIndex = 1;
		CurrentFloorSeed = floorSeed;
		RunStarted = true;
		_loadedPeers.Clear();
		_floorInitDispatched = false;
		_nextEntityNetId = 1;

		GetTree()?.ChangeSceneToFile("res://scene/levels/level.tscn");
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcPeerLevelLoaded()
	{
		if (!IsHost) return;
		long senderId = Multiplayer.GetRemoteSenderId();
		if (!_lobbyPlayers.ContainsKey(senderId)) return;

		_loadedPeers.Add(senderId);
		TryDispatchFloorStart();
	}

	// --- Shared Player Lookup Helpers ---

	public static List<Player> GetAllPlayers(SceneTree tree)
	{
		var result = new List<Player>();
		if (tree == null) return result;

		foreach (Node node in tree.GetNodesInGroup("Player"))
		{
			if (node is Player p && GodotObject.IsInstanceValid(p) && !p.IsQueuedForDeletion())
			{
				result.Add(p);
			}
		}
		return result;
	}

	public static List<Player> GetAlivePlayers(SceneTree tree)
	{
		var result = new List<Player>();
		if (tree == null) return result;

		foreach (Node node in tree.GetNodesInGroup("Player"))
		{
			if (node is Player p && GodotObject.IsInstanceValid(p) && !p.IsQueuedForDeletion() && !p.IsDead && p.CurrentHealth > 0.0f)
			{
				result.Add(p);
			}
		}
		return result;
	}

	public static Player GetLocalPlayer(SceneTree tree)
	{
		if (tree == null) return null;

		var players = GetAllPlayers(tree);
		if (Instance == null || !Instance.IsCoopSession)
		{
			return players.Count > 0 ? players[0] : null;
		}

		long localId = Instance.LocalPeerId;
		foreach (var p in players)
		{
			if (p.PeerId == localId) return p;
		}
		return null;
	}

	public static Player GetPlayerByPeerId(SceneTree tree, long peerId)
	{
		if (tree == null) return null;
		foreach (var p in GetAllPlayers(tree))
		{
			if (p.PeerId == peerId) return p;
		}
		return null;
	}

	public static Player GetNearestAlivePlayer(SceneTree tree, Vector2 worldPos)
	{
		var alive = GetAlivePlayers(tree);
		Player best = null;
		float bestDistSq = float.MaxValue;
		foreach (var p in alive)
		{
			float dSq = p.GlobalPosition.DistanceSquaredTo(worldPos);
			if (dSq < bestDistSq)
			{
				bestDistSq = dSq;
				best = p;
			}
		}
		return best;
	}
}
