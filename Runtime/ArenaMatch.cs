using System.Collections.Generic;
using ITKamianets.Engine.Core;
using ITKamianets.Engine.Network;
using ITKamianets.Engine.Network.Model;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Temnorid.Arena.Core
{
    /// <summary>
    /// The match: an endless free-for-all deathmatch against bots. One per arena scene, as a
    /// scene-placed NetworkObject. The server spawns the bots, keeps the scoreboard, respawns the
    /// defeated after a delay at the spawn point farthest from enemies, defeats anyone who falls
    /// out of the arena, and announces each defeat as a session message. Everyone reads
    /// <see cref="Scores"/>, built from the combatants' own kills and deaths.
    /// </summary>
    public class ArenaMatch : NetworkBehaviour
    {
        private const string LogCategory = "Arena";

        [SerializeField] private GameObject botPrefab;
        [SerializeField] private int botCount = 5;
        [SerializeField] private float respawnDelay = 3f;

        [Tooltip("Falling below this height defeats a combatant.")]
        [SerializeField] private float killHeight = -20f;

        [Tooltip("The arena's sceneId, written into the session.")]
        [SerializeField] private string sceneId = "arena";

        public static ArenaMatch Current { get; private set; }

        /// <summary>One line per combatant in the match, from its network variables.</summary>
        public List<ArenaScore> Scores
        {
            get
            {
                var rows = new List<ArenaScore>(Combatant.All.Count);
                foreach (var combatant in Combatant.All)
                {
                    rows.Add(new ArenaScore
                    {
                        Id = new FixedString32Bytes(combatant.Id),
                        Name = new FixedString64Bytes(combatant.Name),
                        Kills = combatant.Kills.Value,
                        Deaths = combatant.Deaths.Value,
                        IsBot = combatant.IsBot,
                    });
                }

                return rows;
            }
        }

        public float RespawnDelay => respawnDelay;

        public int BotCount
        {
            get => botCount;
            set => botCount = Mathf.Max(0, value);
        }

        private readonly Dictionary<Combatant, float> _respawnAt = new Dictionary<Combatant, float>();
        private readonly List<Combatant> _bots = new List<Combatant>();

        public override void OnNetworkSpawn()
        {
            Current = this;
            if (!IsServer)
            {
                return;
            }

            Combatant.Defeated += OnDefeated;
            Combatant.Spawned += OnCombatantSpawned;
            Combatant.Despawned += OnCombatantDespawned;

            foreach (var combatant in Combatant.All)
            {
                OnCombatantSpawned(combatant);
            }

            var session = NetworkSession.Current;
            if (session != null && session.Messages.Session != null && session.Messages.Session.State != SessionState.Running)
            {
                session.Messages.SetState(SessionState.Running, sceneId);
            }

            SpawnBots();
        }

        public override void OnNetworkDespawn()
        {
            if (Current == this)
            {
                Current = null;
            }

            Combatant.Defeated -= OnDefeated;
            Combatant.Spawned -= OnCombatantSpawned;
            Combatant.Despawned -= OnCombatantDespawned;
        }

        private void Update()
        {
            if (!IsServer)
            {
                return;
            }

            foreach (var combatant in Combatant.All)
            {
                if (combatant.Alive.Value && combatant.transform.position.y < killHeight)
                {
                    combatant.ApplyDamage(float.MaxValue, null, "fall");
                }

                if (!combatant.Alive.Value && _respawnAt.TryGetValue(combatant, out var at) && Time.time >= at)
                {
                    _respawnAt.Remove(combatant);
                    Respawn(combatant);
                }

                // Player names arrive with their session join, which may come after their character.
                if (!combatant.IsBot && combatant.DisplayName.Value.Length == 0)
                {
                    var player = NetworkSession.Current?.Messages.Session?.FindPlayer(combatant.Id);
                    if (player?.DisplayName != null)
                    {
                        combatant.SetName(player.DisplayName);
                    }
                }
            }
        }

        // ---- Server ----

        private void SpawnBots()
        {
            if (botPrefab == null)
            {
                EngineLog.Warning(LogCategory, "No bot prefab set; the match has no bots.");
                return;
            }

            for (var i = 0; i < botCount; i++)
            {
                var bot = Instantiate(botPrefab);
                bot.GetComponent<NetworkObject>().Spawn(true);
                var combatant = bot.GetComponent<Combatant>();
                combatant.SetName($"Bot {i + 1}");
                _bots.Add(combatant);
            }
        }

        private void OnCombatantSpawned(Combatant combatant)
        {
            Respawn(combatant);
        }

        private void OnCombatantDespawned(Combatant combatant)
        {
            _respawnAt.Remove(combatant);
        }

        private void OnDefeated(Combatant victim, Combatant attacker, string cause)
        {
            victim.Deaths.Value++;
            if (attacker != null && attacker != victim)
            {
                attacker.Kills.Value++;
            }

            _respawnAt[victim] = Time.time + respawnDelay;

            NetworkSession.Current?.Messages.Publish(ArenaEvents.PlayerDefeated, new ArenaEvents.DefeatedPayload
            {
                playerId = victim.Id,
                byPlayerId = attacker != null ? attacker.Id : null,
                cause = cause,
            });
        }

        private void Respawn(Combatant combatant)
        {
            var (position, yaw) = PickSpawn(combatant);
            combatant.Respawn(position, yaw);
        }

        // The spawn point farthest from every living enemy, so nobody appears in front of a gun.
        private (Vector3, float) PickSpawn(Combatant forWhom)
        {
            var points = EngineServices.TryGet<IArenaSpawns>(out var spawns) ? spawns.SpawnPoints : null;
            if (points == null || points.Count == 0)
            {
                var angle = Random.Range(0f, 360f);
                var around = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 6f;
                return (around + Vector3.up * 0.1f, angle + 180f);
            }

            var best = points[Random.Range(0, points.Count)];
            var bestDistance = -1f;
            foreach (var point in points)
            {
                var nearest = float.MaxValue;
                foreach (var other in Combatant.All)
                {
                    if (other != forWhom && other.Alive.Value)
                    {
                        nearest = Mathf.Min(nearest, Vector3.Distance(other.transform.position, point.position));
                    }
                }

                if (nearest > bestDistance)
                {
                    bestDistance = nearest;
                    best = point;
                }
            }

            return (best.position, best.rotation.eulerAngles.y);
        }
    }
}
