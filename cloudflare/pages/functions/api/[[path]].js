import { proxy } from "../_proxy.js";

// /api/* -> backend (REST: login, pets/cards, PvP, market, sync ...)
export const onRequest = (context) => proxy(context);
