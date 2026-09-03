using System;

namespace ProjectRealm.Domain
{
    public readonly struct WorldSeed : IEquatable<WorldSeed>
    {
        public WorldSeed(long value)
        {
            Value = value;
        }

        public long Value { get; }

        public bool Equals(WorldSeed other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is WorldSeed other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value.ToString();
        }
    }
}
