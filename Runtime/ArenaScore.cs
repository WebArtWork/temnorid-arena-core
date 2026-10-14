using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Temnorid.Arena.Core
{
    /// <summary>One line of the scoreboard.</summary>
    public struct ArenaScore : INetworkSerializable, IEquatable<ArenaScore>
    {
        public FixedString32Bytes Id;
        public FixedString64Bytes Name;
        public int Kills;
        public int Deaths;
        public bool IsBot;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref Name);
            serializer.SerializeValue(ref Kills);
            serializer.SerializeValue(ref Deaths);
            serializer.SerializeValue(ref IsBot);
        }

        public bool Equals(ArenaScore other)
        {
            return Id.Equals(other.Id) && Name.Equals(other.Name) && Kills == other.Kills && Deaths == other.Deaths && IsBot == other.IsBot;
        }
    }

    /// <summary>Where combatants can (re)appear. The map provides it through <c>EngineServices</c>.</summary>
    public interface IArenaSpawns
    {
        IReadOnlyList<Pose> SpawnPoints { get; }
    }

    /// <summary>Temnorid Arena's game events in 3d-network-schema session messages.</summary>
    public static class ArenaEvents
    {
        /// <summary>Payload: <see cref="DefeatedPayload"/>.</summary>
        public const string PlayerDefeated = "temnorid-arena.player-defeated";

        public const string Mode = "temnorid-arena.deathmatch";

        public class DefeatedPayload
        {
            public string playerId;
            public string byPlayerId;
            public string cause;
        }
    }
}
