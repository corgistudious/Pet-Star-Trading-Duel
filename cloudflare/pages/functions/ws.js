import { proxy, json } from "./_proxy.js";

// /ws -> backend realtime WebSocket (single-use ticket in the query string)
export const onRequest = (context) => {
  if ((context.request.headers.get("upgrade") || "").toLowerCase() !== "websocket") return json(426, "websocket_required");
  return proxy(context);
};
