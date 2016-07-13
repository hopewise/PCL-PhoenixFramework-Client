using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

using WebSocketSharp;
using AdvancedTimer.Forms.Plugin.Abstractions;
using Xamarin.Forms;

namespace Phoenix
{
  public class SocketOptions
  {
    public int? Timeout { get; set; }
    public int? HeartbeatIntervalMs { get; set; }
    public Func<int, int> ReconnectAfterMsCallback { get; set; }
    public Action<string, string, JObject> LogCallback { get; set; }
    public JObject Params { get; set; }
  }

  public class Socket
  {
    // Initializes the Socket
    //
    // endPoint - The string WebSocket endpoint, ie, "ws://example.com/ws",
    //                                               "wss://example.com"
    //                                               "/ws" (inherited host & protocol)
    // opts - Optional configuration
    //   transport - The Websocket Transport, for example WebSocket or Phoenix.LongPoll.
    //               Defaults to WebSocket with automatic LongPoll fallback.
    //   timeout - The default timeout in milliseconds to trigger push timeouts.
    //             Defaults `DEFAULT_TIMEOUT`
    //   heartbeatIntervalMs - The millisec interval to send a heartbeat message
    //   reconnectAfterMs - The optional function that returns the millsec
    //                      reconnect interval. Defaults to stepped backoff of:
    //
    //     function(tries){
    //       return [1000, 5000, 10000][tries - 1] || 10000
    //     }
    //
    //   logger - The optional function for specialized logging, ie:
    //     `logger: (kind, msg, data) => { console.log(`${kind}: ${msg}`, data) }
    //
    //   longpollerTimeout - The maximum timeout of a long poll AJAX request.
    //                        Defaults to 20s (double the server long poll timer).
    //
    //   params - The optional params to pass when connecting
    //
    // For IE8 support use an ES5-shim (https://github.com/es-shims/es5-shim)
    //

    private IList<Action> _openCallbacks;
    private IList<Action<bool, string, ushort>> _closeCallbacks;
    private IList<Action<string, Exception>> _errorCallbacks;
    private IList<Action<JObject>> _messageCallbacks;

    //private IList<string> _stateChangeCallbacks;
    private IList<Channel> _channels;

    private IList<Action> _sendBuffer;
    private uint _ref;
    private int _timeout;
    private int _heartbeatIntervalMs;
    private Func<int, int> _reconnectAfterMsCallback;
    private Action<string, string, JObject> _logCallback;
    private JObject _params;
    private string _endpoint;
    private RetryTimer _reconnectTimer;
    private IAdvancedTimer _heartbeatTimer;

    private WebSocket _conn;
    private string _endpointUrl;

    public int Timeout { get { return _timeout; } }
    public Func<int, int> ReconnectAfterMs { get { return _reconnectAfterMsCallback; } }

    //constructor(endPoint, opts = {}){
    //  this.stateChangeCallbacks = {open: [], close: [], error: [], message: []}
    //  this.channels             = []
    //  this.sendBuffer           = []
    //  this.ref                  = 0
    //  this.timeout              = opts.timeout || DEFAULT_TIMEOUT
    //  this.transport            = opts.transport || window.WebSocket || LongPoll
    //  this.heartbeatIntervalMs  = opts.heartbeatIntervalMs || 30000
    //  this.reconnectAfterMs     = opts.reconnectAfterMs || function(tries){
    //    return [1000, 2000, 5000, 10000][tries - 1] || 10000
    //  }
    //  this.logger               = opts.logger || function(){} // noop
    //  this.longpollerTimeout    = opts.longpollerTimeout || 20000
    //  this.params               = opts.params || {}
    //  this.endPoint             = `${endPoint}/${TRANSPORTS.websocket}`
    //  this.reconnectTimer       = new Timer(() => {
    //    this.disconnect(() => this.connect())
    //  }, this.reconnectAfterMs)
    //}

    public Socket(string endpoint, SocketOptions options) :
      this(endpoint,
        options.Timeout.HasValue ? options.Timeout.Value : Phoenix.DEFAULT_TIMEOUT,
        options.HeartbeatIntervalMs.HasValue ? options.HeartbeatIntervalMs.Value : Phoenix.DEFAULT_HEARTBEAT_INTERVAL,
        options.Params,
        options.ReconnectAfterMsCallback,
        options.LogCallback
      )
    { }

    public Socket(string endpoint,
                  int timeout = Phoenix.DEFAULT_TIMEOUT,
                  int heartbeatIntervalMs = Phoenix.DEFAULT_HEARTBEAT_INTERVAL,
                  JObject params_ = null,
                  Func<int, int> reconnectAfterMs = null,
                  Action<string, string, JObject> logger = null)
    {
      _openCallbacks = new List<Action>();
      _closeCallbacks = new List<Action<bool, string, ushort>>();
      _errorCallbacks = new List<Action<string, Exception>>();
      _messageCallbacks = new List<Action<JObject>>();

      _channels = new List<Channel>();
      _sendBuffer = new List<Action>();
      _ref = 0;
      _timeout = timeout;
      //transport
      _heartbeatIntervalMs = heartbeatIntervalMs;
      _reconnectAfterMsCallback = reconnectAfterMs ?? DefaultReconnectAfterMs;

      _logCallback = logger ?? DefaultLogger;
      //longpollerTimeout
      _params = params_ ?? Phoenix.EMPTY_JS_OBJECT;
      _endpoint = $"{endpoint}/{Phoenix.TRANSPORT_WEBSOCKET}";

      _reconnectTimer = new RetryTimer(Reconnect, _reconnectAfterMsCallback);

      _heartbeatTimer = DependencyService.Get<IAdvancedTimer>(); 
	_heartbeatTimer.initTimer(_heartbeatIntervalMs, SendHeartbeat, true);
      

  
    }
    private void Reconnect()
    {
      Disconnect(() => Connect());
    }
    private int DefaultReconnectAfterMs(int numTries)
    {
      var times = new int[4] { 1000, 2000, 5000, 10000 };
      return (numTries > 0 && numTries < times.Length) ? times[numTries - 1] : 10000;
    }
    private void DefaultLogger(string kind, string msg, JObject data) { }

    //protocol(){ return location.protocol.match(/^https/) ? "wss" : "ws" }

    //jfis - user must suppoly protocol in endpoint

    //endPointURL(){
    //  let uri = Ajax.appendParams(
    //    Ajax.appendParams(this.endPoint, this.params), {vsn: VSN})
    //  if(uri.charAt(0) !== "/"){ return uri }
    //  if(uri.charAt(1) === "/"){ return `${this.protocol()}:${uri}` }

    //  return `${this.protocol()}://${location.host}${uri}`
    //}
    private string EndpointUrl()
    {
      if (_endpointUrl != null) return _endpointUrl; //jfis - cached previous value. if _endpoint or _params changes this is a problem

      var uri = Utility.AppendParams(_endpoint, _params);
      var vsn = new JObject(new JProperty("vsn", Phoenix.VSN));
      uri = Utility.AppendParams(uri, vsn);

      //jfis - user must supply protocol
      _endpointUrl = uri;
      return uri;
    }

    //disconnect(callback, code, reason){
    //  if(this.conn){
    //    this.conn.onclose = function(){} // noop
    //    if(code){ this.conn.close(code, reason || "") } else { this.conn.close() }
    //    this.conn = null
    //  }
    //  callback && callback()
    //}
    public void Disconnect(Action callback = null, ushort? code = null, string reason = null)
    {
      if (_conn != null)
      {
        _conn.OnClose -= OnConnClose;

        if (_conn.ReadyState != WebSocketState.Closed)
        {
          if (code != null)
          {
            _conn.Close(code.Value, reason ?? "");
          }
          else
          {
            _conn.Close();
          }
        }

        _conn = null;
      }

      if (callback != null) callback();
    }

    //// params - The params to send when connecting, for example `{user_id: userToken}`
    //connect(params){
    //  if(params){
    //    console && console.log("passing params to connect is deprecated. Instead pass :params to the Socket constructor")
    //    this.params = params
    //  }
    //  if(this.conn){ return }

    //  this.conn = new this.transport(this.endPointURL())
    //  this.conn.timeout   = this.longpollerTimeout
    //  this.conn.onopen    = () => this.onConnOpen()
    //  this.conn.onerror   = error => this.onConnError(error)
    //  this.conn.onmessage = event => this.onConnMessage(event)
    //  this.conn.onclose   = event => this.onConnClose(event)
    //}
    public void Connect()
    {
      if (_conn != null) return; //jfis - necessary?

      _conn = new WebSocket(EndpointUrl()); //jfis - websocket-sharp specific
      //
      _conn.OnOpen += OnConnOpen;
      _conn.OnError += OnConnError;
      _conn.OnMessage += OnConnMessage;
      _conn.OnClose += OnConnClose;

      _conn.Connect();
    }

    //// Logs the message. Override `this.logger` for specialized logging. noops by default
    //log(kind, msg, data){ this.logger(kind, msg, data) }
    internal void Log(string kind, string msg, JObject data = null)
    {
      _logCallback(kind, msg, data);
    }

    //// Registers callbacks for connection state change events
    ////
    //// Examples
    ////
    ////    socket.onError(function(error){ alert("An error occurred") })
    ////
    //onOpen     (callback){ this.stateChangeCallbacks.open.push(callback) }
    public void OnOpen(Action callback)
    {
      _openCallbacks.Add(callback);
    }

    //onClose    (callback){ this.stateChangeCallbacks.close.push(callback) }
    public void OnClose(Action<bool, string, ushort> callback)
    {
      _closeCallbacks.Add(callback);
    }

    //onError    (callback){ this.stateChangeCallbacks.error.push(callback) }
    public void OnError(Action<string, Exception> callback)
    {
      _errorCallbacks.Add(callback);
    }

    //onMessage  (callback){ this.stateChangeCallbacks.message.push(callback) }
    public void OnMessage(Action<JObject> callback)
    {
      _messageCallbacks.Add(callback);
    }

    //onConnOpen(){
    //  this.log("transport", `connected to ${this.endPointURL()}`, this.transport.prototype)
    //  this.flushSendBuffer()
    //  this.reconnectTimer.reset()
    //  if(!this.conn.skipHeartbeat){
    //    clearInterval(this.heartbeatTimer)
    //    this.heartbeatTimer = setInterval(() => this.sendHeartbeat(), this.heartbeatIntervalMs)
    //  }
    //  this.stateChangeCallbacks.open.forEach( callback => callback() )
    //}
    internal void OnConnOpen(object sender, EventArgs e)
    {
      Log("transport", $"connected to { EndpointUrl() }"); //FIX phoenix js doesnt need to build EndpointUrl more than once - added cache
      
      FlushSendBuffer(); //jfis - send anythign buffered when conn opens
      _reconnectTimer.Reset(); //jfis - stop bothering to try reconnecting

      //skipHeartbeat only for longpoll
			_heartbeatTimer.stopTimer(); //jfis - not sure this stop is necessary. can conn be opened while heartbeat enabled?
      _heartbeatTimer.startTimer(); //jfis - connected now so begin sending heartbeats

      foreach (var cb in _openCallbacks) cb(); 
    }

    //onConnClose(event){
    //  this.log("transport", "close", event)
    //  this.triggerChanError()
    //  clearInterval(this.heartbeatTimer)
    //  this.reconnectTimer.setTimeout()
    //  this.stateChangeCallbacks.close.forEach( callback => callback(event) )
    //}
    internal void OnConnClose(object sender, CloseEventArgs e)
    {
      Log("transport", "close");
      
      TriggerChanError(); //jfis - trigger error event on all channels
			_heartbeatTimer.stopTimer(); //jfis - no conn so stop heartbeat
      _reconnectTimer.SetTimeout(); //jfis - no conn so start trying to reconnect

      foreach (var cb in _closeCallbacks) cb(e.WasClean, e.Reason, e.Code);
    }

    //onConnError(error){
    //  this.log("transport", error)
    //  this.triggerChanError()
    //  this.stateChangeCallbacks.error.forEach( callback => callback(error) )
    //}
    internal void OnConnError(object sender, ErrorEventArgs e)
    {
      Log("transport", "error");
      
      TriggerChanError(); //jfis - trigger error event on all channels
      foreach (var cb in _errorCallbacks) cb(e.Message, e.Exception);
    }

    //triggerChanError(){
    //  this.channels.forEach( channel => channel.trigger(CHANNEL_EVENTS.error) )
    //}
    internal void TriggerChanError()
    {
      foreach (var c in _channels) c.Trigger(Phoenix.CHANNEL_EVENT_ERROR);
    }

    //connectionState(){
    //  switch(this.conn && this.conn.readyState){
    //    case SOCKET_STATES.connecting: return "connecting"
    //    case SOCKET_STATES.open:       return "open"
    //    case SOCKET_STATES.closing:    return "closing"
    //    default:                       return "closed"
    //  }
    //}
    internal string ConnectionState()
    {
      if (_conn == null) return Phoenix.CONNECTION_STATE_CLOSED;

      switch (_conn.ReadyState) //jfis - websocket-sharp specific
      {
        case WebSocketState.Open:
          return Phoenix.CONNECTION_STATE_OPEN;

        case WebSocketState.Connecting:
          return Phoenix.CONNECTION_STATE_CONNECTING;

        case WebSocketState.Closing:
          return Phoenix.CONNECTION_STATE_CLOSING;

        default:
          return Phoenix.CONNECTION_STATE_CLOSED;
      }
    }

    //isConnected(){ return this.connectionState() === "open" }
    internal bool IsConnected()
    {
      return ConnectionState() == Phoenix.CONNECTION_STATE_OPEN;
    }

    //remove(channel){
    //  this.channels = this.channels.filter( c => !c.isMember(channel.topic) )
    //}
    internal void Remove(Channel channel)
    {
      _channels = _channels.Where(c => !c.IsMember(channel.Topic)).ToList();
    }

    //channel(topic, chanParams = {}){
    //  let chan = new Channel(topic, chanParams, this)
    //  this.channels.push(chan)
    //  return chan
    //}
    public Channel Channel(string topic, JObject chanParams)
    {
      var chan = new Channel(topic, chanParams, this);
      _channels.Add(chan);
      //jfis - js ppl have strange way of naming things. socket.Channel to makes and adds a channel. not obvious.
      return chan;
    }

    //push(data){
    //  let {topic, event, payload, ref} = data
    //  let callback = () => this.conn.send(JSON.stringify(data))
    //  this.log("push", `${topic} ${event} (${ref})`, payload)
    //  if(this.isConnected()){
    //    callback()
    //  }
    //  else {
    //    this.sendBuffer.push(callback)
    //  }
    //}
    internal void Push(JObject data)
    {
      var dataString = data.ToString(Formatting.None);
      Action callback = () =>
      {
        Log("SENDING", dataString);
        _conn.Send(dataString); //jfis - defer sending in case not currently connected
      };
      
      var topic = (string) data["topic"];
      var event_ = (string) data["event"];
      var payload = (JObject) data["payload"];
      var ref_ = (string) data["ref"];



      Log("push", $"{topic} {event_} ({ref_})", payload);

      if (ref_ == null)
      {
				
        throw new Exception("no ref!");
      }
      if (IsConnected()) //jfis - feels too chaotic, maybe should always go in queue and something else handles queue processing
      {
        callback();
      }
      else
      {
        _sendBuffer.Add(callback);
      }
    }

    //// Return the next message ref, accounting for overflows
    //makeRef(){
    //  let newRef = this.ref + 1
    //  if(newRef === this.ref){ this.ref = 0 } else { this.ref = newRef }

    //  return this.ref.toString()
    //}
    internal string MakeRef()
    {
      _ref++; //uint32 overflow becomes 0
      return _ref.ToString();
    }

    //sendHeartbeat(){ if(!this.isConnected()){ return }
    //  this.push({topic: "phoenix", event: "heartbeat", payload: {}, ref: this.makeRef()})
    //}
    internal void SendHeartbeat(dynamic o, dynamic e)
    {
      if (!IsConnected()) return; //jfis - necessary?

      var data = new JObject();
      data["topic"] = "phoenix";
      data["event"] = "heartbeat";
      data["payload"] = new JObject();
      data["ref"] = MakeRef();
      Push(data);
    }

    //flushSendBuffer(){
    //  if(this.isConnected() && this.sendBuffer.length > 0){
    //    this.sendBuffer.forEach( callback => callback() )
    //    this.sendBuffer = []
    //  }
    //}
    internal void FlushSendBuffer()
    {
      if (IsConnected() && _sendBuffer.Count > 0) //jfis - seems silly to check connected here when only onconnopen calls flush
      { //jfis - also count check doesnt save much
        foreach (var cb in _sendBuffer) cb();
        _sendBuffer.Clear();
      }
    }

    //onConnMessage(rawMessage){
    //  let msg = JSON.parse(rawMessage.data)
    //  let {topic, event, payload, ref} = msg
    //  this.log("receive", `${payload.status || ""} ${topic} ${event} ${ref && "(" + ref + ")" || ""}`, payload)
    //  this.channels.filter( channel => channel.isMember(topic) )
    //               .forEach( channel => channel.trigger(event, payload, ref) )
    //  this.stateChangeCallbacks.message.forEach( callback => callback(msg) )
    //}
    internal void OnConnMessage(object sender, MessageEventArgs e)
    {
      Log("DATA", e.Data);
      //jfis - every reeceived msg comes here
      var msg = JObject.Parse(e.Data);
      var topic = (string)msg["topic"];
      var event_ = (string)msg["event"];
      var payload = (JObject)msg["payload"];
      var ref_ = (string)msg["ref"];

      Log("receive", $"{payload["status"] ?? ""} {topic} {event_} ({ ref_ })", payload);
      //jfis - pass on to appropriate channels
      
			foreach (var item in _channels.Where(c => c.IsMember(topic)))
			{
				item.Trigger(event_, payload, ref_);
			}


			foreach (var cb in _messageCallbacks) cb(msg);
    }
  }
}