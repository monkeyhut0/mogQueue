using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public class CPHInline
{
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
        Clear
    }

    public record BroadcastEnvelope(
        [property: JsonProperty("target")] BroadcastTarget Target,
        [property: JsonProperty("event")] BroadcastEvent Event,
        [property: JsonProperty("data")] string Data
    );

    public class DrawRequest
    {
        [JsonProperty("color")]
        public string Color { get; set; }

        [JsonProperty("prompt")]
        public string Prompt { get; set; }

        [JsonProperty("initialTime")]
        public long InitialTime { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        [JsonProperty("updateTime")]
        public long UpdateTime { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        [JsonProperty("completed")]
        public bool Completed { get; set; } = false;
    }

    public record CompletedRequest(
        [property: JsonProperty("userId")] string UserId,
        [property: JsonProperty("request")] DrawRequest Request
    );

    public bool AddOrUpdate(string userId, string color, string prompt)
    {
        // Using List instead of Queue so that we can check user positions and arbitrary removal
        List<string> drawQueue = CPH.GetGlobalVar<List<string>>("drawQueue") ?? new List<string>();

        if (drawQueue.Contains(userId))
        {
            // update existing request
            DrawRequest existingRequest = CPH.GetTwtichUserVarById<DrawRequest>(userId, "drawRequest") ?? new DrawRequest();
            existingRequest.Color = color;
            existingRequest.Prompt = prompt;
            existingRequest.UpdateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            CPH.SetTwitchUserVarById(userId, existingRequest);
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

            CPH.SetTwitchUserVarById(userId, newRequest);

            drawQueue.Add(userId);
            CPH.SetGlobalVar("drawQueue", drawQueue);
        }

        BroadcastQueue();
        return true;
    }

    public bool BroadcastQueue()
    {
        List<string> drawQueue = CPH.GetGlobalVar<List<string>>("drawQueue") ?? new List<string>();

        var payload = new BroadcastEnvelope(
            Target: BroadcastTarget.All,
            Event: BroadcastEvent.DrawQueueUpdate,
            Data: drawQueue
        );

        string payloadJson = JsonConvert.SerializeObject(payload);
        CPH.WebsocketBroadcastJson(payload);

        return true;
    }

    public bool BroadcastCompletedQueue()
    {
        List<string> completedQueue = CPH.GetGlobalVar<List<string>>("completedQueue") ?? new List<string>();

        var payload = new BroadcastEnvelope(
            Target: BroadcastTarget.All,
            Event: BroadcastEvent.CompletedQueueUpdate,
            Data: completedQueue
        );

        string payloadJson = JsonConvert.SerializeObject(payload);
        CPH.WebsocketBroadcastJson(payload);

        return true;
    }

    public bool CompleteRequest(string userId)
    {
        CPH.LogInfo($"Completing request for user {userId}.");
        
        // drawQueue
        List<string> drawQueue = CPH.GetGlobalVar<List<string>>("drawQueue") ?? new List<string>();
        if (!drawQueue.Contains(userId))
        {
            CPH.LogInfo($"Completing request failed. User {userId} not found in draw queue.");
            return false;
        }        
        drawQueue.Remove(userId);
        CPH.SetGlobalVar("drawQueue", drawQueue);
        BroadcastQueue();
        
        // completed queue
        var completedRequest = new CompletedRequest(userId, CPH.GetTwtichUserVarById<DrawRequest>(userId, "drawRequest"));
        List<string> completedQueue = CPH.GetGlobalVar<List<string>>("completedQueue") ?? new List<string>();
        completedQueue.Add(userId);
        CPH.SetGlobalVar("completedQueue", completedQueue);
        BroadcastCompletedQueue();

        // mark request as completed
        DrawRequest existingRequest = CPH.GetTwtichUserVarById<DrawRequest>(userId, "drawRequest");
        if (existingRequest == null)
        {
            CPH.LogInfo($"Completing request failed. User {userId} has no existing draw request.");
            return false;
        }
        existingRequest.Completed = true;
        CPH.SetTwitchUserVarById(userId, existingRequest);

        return true;
    }

    public bool RejectRequest(string userId)
    {

        BroadcastQueue();

        return true;
    }

    public bool ClearQueue()
    {
        // clear user vars
        // clear global var

        BroadcastQueue();

        return true;
    }

    public bool ClearCompletedQueue()
    {
        // clear global var

        BroadcastCompletedQueue();

        return true;
    }

    public bool PauseQueue()
    {
        // disable new requests from command

        return true;
    }
}