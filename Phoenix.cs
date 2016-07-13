using Newtonsoft.Json.Linq;

namespace Phoenix
{
  internal static class Phoenix
  {
    public const string VSN = "1.0.0";
    //public const enum SOCKET_STATES = { connecting: 0, open: 1, closing: 2, closed: 3 };
    //public const byte SOCKET_STATE_CONNECTING = 0;
    //public const byte SOCKET_STATE_OPEN = 1;
    //public const byte SOCKET_STATE_CLOSING = 2;
    //public const byte SOCKET_STATE_CLOSED = 3;

    public const int DEFAULT_TIMEOUT = 10000;
    public const int DEFAULT_HEARTBEAT_INTERVAL = 30000;

    public const string CHANNEL_EVENT_CLOSE = "phx_close";
    public const string CHANNEL_EVENT_ERROR = "phx_error";
    public const string CHANNEL_EVENT_JOIN = "phx_join";
    public const string CHANNEL_EVENT_REPLY = "phx_reply";
    public const string CHANNEL_EVENT_LEAVE = "phx_leave";

    public const string CONNECTION_STATE_CONNECTING = "connecting";
    public const string CONNECTION_STATE_OPEN = "open";
    public const string CONNECTION_STATE_CLOSING = "closing";
    public const string CONNECTION_STATE_CLOSED = "closed";

    //public const TRANSPORTS = {longpoll: "longpoll", websocket: "websocket"}
    public const string TRANSPORT_WEBSOCKET = "websocket";

    public static readonly JObject EMPTY_JS_OBJECT = new JObject();
  }
}