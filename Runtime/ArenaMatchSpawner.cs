using ITKamianets.Engine.Network;
using Unity.Netcode;
using UnityEngine;

namespace Temnorid.Arena.Core
{
    /// <summary>
    /// Starts the match when a session starts: on the server it spawns the <see cref="ArenaMatch"/>
    /// prefab over the network, so every client gets it too. Put one in the arena scene; list the
    /// match prefab in the <c>NetworkSession</c>'s network prefabs.
    /// </summary>
    public class ArenaMatchSpawner : MonoBehaviour
    {
        [Tooltip("A prefab with a NetworkObject and an ArenaMatch.")]
        [SerializeField] private GameObject matchPrefab;

        private NetworkSession _session;

        private void Update()
        {
            // The session may be created after this object, or replaced on a scene reload.
            var session = NetworkSession.Current;
            if (session == _session)
            {
                return;
            }

            if (_session != null)
            {
                _session.Started -= OnStarted;
            }

            _session = session;
            if (_session != null)
            {
                _session.Started += OnStarted;
                if (_session.Role != NetworkRole.Offline)
                {
                    OnStarted(_session.Role);
                }
            }
        }

        private void OnDestroy()
        {
            if (_session != null)
            {
                _session.Started -= OnStarted;
            }
        }

        private void OnStarted(NetworkRole role)
        {
            if (role == NetworkRole.Client || matchPrefab == null || ArenaMatch.Current != null)
            {
                return;
            }

            Instantiate(matchPrefab).GetComponent<NetworkObject>().Spawn(true);
        }
    }
}
