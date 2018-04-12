namespace OpenTracing.Contrib.DynamicTracing
{
    internal static class KnownLogFieldNames
    {
        public const string Event = "event";
        public const string Message = "message";
        public const string Stack = "stack";

        public static class Error
        {
            public const string Object = "error.object";
            public const string Kind = "error.kind";
        }
    }
}