using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Phoenix
{
  public class Channel
  {
    private enum ChannelState
    {
      Closed,
      Errored,
      Joined,
      Joining,
    }

    private class Binding
    {
      public string Event { get; }
      public Action<JObject, string> Callback { get; }

      public Binding(string event_, Action<JObject, string> callback)
      {
        Event = event_;
        Callback = callback;
      }
    }

    private ChannelState _state;
    private string _topic;
    private JObject _params;
    private Socket _socket;
    private IList<Binding> _bindings;
    private int _timeout;
    private bool _joinedOnce;
    private Push _joinPush;
    private IList<Push> _pushBuffer;
    private RetryTimer _rejoinTimer;

    public string Topic { get { return _topic; } }
    public Socket Socket { get { return _socket; } }

    //constructor(topic, params, socket) {
    //  this.state       = CHANNEL_STATES.closed
    //  this.topic       = topic
    //  this.params      = params || {}
    //  this.socket      = socket
    //  this.bindings    = []
    //  this.timeout     = this.socket.timeout
    //  this.joinedOnce  = false
    //  this.joinPush    = new Push(this, CHANNEL_EVENTS.join, this.params, this.timeout)
    //  this.pushBuffer  = []
    //  this.rejoinTimer  = new Timer(
    //    () => this.rejoinUntilConnected(),
    //    this.socket.reconnectAfterMs
    //  )
    //  this.joinPush.receive("ok", () => {
    //    this.state = CHANNEL_STATES.joined
    //    this.rejoinTimer.reset()
    //    this.pushBuffer.forEach( pushEvent => pushEvent.send() )
    //    this.pushBuffer = []
    //  })
    //  this.onClose( () => {
    //    this.socket.log("channel", `close ${this.topic}`)
    //    this.state = CHANNEL_STATES.closed
    //    this.socket.remove(this)
    //  })
    //  this.onError( reason => {
    //    this.socket.log("channel", `error ${this.topic}`, reason)
    //    this.state = CHANNEL_STATES.errored
    //    this.rejoinTimer.setTimeout()
    //  })
    //  this.joinPush.receive("timeout", () => {
    //    if(this.state !== CHANNEL_STATES.joining){ return }

    //    this.socket.log("channel", `timeout ${this.topic}`, this.joinPush.timeout)
    //    this.state = CHANNEL_STATES.errored
    //    this.rejoinTimer.setTimeout()
    //  })
    //  this.on(CHANNEL_EVENTS.reply, (payload, ref) => {
    //    this.trigger(this.replyEventName(ref), payload)
    //  })
    //}
    public Channel(string topic, JObject params_, Socket socket)
    {
      _state = ChannelState.Closed;
      _topic = topic;
      _params = params_ ?? Phoenix.EMPTY_JS_OBJECT;
      _socket = socket;

      _bindings = new List<Binding>();
      _timeout = _socket.Timeout;
      _joinedOnce = false;
      _joinPush = new Push(this, Phoenix.CHANNEL_EVENT_JOIN, _params, _timeout);
      _pushBuffer = new List<Push>();

      _rejoinTimer = new RetryTimer(RejoinUntilConnected, _socket.ReconnectAfterMs); //jfis - why another timer instead of waiting for socket event?

      _joinPush.Receive("ok",
        (_) =>
        {
          _socket.Log("JP REC OK", "");
          _state = ChannelState.Joined;
          _rejoinTimer.Reset();
          foreach (var p in _pushBuffer) p.Send();
          _pushBuffer.Clear();
        }
      );
			
      OnClose(() =>
        {
          _socket.Log("channel", $"close {_topic}");

          _state = ChannelState.Closed;
          _socket.Remove(this);
        });

      OnError(
        () => //reason only used for logging
        {
          _socket.Log("channel", $"error {_topic}"); //, reason);

          _state = ChannelState.Errored;
          _rejoinTimer.SetTimeout();
        }
      );

      _joinPush.Receive("timeout",
        (_) =>
        {
          if (_state == ChannelState.Joining) return;

          _socket.Log("channel", $"timeout {_topic}");//, _joinPush.timeout)

          _state = ChannelState.Errored;
          _rejoinTimer.SetTimeout();
        }
      );

      On(Phoenix.CHANNEL_EVENT_REPLY, OnReply);
    }
    private void OnReply(JObject payload, string ref_)
    {
      Trigger(ReplyEventName(ref_), payload);
    }

    //rejoinUntilConnected(){
    //  this.rejoinTimer.setTimeout()
    //  if(this.socket.isConnected()){
    //    this.rejoin()
    //  }
    //}
    public void RejoinUntilConnected()
    {
      _socket.Log("timer", "chan rejoin");
      _rejoinTimer.SetTimeout();
      if (_socket.IsConnected()) //jfis - instead of checking, socket should tell channel
      {
        Rejoin();
      }
    }

    //join(timeout = this.timeout){
    //  if(this.joinedOnce){
    //    throw(`tried to join multiple times. 'join' can only be called a single time per channel instance`)
    //  } else {
    //    this.joinedOnce = true
    //  }
    //  this.rejoin(timeout)
    //  return this.joinPush
    //}
    public Push Join() => Join(_timeout);

    public Push Join(int timeout)
    {
      if (_joinedOnce) //jfis - necessary? can probably just ignore subsequent times
      {
        //return;
        throw new Exception("tried to join multiple times. 'join' can only be called a single time per channel instance");
      }

      _joinedOnce = true;
      Rejoin(timeout);
      return _joinPush;
    }

    //onClose(callback){ this.on(CHANNEL_EVENTS.close, callback) }
    public void OnClose(Action callback)
    {
      On(Phoenix.CHANNEL_EVENT_CLOSE, callback);
    }

    //onError(callback){
    //  this.on(CHANNEL_EVENTS.error, reason => callback(reason) )
    //}
    public void OnError(Action callback)
    {
      On(Phoenix.CHANNEL_EVENT_ERROR, () => callback());
    }

    //on(event, callback){ this.bindings.push({event, callback}) }
    public void On(string event_, Action callback)
    {
      _bindings.Add(new Binding(event_, (_, __) => callback()));
    }

    public void On(string event_, Action<JObject, string> callback)
    {
      _bindings.Add(new Binding(event_, callback));
    }

    //off(event){ this.bindings = this.bindings.filter( bind => bind.event !== event ) }
    public void Off(string event_)
    {
      _bindings = _bindings.Where(b => b.Event != event_).ToList();
    }

    //canPush(){ return this.socket.isConnected() && this.state === CHANNEL_STATES.joined }
    public bool CanPush()
    {
      return _socket.IsConnected() && _state == ChannelState.Joined; //jfis - another _socket call
    }

    //push(event, payload, timeout = this.timeout){
    //  if(!this.joinedOnce){
    //    throw(`tried to push '${event}' to '${this.topic}' before joining. Use channel.join() before pushing events`)
    //  }
    //  let pushEvent = new Push(this, event, payload, timeout)
    //  if(this.canPush()){
    //    pushEvent.send()
    //  } else {
    //    pushEvent.startTimeout()
    //    this.pushBuffer.push(pushEvent)
    //  }

    //  return pushEvent
    //}
    public Push Push(string event_, JObject payload) => Push(event_, payload, _timeout);

    public Push Push(string event_, JObject payload, int timeout)
    {
      if (!_joinedOnce) //jfis - necessary?
      {
        throw new Exception($"tried to push '{event_}' to '{_topic}' before joining. Use channel.join() before pushing events");
      }

      var pushEvent = new Push(this, event_, payload, timeout);

      if (CanPush())
      {
        pushEvent.Send(); //jfis - send now if can
      }
      else
      {
        pushEvent.StartTimeout(); //jfis - if cant add to buffer, but what does start timeout do? 
        _pushBuffer.Add(pushEvent);
      }

      return pushEvent;
    }

    //// Leaves the channel
    ////
    //// Unsubscribes from server events, and
    //// instructs channel to terminate on server
    ////
    //// Triggers onClose() hooks
    ////
    //// To receive leave acknowledgements, use the a `receive`
    //// hook to bind to the server ack, ie:
    ////
    ////     channel.leave().receive("ok", () => alert("left!") )
    ////
    //leave(timeout = this.timeout){
    //  let onClose = () => {
    //    this.socket.log("channel", `leave ${this.topic}`)
    //    this.trigger(CHANNEL_EVENTS.close, "leave")
    //  }
    //  let leavePush = new Push(this, CHANNEL_EVENTS.leave, {}, timeout)
    //  leavePush.receive("ok", () => onClose() )
    //           .receive("timeout", () => onClose() )
    //  leavePush.send()
    //  if(!this.canPush()){ leavePush.trigger("ok", {}) }

    //  return leavePush
    //}
    public Push Leave() => Leave(_timeout);

    public Push Leave(int timeout)
    {
      Action onClose = () =>
        {
          _socket.Log("channel", $"leave ${_topic}"); //jfis - seems odd to tie logs to socket

          Trigger(Phoenix.CHANNEL_EVENT_CLOSE);//, "leave");
        };

      var leavePush = new Push(this, Phoenix.CHANNEL_EVENT_LEAVE, Phoenix.EMPTY_JS_OBJECT, timeout);
      leavePush
        .Receive("ok", (_) => onClose())
        .Receive("timeout", (_) => onClose());
      leavePush.Send();

      if (!CanPush()) //jfis - if cant push simulate ok
      {
        leavePush.Trigger("ok", Phoenix.EMPTY_JS_OBJECT);
      }

      return leavePush;
    }

    //// Overridable message hook
    ////
    //// Receives all events for specialized message handling
    //onMessage(event, payload, ref){}
    public void OnMessage(string event_, JObject payload, string ref_)
    {
      //??? TODO 
      //jfis - can probably just remove this func
      payload = payload ?? Phoenix.EMPTY_JS_OBJECT;
      _socket.Log($"{event_} | {ref_}", payload.ToString(Newtonsoft.Json.Formatting.None));
    }

    //// private

    //isMember(topic){ return this.topic === topic }
    public bool IsMember(string topic)
    {
      return _topic == topic; //jfis - is a channel a "member" of a topic? seems like wrong wording
    }

    //sendJoin(timeout){
    //  this.state = CHANNEL_STATES.joining
    //  this.joinPush.resend(timeout)
    //}
    private void SendJoin(int timeout)
    {
      _state = ChannelState.Joining;
      _joinPush.Resend(timeout);
    }

    //rejoin(timeout = this.timeout){ this.sendJoin(timeout) }
    private void Rejoin() => Rejoin(_timeout);

    private void Rejoin(int timeout)
    {
      SendJoin(timeout); //jfis - rejoin is only called once, from rejoin until connected. and always with no params. then only calls sendjoin. seems convoluted. why not call sendJoin?
    }

    //trigger(triggerEvent, payload, ref){
    //  this.onMessage(triggerEvent, payload, ref)
    //  this.bindings.filter( bind => bind.event === triggerEvent )
    //               .map( bind => bind.callback(payload, ref) )
    //}
    internal void Trigger(string triggerEvent, JObject payload = null, string ref_ = null)
    {
      OnMessage(triggerEvent, payload, ref_); //jfis - this might only be valuable in js land

				
			foreach (var item in _bindings.Where(b => b.Event == triggerEvent))
		{
			item.Callback(payload, ref_);
		}
				
    }

    //replyEventName(ref){ return `chan_reply_${ref}` }
    internal string ReplyEventName(string ref_)
    {
      return $"chan_reply_{ref_}";
    }
  }
}