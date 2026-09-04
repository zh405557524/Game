using System;

namespace ProjectRealm.Foundation
{
    /// <summary>Framework 对外返回的稳定错误类别；UI 不需要解析异常文本。</summary>
    public enum RealmErrorKind
    {
        None,
        Validation,
        NotFound,
        Conflict,
        Unavailable,
        Persistence,
        Compatibility,
        Fatal
    }

    /// <summary>跨 Framework 边界传递的不可变错误。</summary>
    public sealed class RealmError
    {
        public RealmError(string code, string message, RealmErrorKind kind)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ArgumentException("A stable error code is required.", nameof(code));
            }

            Code = code;
            Message = message ?? string.Empty;
            Kind = kind;
        }

        public string Code { get; }
        public string Message { get; }
        public RealmErrorKind Kind { get; }
    }

    /// <summary>不携带值的操作结果。</summary>
    public sealed class RealmResult
    {
        private RealmResult(bool succeeded, RealmError error)
        {
            Succeeded = succeeded;
            Error = error;
        }

        public bool Succeeded { get; }
        public RealmError Error { get; }

        public static RealmResult Success() => new RealmResult(true, null);

        public static RealmResult Failure(string code, string message, RealmErrorKind kind) =>
            new RealmResult(false, new RealmError(code, message, kind));
    }

    /// <summary>携带不可变返回值的操作结果。</summary>
    public sealed class RealmResult<T>
    {
        private RealmResult(bool succeeded, T value, RealmError error)
        {
            Succeeded = succeeded;
            Value = value;
            Error = error;
        }

        public bool Succeeded { get; }
        public T Value { get; }
        public RealmError Error { get; }

        public static RealmResult<T> Success(T value) => new RealmResult<T>(true, value, null);

        public static RealmResult<T> Failure(string code, string message, RealmErrorKind kind) =>
            new RealmResult<T>(false, default(T), new RealmError(code, message, kind));
    }
}
