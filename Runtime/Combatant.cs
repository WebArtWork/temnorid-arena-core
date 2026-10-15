using System;
using System.Collections.Generic;
using ITKamianets.Engine.Character;
using ITKamianets.Engine.Network;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Temnorid.Arena.Core
{
    /// <summary>
    /// Anything that fights in the arena, player or bot: health, mana, a damage shield, alive or
    /// dead. The server changes it; everyone sees it through network variables. Put it on the
    /// player and bot prefabs next to the <c>CharacterMotor</c>.
    /// </summary>
    [RequireComponent(typeof(CharacterMotor))]
    public class Combatant : NetworkBehaviour
    {
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float maxMana = 100f;

        [Tooltip("Mana per second.")]
        [SerializeField] private float manaRegen = 12f;

        [SerializeField] private bool isBot;

        [Tooltip("Hide the body while dead. Off for models with a death animation, which then lie where they fell.")]
        [SerializeField] private bool hideBodyWhenDead = true;

        [Tooltip("Turned off while dead, e.g. the controls and the spell caster.")]
        [SerializeField] private Behaviour[] disableWhenDead = new Behaviour[0];

        public readonly NetworkVariable<float> Health = new NetworkVariable<float>(100f);
        public readonly NetworkVariable<float> Mana = new NetworkVariable<float>(100f);
        public readonly NetworkVariable<float> Shield = new NetworkVariable<float>();
        public readonly NetworkVariable<bool> Alive = new NetworkVariable<bool>(true);
        public readonly NetworkVariable<FixedString64Bytes> DisplayName = new NetworkVariable<FixedString64Bytes>();

        /// <summary>This match's score. Kept on each combatant, so every client builds the same scoreboard.</summary>
        public readonly NetworkVariable<int> Kills = new NetworkVariable<int>();

        public readonly NetworkVariable<int> Deaths = new NetworkVariable<int>();

        /// <summary>Every combatant in the match, on every side.</summary>
        public static readonly List<Combatant> All = new List<Combatant>();

        public static event Action<Combatant> Spawned;
        public static event Action<Combatant> Despawned;

        /// <summary>Server: someone was defeated, by whom (null for a fall), and how (a spell id, "fall").</summary>
        public static event Action<Combatant, Combatant, string> Defeated;

        /// <summary>Everyone: this combatant took damage (amount after the shield).</summary>
        public event Action<float> Damaged;

        public event Action<bool> AliveChanged;

        public float MaxHealth => maxHealth;
        public float MaxMana => maxMana;
        public bool IsBot => isBot;
        public CharacterMotor Motor { get; private set; }

        /// <summary>Stable id in the session: the session's player id, or "bot-&lt;n&gt;".</summary>
        public string Id => isBot ? $"bot-{NetworkObjectId}" : SessionChannel.PlayerIdFor(OwnerClientId);

        public string Name => DisplayName.Value.Length > 0 ? DisplayName.Value.ToString() : Id;

        private float _shieldEnds;
        private bool[] _enabledBeforeDeath;

        private void Awake()
        {
            Motor = GetComponent<CharacterMotor>();
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            Alive.OnValueChanged += OnAliveChanged;

            if (IsServer)
            {
                Health.Value = maxHealth;
                Mana.Value = maxMana;
                Shield.Value = 0f;
                Alive.Value = true;
            }

            Spawned?.Invoke(this);
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            Alive.OnValueChanged -= OnAliveChanged;
            Despawned?.Invoke(this);
        }

        private void Update()
        {
            if (!IsServer || !Alive.Value)
            {
                return;
            }

            if (Mana.Value < maxMana)
            {
                Mana.Value = Mathf.Min(maxMana, Mana.Value + manaRegen * Time.deltaTime);
            }

            if (Shield.Value > 0f && Time.time >= _shieldEnds)
            {
                Shield.Value = 0f;
            }
        }

        // ---- Server ----

        public void SetName(string displayName)
        {
            if (IsServer)
            {
                DisplayName.Value = new FixedString64Bytes(displayName);
            }
        }

        /// <summary>Server: takes damage, the shield first. Reaching zero health defeats it.</summary>
        public void ApplyDamage(float amount, Combatant attacker, string cause)
        {
            if (!IsServer || !Alive.Value || amount <= 0f)
            {
                return;
            }

            var absorbed = Mathf.Min(Shield.Value, amount);
            Shield.Value -= absorbed;
            var taken = amount - absorbed;
            Health.Value = Mathf.Max(0f, Health.Value - taken);
            DamagedClientRpc(taken);

            if (Health.Value <= 0f)
            {
                Alive.Value = false;
                Defeated?.Invoke(this, attacker, cause);
            }
        }

        /// <summary>Server: pays mana if there is enough.</summary>
        public bool TrySpendMana(float amount)
        {
            if (!IsServer || !Alive.Value || Mana.Value < amount)
            {
                return false;
            }

            Mana.Value -= amount;
            return true;
        }

        /// <summary>Server: a shield that absorbs damage for a while. A new shield replaces the old one.</summary>
        public void GiveShield(float amount, float seconds)
        {
            if (IsServer && Alive.Value)
            {
                Shield.Value = amount;
                _shieldEnds = Time.time + seconds;
            }
        }

        /// <summary>Server: back to full health and mana at a spawn point.</summary>
        public void Respawn(Vector3 position, float yaw)
        {
            if (!IsServer)
            {
                return;
            }

            Health.Value = maxHealth;
            Mana.Value = maxMana;
            Shield.Value = 0f;
            Motor.Teleport(position, yaw);
            Alive.Value = true;
            TeleportClientRpc(position, yaw);
        }

        // ---- Everyone ----

        [Rpc(SendTo.NotServer)]
        private void TeleportClientRpc(Vector3 position, float yaw)
        {
            Motor.Teleport(position, yaw);
        }

        [Rpc(SendTo.Everyone)]
        private void DamagedClientRpc(float amount)
        {
            Damaged?.Invoke(amount);
        }

        private void OnAliveChanged(bool before, bool alive)
        {
            // Dead bodies stop where they are: no leftover command keeps them walking.
            Motor.SetCommand(new CharacterCommand { Yaw = Motor.Yaw });

            if (hideBodyWhenDead)
            {
                foreach (var renderer in GetComponentsInChildren<Renderer>())
                {
                    renderer.enabled = alive;
                }
            }

            if (!alive)
            {
                _enabledBeforeDeath = new bool[disableWhenDead.Length];
                for (var i = 0; i < disableWhenDead.Length; i++)
                {
                    if (disableWhenDead[i] != null)
                    {
                        _enabledBeforeDeath[i] = disableWhenDead[i].enabled;
                        disableWhenDead[i].enabled = false;
                    }
                }
            }
            else if (_enabledBeforeDeath != null)
            {
                for (var i = 0; i < disableWhenDead.Length; i++)
                {
                    if (disableWhenDead[i] != null)
                    {
                        disableWhenDead[i].enabled = _enabledBeforeDeath[i];
                    }
                }

                _enabledBeforeDeath = null;
            }

            AliveChanged?.Invoke(alive);
        }
    }
}
