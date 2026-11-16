// Browser WebSocket bridge for Unity WebGL (System.Net.WebSockets is not available there).
// Unity calls PetStarWsOpen/PetStarWsClose; events come back through SendMessage on the given GameObject:
//   OnWsOpen(""), OnWsMessage(json), OnWsClosed(""). A "ping" is sent every 20 s (server answers "pong").
mergeInto(LibraryManager.library, {
  PetStarWsOpen: function (urlPtr, goPtr) {
    var url = UTF8ToString(urlPtr), go = UTF8ToString(goPtr);
    var st = (window.__petstarWs = window.__petstarWs || {});
    try { if (st.ws) { st.ws.onclose = null; st.ws.close(); } } catch (e) {}
    if (st.ping) clearInterval(st.ping);
    var ws = new WebSocket(url);
    st.ws = ws;
    ws.onopen = function () {
      SendMessage(go, 'OnWsOpen', '');
      st.ping = setInterval(function () { if (ws.readyState === 1) ws.send('ping'); }, 20000);
    };
    ws.onmessage = function (e) { if (typeof e.data === 'string' && e.data !== 'pong') SendMessage(go, 'OnWsMessage', e.data); };
    ws.onclose = function () { if (st.ping) clearInterval(st.ping); if (st.ws === ws) st.ws = null; SendMessage(go, 'OnWsClosed', ''); };
    ws.onerror = function () { /* onclose follows */ };
  },
  PetStarWsClose: function () {
    var st = window.__petstarWs;
    if (!st) return;
    if (st.ping) clearInterval(st.ping);
    try { if (st.ws) { st.ws.onclose = null; st.ws.close(); } } catch (e) {}
    st.ws = null;
  }
});
