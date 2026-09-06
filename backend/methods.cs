using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Streamer.bot.Plugin.Interface;
using Streamer.bot.Plugin.Interface.Model;

public class CPHInline
{
    // Declaring CPH here satisfies the compiler and provides full IntelliSense
    public IInlineInvokeProxy CPH { get; set; } = null!;
    
    // required for actions
    public bool Execute() => true;

    public enum BroadcastTarget
    {
        All,
        Controller,
        Viewer
    }

    public enum BroadcastEvent
    {
        DrawQueueUpdate,
        CompletedQueueUpdate,
        PauseQueue,
        UnpauseQueue
    }

    public class BroadcastEnvelope
    {
        [JsonProperty("target")]
        public BroadcastTarget Target { get; set; } = BroadcastTarget.All;

        [JsonProperty("event")]
        public BroadcastEvent Event { get; set; } = BroadcastEvent.DrawQueueUpdate;

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public object? Data { get; set; } = null;
    }

    public enum InboundEvent
    {
        GetDrawQueue,
        GetCompletedQueue,
        CompleteRequest,
        RejectRequest,
        ClearDrawQueue,
        ClearCompletedQueue,
        ClearAllQueues,
        PauseQueue,
        UnpauseQueue
    }

    public class InboundEnvelope
    {
        [JsonProperty("event")]
        public InboundEvent? Event { get; set; } = null;

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public object? Data { get; set; } = null;
    }

    public class DrawRequest
    {
        [JsonProperty("color")]
        public string Color { get; set; } = "#FFFFFF";

        [JsonProperty("prompt")]
        public string Prompt { get; set; } = "";

        [JsonProperty("initialTime")]
        public long InitialTime { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        [JsonProperty("updateTime")]
        public long UpdateTime { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        [JsonProperty("completed")]
        public bool Completed { get; set; } = false;
    }

    public class DrawRequestDetails
    {
        [JsonProperty("userInfo")]
        public TwitchUserInfo UserInfo { get; set; } = new TwitchUserInfo();

        [JsonProperty("request")]
        public DrawRequest Request { get; set; } = new DrawRequest();
    }

    private DrawRequestDetails? GetDetails(string userId)
    {
        var request = CPH.GetTwitchUserVarById<DrawRequest>(userId, "drawRequest");
        if (request == null)
        {
            return null;
        }

        var twitchUser = CPH.TwitchGetUserInfoById(userId);
        if (twitchUser == null)
        {
            return null;
        }

        return new DrawRequestDetails
        {
            UserInfo = twitchUser, 
            Request = request
        };
    }

    public bool AddOrUpdate()
    {
        // args
        if 
        (
            !CPH.TryGetArg("userId", out string userId) ||
            !CPH.TryGetArg("color", out string color) ||
            !CPH.TryGetArg("prompt", out string prompt) 
        ) 
        {
            CPH.LogWarn($"AddOrUpdate: Missing required arguments.");
            return false;
        }

        // Using List instead of Queue so that we can check user positions and arbitrary removal
        List<string> drawQueue = CPH.GetGlobalVar<List<string>>("drawQueue") ?? new List<string>();

        if (drawQueue.Contains(userId))
        {
            // update existing request
            DrawRequest existingRequest = CPH.GetTwitchUserVarById<DrawRequest>(userId, "drawRequest") ?? new DrawRequest();
            existingRequest.Color = color;
            existingRequest.Prompt = prompt;
            existingRequest.UpdateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            CPH.SetTwitchUserVarById(userId, "drawRequest", existingRequest);
        }
        else
        {
            // add new request
            DrawRequest newRequest = new DrawRequest
            {
                Color = color,
                Prompt = prompt
                // rest are default
            };

            CPH.SetTwitchUserVarById(userId, "drawRequest", newRequest);

            drawQueue.Add(userId);
            CPH.SetGlobalVar("drawQueue", drawQueue);
        }

        BroadcastDrawQueue();
        return true;
    }

    public bool BroadcastDrawQueue()
    {
        List<string> drawQueue = CPH.GetGlobalVar<List<string>>("drawQueue") ?? new List<string>();

        return BroadcastQueue(drawQueue, BroadcastEvent.DrawQueueUpdate);
    }

    public bool BroadcastCompletedQueue()
    {
        List<string> completedQueue = CPH.GetGlobalVar<List<string>>("completedQueue") ?? [];

        return BroadcastQueue(completedQueue, BroadcastEvent.CompletedQueueUpdate);
    }

    private bool BroadcastQueue(List<string> userIds, BroadcastEvent eventType = BroadcastEvent.DrawQueueUpdate)
    {
        // convert ordered list to list of details
        List<DrawRequestDetails> drawDetails = [];
        foreach (var userId in userIds)
        {
            var details = GetDetails(userId);
            if (details == null)
            {
                CPH.LogInfo($"BroadcastQueue: User {userId} has no details, skipping.");
                continue;
            }
            drawDetails.Add(details);
        }

        var payload = new BroadcastEnvelope
        {
            Target = BroadcastTarget.All,
            Event = eventType,
            Data = drawDetails
        };

        string payloadJson = JsonConvert.SerializeObject(payload);
        CPH.WebsocketBroadcastJson(payloadJson);

        return true;
    }

    public bool CompleteRequest(string userId)
    {
        CPH.LogInfo($"Completing request for user {userId}.");
        
        // drawQueue
        List<string> drawQueue = CPH.GetGlobalVar<List<string>>("drawQueue") ?? [];
        if (!drawQueue.Contains(userId))
        {
            CPH.LogInfo($"Completing request failed. User {userId} not found in draw queue.");
            return false;
        }        
        drawQueue.Remove(userId);
        CPH.SetGlobalVar("drawQueue", drawQueue);
        BroadcastDrawQueue();
        
        // completed queue
        List<string> completedQueue = CPH.GetGlobalVar<List<string>>("completedQueue") ?? [];
        completedQueue.Add(userId);
        CPH.SetGlobalVar("completedQueue", completedQueue);
        BroadcastCompletedQueue();

        // mark request as completed
        DrawRequest existingRequest = CPH.GetTwitchUserVarById<DrawRequest>(userId, "drawRequest");
        if (existingRequest == null)
        {
            CPH.LogInfo($"Completing request failed. User {userId} has no existing draw request.");
            return false;
        }
        existingRequest.Completed = true;
        CPH.SetTwitchUserVarById(userId, "drawRequest", existingRequest);

        return true;
    }

    public bool RejectRequest(string userId)
    {
        CPH.LogInfo($"Rejecting request for user {userId}.");
        
        // drawQueue
        List<string> drawQueue = CPH.GetGlobalVar<List<string>>("drawQueue") ?? [];
        if (!drawQueue.Contains(userId))
        {
            CPH.LogInfo($"Rejecting request failed. User {userId} not found in draw queue.");
            return false;
        }        
        drawQueue.Remove(userId);
        CPH.SetGlobalVar("drawQueue", drawQueue);
        BroadcastDrawQueue();

        // clear request
        CPH.SetTwitchUserVarById(userId, "drawRequest", null);

        return true;
    }

    public bool ClearDrawQueue()
    {
        List<string> drawQueue = CPH.GetGlobalVar<List<string>>("drawQueue");

        // clear global var
        CPH.SetGlobalVar("drawQueue", new List<string>());
        BroadcastDrawQueue();

        return ClearUserVars(drawQueue);
    }

    public bool ClearCompletedQueue()
    {
        List<string> completedQueue = CPH.GetGlobalVar<List<string>>("completedQueue");

        // clear global var
        CPH.SetGlobalVar("completedQueue", new List<string>());
        BroadcastCompletedQueue();

        return ClearUserVars(completedQueue);
    }

    private bool ClearUserVars(List<string> userIds)
    {
        if (userIds == null || userIds.Count == 0) return true; // nothing to do

        // user vars first while they're still valid
        foreach (var userId in userIds)
        {
            CPH.SetTwitchUserVarById(userId, "drawRequest", null);
        }
        return true;
    }

    public bool PauseQueue()
    {
        // disable new requests from command
        CPH.SetGlobalVar("isQueueOpen", false);
        
        var payload = new BroadcastEnvelope
        {
            Target = BroadcastTarget.All,
            Event = BroadcastEvent.PauseQueue
        };

        string payloadJson = JsonConvert.SerializeObject(payload);
        CPH.WebsocketBroadcastJson(payloadJson);

        return true;
    }

    public bool UnpauseQueue()
    {
        // enable new requests from command
        CPH.SetGlobalVar("isQueueOpen", true);
    
        var payload = new BroadcastEnvelope
        {
            Target = BroadcastTarget.All,
            Event = BroadcastEvent.UnpauseQueue
        };

        string payloadJson = JsonConvert.SerializeObject(payload);
        CPH.WebsocketBroadcastJson(payloadJson);

        return true;
    }

    public bool WebsocketHandler()
    {
        // args
        if
        (
            CPH.TryGetArg("data", out string data)
        )
        {
            CPH.LogWarn($"WebsocketHandler: Missing data.");
            return false;
        }

        // parse inbound envelope
        InboundEnvelope? inbound = JsonConvert.DeserializeObject<InboundEnvelope>(data);
        if (inbound == null || inbound.Event == null)
        {
            CPH.LogWarn($"WebsocketHandler: Invalid inbound envelope. Data: {data}");
            return false;
        }

        // handle inbound event
        switch (inbound.Event)
        {
            case InboundEvent.GetDrawQueue:
                BroadcastDrawQueue();
                break;
            case InboundEvent.GetCompletedQueue:
                BroadcastCompletedQueue();
                break;
            case InboundEvent.CompleteRequest:
                if (inbound.Data is string completeUserId)
                {
                    CompleteRequest(completeUserId);
                }
                break;
            case InboundEvent.RejectRequest:
                if (inbound.Data is string rejectUserId)
                {
                    RejectRequest(rejectUserId);
                }
                break;
            case InboundEvent.ClearDrawQueue:
                ClearDrawQueue();
                break;
            case InboundEvent.ClearCompletedQueue:
                ClearCompletedQueue();
                break;
            case InboundEvent.ClearAllQueues:
                ClearDrawQueue();
                ClearCompletedQueue();
                break;
            case InboundEvent.PauseQueue:
                PauseQueue();
                break;
            case InboundEvent.UnpauseQueue:
                UnpauseQueue();
                break;
            default:
                CPH.LogWarn($"WebsocketHandler: Unknown inbound event: {inbound.Event}");
                return false;
        }

        return true;
    }
}