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
        Update,
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
            Event: BroadcastEvent.Update,
            Data: drawQueue
        );

        string payloadJson = JsonConvert.SerializeObject(payload);
        CPH.WebsocketBroadcastJson(payload);

        return true;
    }

    public bool CompleteRequest(string userId)
    {
        

        BroadcastQueue();
    }

    public bool RejectRequest(string userId)
    {
        

        BroadcastQueue();
    }

    public bool ClearQueue()
    {
        // clear user vars
        // clear global var

        BroadcastQueue();
    }

    public bool PauseQueue()
    {
        // disable new requests from command
    }
}