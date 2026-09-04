using System;
using System.Collections.Generic;
using System.IO;
using ProjectRealm.Foundation;

namespace ProjectRealm.SystemServer
{
    internal static class RealmErrorMapper
    {
        public static RealmError FromException(Exception exception)
        {
            if (exception is FileNotFoundException)
            {
                return new RealmError("save_not_found", exception.Message, RealmErrorKind.NotFound);
            }

            if (exception is KeyNotFoundException)
            {
                return new RealmError("definition_not_found", exception.Message, RealmErrorKind.NotFound);
            }

            if (exception is InvalidDataException)
            {
                return new RealmError("save_corrupt", exception.Message, RealmErrorKind.Persistence);
            }

            if (exception is IOException)
            {
                return new RealmError("persistence_io_failed", exception.Message, RealmErrorKind.Persistence);
            }

            if (exception is ArgumentException)
            {
                return new RealmError("invalid_request", exception.Message, RealmErrorKind.Validation);
            }

            if (exception is InvalidOperationException &&
                (exception.Message.IndexOf("Definition", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 exception.Message.IndexOf("schema", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 exception.Message.IndexOf("checkpoint", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return new RealmError("definition_or_save_incompatible", exception.Message, RealmErrorKind.Compatibility);
            }

            return new RealmError("framework_fault", exception.Message, RealmErrorKind.Fatal);
        }
    }
}
