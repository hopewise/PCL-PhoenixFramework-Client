using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedTimer.Forms.Plugin.Abstractions;
using Xamarin.Forms;

namespace Phoenix
{
  public class Push
  {
    private class ReceiveHook
    {
      internal string Status { get; set; }
      internal Action<JObject> Callback { get; set; }
    }

    //// Initializes the Push
    ////
    //// channel - The Channel
    //// event - The event, for example `"phx_join"`
    //// payload - The payload, for example `{user_id: 123}`
    //// timeout - The push timeout in milliseconds
    ////
    //constructor(channel, event, payload, timeout){
    //  this.channel = channel
    //  this.event        = event
    //  this.payload      = payload || { }
    //  this.receivedResp = null
    //  this.timeout      = timeout
    //  this.timeoutTimer = null
    //  this.recHooks     = []
    //  this.sent         = false
    //}
    private Channel _channel;

    private string _event;
    private JObject _payload;
    private int _timeout;
    private IAdvancedTimer _timeoutTimer;
    private IList<ReceiveHook> _recHooks;
    //private bool _sent; //FIX phoenix doesnt actually use this var

    private JObject _receivedResp;

    private string _ref;
    private string _refEvent;

    public Push(Channel channel, string event_, JObject payload, int timeout)
    { //jfis - things seem too tied together. do pushes really need to know which channel they belong to? do channels need to know socket?
      _channel = channel;
      _event = event_;
      _payload = payload ?? Phoenix.EMPTY_JS_OBJECT;
      _receivedResp = null;
      _timeout = timeout;

      _timeoutTimer = DependencyService.Get<IAdvancedTimer>();
	  _timeoutTimer.initTimer(_timeout, timerElapsed, false);

		// we need it to be stopped by default
		_timeoutTimer.stopTimer();


      _recHooks = new List<ReceiveHook>();
      //_sent = false;
    }

	void timerElapsed(object sender, EventArgs e)
	{
			Trigger("timeout", Phoenix.EMPTY_JS_OBJECT);
	}


		//resend(timeout){
		//  this.timeout = timeout
		//  this.cancelRefEvent()
		//  this.ref          = null
		//  this.refEvent = null
		//  this.receivedResp = null
		//  this.sent = false
		//  this.send()
		//}

	public void Resend(int timeout)
    {
      _channel.Socket.Log("push resend", _ref);
      _timeout = timeout;

      CancelRefEvent();

      _ref = null;
      _refEvent = null;
      _receivedResp = null;
      //_sent = false;

      Send();
    }

    //send(){
    // if(this.hasReceived("timeout")){ return }
    //  this.startTimeout()
    //  this.sent = true
    //  this.channel.socket.push({
    //    topic: this.channel.topic,
    //    event: this.event,
    //    payload: this.payload,
    //    ref: this.ref
    //  })
    //}
    public void Send()
    {
      _channel.Socket.Log("push send", _ref);
      if (HasReceived("timeout")) return;

      StartTimeout();

      //_sent = true;

      _channel.Socket.Log("push send", _ref);

      var data = new JObject();
      data["topic"] = _channel.Topic;
      data["event"] = _event;
      data["payload"] = _payload;
	  data["ref"] = _ref;

      _channel.Socket.Push(data);
    }

    //receive(status, callback){
    //  if(this.hasReceived(status)){
    //    callback(this.receivedResp.response)
    //  }

    //  this.recHooks.push({status, callback})
    //  return this
    //}
    public Push Receive(string status, Action<JObject> callback)
    {
      if (HasReceived(status)) callback((JObject)_receivedResp["response"]);

      _recHooks.Add(new ReceiveHook() { Status = status, Callback = callback });
      return this;
    }

    //// private

    //matchReceive({status, response, ref}){
    //  this.recHooks.filter( h => h.status === status )
    //               .forEach( h => h.callback(response) )
    //}
    private void MatchReceive(JObject json)
    {
      var status = (string)json["status"];
      var response = (JObject)json["response"];
      //var ref_ = json["ref"].ToString();

			foreach (var item in _recHooks.Where((h) => h.Status == status))
			{
				item.Callback(response);
			}
        
    }

    //cancelRefEvent(){ if(!this.refEvent){ return }
    //  this.channel.off(this.refEvent)
    //}
    private void CancelRefEvent()
    {
      if (_refEvent == null) return;

      _channel.Off(_refEvent);
    }

    //cancelTimeout(){
    //  clearTimeout(this.timeoutTimer)
    //  this.timeoutTimer = null
    //}
    private void CancelTimeout()
    {
      _timeoutTimer.stopTimer();
    }

    //startTimeout(){ if(this.timeoutTimer){ return }
    //  this.ref      = this.channel.socket.makeRef()
    //  this.refEvent = this.channel.replyEventName(this.ref)

    //  this.channel.on(this.refEvent, payload => {
    //    this.cancelRefEvent()
    //    this.cancelTimeout()
    //    this.receivedResp = payload
    //    this.matchReceive(payload)
    //  })

    //  this.timeoutTimer = setTimeout(() => {
    //    this.trigger("timeout", {})
    //  }, this.timeout)
    //}
    internal void StartTimeout()
    {
			if (_timeoutTimer.isTimerEnabled())
			{
				return; //jfis - dont do if timer running
			}

      _ref = _channel.Socket.MakeRef(); //jfis - get new ref
      _refEvent = _channel.ReplyEventName(_ref); //jfis - ref as string

      _channel.On(_refEvent, (payload, _) => //make binding for refEvent
        {
          CancelRefEvent();
          CancelTimeout();
          var json = (JObject)payload;
          _receivedResp = json;

          MatchReceive(json);
        });

			_timeoutTimer.setInterval(_timeout);
      _timeoutTimer.startTimer(); //start timeout timer, which triggers "timeout"
    }

    //hasReceived(status){
    //  return this.receivedResp && this.receivedResp.status === status
    //}
    private bool HasReceived(string status)
    {
      return (_receivedResp != null) && (string)_receivedResp["status"] == status;
    }

    //trigger(status, response){
    //  this.channel.trigger(this.refEvent, {status, response})
    //}
    internal void Trigger(string status, JObject response)
    {
      var data = new JObject();
      data["status"] = status;
      data["response"] = response;
      _channel.Trigger(_refEvent, data);
    }
  }
}