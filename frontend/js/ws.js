import { eventHandlers } from './queue.js';

const WS_URL = 'ws://127.0.0.1:9090/';

/** @type {WebSocket} */
let socket;
let broadcastCallback = null;

export function connectWebSocket() {
    socket = new WebSocket(WS_URL);

    socket.addEventListener('open', onOpen);
    socket.addEventListener('close', onClose);
    socket.addEventListener('error', onError);

    socket.addEventListener('message', (event) => {
        try {
            // convert to json before passing
            const payload = JSON.parse(event.data);
            onBroadcast(payload);
            } catch (err) {
            console.error('Failed to parse incoming WS message:', err);
        }
    });
}

function onOpen() {
    console.log(`Connected to Streamer Bot on ${socket.url}`);
    // Request initial queue state on connect/reconnect
    sendMessage('GetDrawQueue');
    sendMessage('GetCompletedQueue');
}

function onClose() {
    console.warn('Disconnected. Attempting to reconnect in 2 seconds...');
    setTimeout(connectWebSocket, 2000);
}

/**
 * @param {any} err
 */
function onError(err) {
    console.error('WebSocket encountered error:', err);
    socket.close();
}

/** @enum {number} */
export const BroadcastedTarget = {
    All: 0,
    Controller: 1,
    Viewer: 2,
}

// default role
let CURRENT_ROLE = BroadcastedTarget.All;

/** @param {BroadcastedTarget} role */
export function initRole(role) {
    CURRENT_ROLE = role;
}

/** @enum {number} */
export const BroadcastedEvent = {
    DrawQueueUpdate: 0,
    CompletedQueueUpdate: 1,
    PauseQueue: 2,
    UnpauseQueue: 3,
}

/**
 * @param {object} payload
 * @param {BroadcastedTarget} payload.target - The broadcast target.
 * @param {BroadcastedEvent} payload.event - The event type.
 * @param {object} payload.data - The event payload data.
 */
function onBroadcast(payload) {
    const { target, event, data } = payload;
    if(target !== BroadcastedTarget.All && target !== CURRENT_ROLE) {
        // don't do anything
        return;
    }
    
    const handler = eventHandlers[event];
    if (handler) {
        handler(data);
    } else {
        console.warn(`No handler for event: ${event}`);
    }
}

export const SendEvent = {
    GetDrawQueue: 'GetDrawQueue',
    GetCompletedQueue: 'GetCompletedQueue',
    CompleteRequest: 'CompleteRequest',
    RejectRequest: 'RejectRequest',
    ClearDrawQueue: 'ClearDrawQueue',
    ClearCompletedQueue: 'ClearCompletedQueue',
    ClearAllQueues: 'ClearAllQueues',
    PauseQueue: 'PauseQueue',
    UnpauseQueue: 'UnpauseQueue',
};


/**
 * @param {string} event
 * @param {any} [data]
 */
export async function sendMessage(event, data) {
    if (!socket || socket.readyState !== WebSocket.OPEN) {
        console.warn(`Cannot send event ${event}: socket is not connected.`);
        return;
    }

    const envelope = {
        event: event,
        data: data
    };

    socket.send(JSON.stringify(envelope))
}