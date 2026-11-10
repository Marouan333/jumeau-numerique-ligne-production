// Pont WebSocket pour les builds WebGL. Les evenements du navigateur sont
// renvoyes vers le GameObject Unity indique a l'ouverture (TwinClient).
var WsBridge = {
  $wsState: { sock: null, obj: null },

  WsOpen: function (urlPtr, objPtr) {
    var url = UTF8ToString(urlPtr);
    wsState.obj = UTF8ToString(objPtr);
    function tell(method, arg) {
      var send = (typeof SendMessage === 'function') ? SendMessage : Module.SendMessage;
      send(wsState.obj, method, arg);
    }
    try {
      if (wsState.sock) { wsState.sock.onclose = null; wsState.sock.close(); }
      var s = new WebSocket(url);
      wsState.sock = s;
      s.onopen = function () { tell('JsOnOpen', ''); };
      s.onmessage = function (e) { tell('JsOnMessage', e.data); };
      s.onclose = function (e) { tell('JsOnClose', String(e.code)); };
      s.onerror = function () { tell('JsOnError', 'erreur WebSocket'); };
    } catch (err) {
      tell('JsOnError', String(err));
    }
  },

  WsSend: function (msgPtr) {
    var s = wsState.sock;
    if (s && s.readyState === 1) s.send(UTF8ToString(msgPtr));
  },

  WsClose: function () {
    var s = wsState.sock;
    if (s) { s.onclose = null; s.close(); wsState.sock = null; }
  },

  // Clic sur une machine : prevenir la page (dashboard).
  TwinStationClicked: function (station) {
    if (typeof window !== 'undefined' && typeof window.twinOnStationSelected === 'function') {
      window.twinOnStationSelected(station);
    }
  }
};

autoAddDeps(WsBridge, '$wsState');
mergeInto(LibraryManager.library, WsBridge);
