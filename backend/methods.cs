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

    private bool _isTest = false;

    private bool IsTestMode()
    {
        if (_isTest) return true;
        if (CPH.TryGetArg("isTest", out bool isTest) && isTest) return true;
        return false;
    }

    // made by gemini, prepends 'test_' to vars during tests
    private string GetVarKey(string key) => IsTestMode() ? $"test_{key}" : key;

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

        [JsonProperty("isTest", NullValueHandling = NullValueHandling.Ignore)]
        public bool IsTest { get; set; } = false;
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
        var request = CPH.GetTwitchUserVarById<DrawRequest>(userId, GetVarKey("drawRequest"));
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
            CPH.RunAction("Error - Invalid Request");
            return false;
        }

        // Using List instead of Queue so that we can check user positions and arbitrary removal
        List<string> drawQueue = CPH.GetGlobalVar<List<string>>(GetVarKey("drawQueue")) ?? new List<string>();

        if (drawQueue.Contains(userId))
        {
            // update existing request
            DrawRequest existingRequest = CPH.GetTwitchUserVarById<DrawRequest>(userId, GetVarKey("drawRequest")) ?? new DrawRequest();
            existingRequest.Color = color;
            existingRequest.Prompt = prompt;
            existingRequest.UpdateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            CPH.SetTwitchUserVarById(userId, GetVarKey("drawRequest"), existingRequest, !IsTestMode());
            CPH.RunAction("Request Updated");
        }
        else
        {
            // check if already completed
            DrawRequest existingRequest = CPH.GetTwitchUserVarById<DrawRequest>(userId, GetVarKey("drawRequest"));
            if (existingRequest is not null && existingRequest.Completed)
            {
                // request already completed
                CPH.LogInfo("User request already completed");
                CPH.RunAction("Error - Request Already Completed");
                return false;
            }

            // add new request
            DrawRequest newRequest = new DrawRequest
            {
                Color = color,
                Prompt = prompt
                // rest are default
            };

            CPH.SetTwitchUserVarById(userId, GetVarKey("drawRequest"), newRequest, !IsTestMode());

            drawQueue.Add(userId);
            CPH.SetGlobalVar(GetVarKey("drawQueue"), drawQueue, !IsTestMode());
            CPH.RunAction("Request Added");
        }

        BroadcastDrawQueue();
        return true;
    }

    public bool BroadcastDrawQueue()
    {
        List<string> drawQueue = CPH.GetGlobalVar<List<string>>(GetVarKey("drawQueue")) ?? new List<string>();

        return BroadcastQueue(drawQueue, BroadcastEvent.DrawQueueUpdate);
    }

    public bool BroadcastCompletedQueue()
    {
        List<string> completedQueue = CPH.GetGlobalVar<List<string>>(GetVarKey("completedQueue")) ?? [];

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
        List<string> drawQueue = CPH.GetGlobalVar<List<string>>(GetVarKey("drawQueue")) ?? [];
        if (!drawQueue.Contains(userId))
        {
            CPH.LogInfo($"Completing request failed. User {userId} not found in draw queue.");
            return false;
        }        
        drawQueue.Remove(userId);
        CPH.SetGlobalVar(GetVarKey("drawQueue"), drawQueue, !IsTestMode());
        BroadcastDrawQueue();
        
        // completed queue
        List<string> completedQueue = CPH.GetGlobalVar<List<string>>(GetVarKey("completedQueue")) ?? [];
        completedQueue.Add(userId);
        CPH.SetGlobalVar(GetVarKey("completedQueue"), completedQueue, !IsTestMode());
        BroadcastCompletedQueue();

        // mark request as completed
        DrawRequest existingRequest = CPH.GetTwitchUserVarById<DrawRequest>(userId, GetVarKey("drawRequest"));
        if (existingRequest == null)
        {
            CPH.LogInfo($"Completing request failed. User {userId} has no existing draw request.");
            return false;
        }
        existingRequest.Completed = true;
        CPH.SetTwitchUserVarById(userId, GetVarKey("drawRequest"), existingRequest, !IsTestMode());

        return true;
    }

    public bool RejectRequest(string userId)
    {
        CPH.LogInfo($"Rejecting request for user {userId}.");
        
        // drawQueue
        List<string> drawQueue = CPH.GetGlobalVar<List<string>>(GetVarKey("drawQueue")) ?? [];
        if (!drawQueue.Contains(userId))
        {
            CPH.LogInfo($"Rejecting request failed. User {userId} not found in draw queue.");
            return false;
        }        
        drawQueue.Remove(userId);
        CPH.SetGlobalVar(GetVarKey("drawQueue"), drawQueue, !IsTestMode());
        BroadcastDrawQueue();

        // clear request
        CPH.SetTwitchUserVarById(userId, GetVarKey("drawRequest"), null, !IsTestMode());

        return true;
    }

    public bool ClearDrawQueue()
    {
        List<string> drawQueue = CPH.GetGlobalVar<List<string>>(GetVarKey("drawQueue"));

        // clear global var
        CPH.SetGlobalVar(GetVarKey("drawQueue"), new List<string>(), !IsTestMode());
        BroadcastDrawQueue();

        return ClearUserVars(drawQueue);
    }

    public bool ClearCompletedQueue()
    {
        List<string> completedQueue = CPH.GetGlobalVar<List<string>>(GetVarKey("completedQueue"));

        // clear global var
        CPH.SetGlobalVar(GetVarKey("completedQueue"), new List<string>(), !IsTestMode());
        BroadcastCompletedQueue();

        return ClearUserVars(completedQueue);
    }

    private bool ClearUserVars(List<string> userIds)
    {
        if (userIds == null || userIds.Count == 0) return true; // nothing to do

        // user vars first while they're still valid
        foreach (var userId in userIds)
        {
            CPH.SetTwitchUserVarById(userId, GetVarKey("drawRequest"), null, !IsTestMode());
        }
        return true;
    }

    public bool PauseQueue()
    {
        // disable new requests from command
        CPH.SetGlobalVar(GetVarKey("isQueueOpen"), false, !IsTestMode());
        
        var payload = new BroadcastEnvelope
        {
            Target = BroadcastTarget.All,
            Event = BroadcastEvent.PauseQueue
        };

        string payloadJson = JsonConvert.SerializeObject(payload);
        CPH.WebsocketBroadcastJson(payloadJson);

        CPH.RunAction("On Queue Pause");
        return true;
    }

    public bool UnpauseQueue()
    {
        // enable new requests from command
        CPH.SetGlobalVar(GetVarKey("isQueueOpen"), true, !IsTestMode());
    
        var payload = new BroadcastEnvelope
        {
            Target = BroadcastTarget.All,
            Event = BroadcastEvent.UnpauseQueue
        };

        string payloadJson = JsonConvert.SerializeObject(payload);
        CPH.WebsocketBroadcastJson(payloadJson);

        CPH.RunAction("On Queue Unpause");
        return true;
    }

    public bool WebsocketHandler()
    {
        // args
        if
        (
            !CPH.TryGetArg("data", out string data)
        )
        {
            CPH.LogWarn($"WebsocketHandler: Missing payload.");
            return false;
        }

        // parse inbound envelope
        InboundEnvelope? inbound = JsonConvert.DeserializeObject<InboundEnvelope>(data);
        if (inbound == null || inbound.Event == null)
        {
            CPH.LogWarn($"WebsocketHandler: Invalid inbound envelope. Data: {data}");
            return false;
        }

        if (inbound.IsTest)
        {
            _isTest = true;
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