namespace OpenTracing.Contrib.DynamicTracing
{
    internal static class KnownTagNames
    {
        public const string Component = "component";

        public const string Error = "error";

        public static class Peer
        {
            public const string Port = "peer.port";
            public const string Address = "peer.address";
            public const string Hostname = "peer.hostname";
            public const string Ipv4 = "peer.ipv4";
            public const string Ipv6 = "peer.ipv6";
            public const string Service = "peer.service";
        }

        public static class Db
        {
            public const string Instance = "db.instance";
            public const string Statement = "db.statement";
            public const string Type = "db.type";
            public const string User = "db.user";
        }

        public static class Http
        {
            public const string Method = "http.method";
            public const string Url = "http.url";
            public const string StatusCode = "http.status_code";
        }

        public static class MessageBus
        {
            public const string Destination = "message_bus.destination";
        }

        public static class Span
        {
            public const string Kind = "span.kind";
        }

        public static class Sampling
        {
            public const string Priority = "sampling.priority";
        }
    }
}