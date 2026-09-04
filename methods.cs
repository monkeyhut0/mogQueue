using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public class CPHInline
{
    // required for actions
    public bool Execute() => true;

    public class DrawRequest
    {
        [JsonProperty("color")]
        public string Color { get; set; }

        [JsonProperty("prompt")]
        public string Prompt { get; set; }

        [JsonProperty("initialTime")]
        public long InitialTime { get; set; }

        [JsonProperty("updateTime")]
        public long UpdateTime { get; set; }

        [JsonProperty("completed")]
        public bool Completed { get; set; }
    }

    public bool AddOrUpdate()
    {
        
    }

    public bool BroadcastQueue()
    {
        
    }

    public bool CompleteRequest()
    {
        
    }

    public bool RejectRequest()
    {
        
    }
}