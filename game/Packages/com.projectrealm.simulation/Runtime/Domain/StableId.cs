using System;

namespace ProjectRealm.Domain
{
    public readonly struct StableId : IEquatable<StableId>
    {
        public StableId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A stable ID cannot be empty.", nameof(value));
            }

            for (var index = 0; index < value.Length; index++)
            {
                if (char.IsWhiteSpace(value[index]))
                {
                    throw new ArgumentException("A stable ID cannot contain whitespace.", nameof(value));
                }
            }

            Value = value;
        }

        public string Value { get; }

        public bool Equals(StableId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is StableId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value ?? string.Empty;
        }
    }
}
