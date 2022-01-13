using System;
using System.Linq;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WildApricotAPI
{
    // Our event that we'll send back to the caller of this
    // library if they're interested in getting messages
    public class WAEventArgs : EventArgs
    {
        public enum Level
        {
            Informational,
            Warning,
            Error
        }

        public WAEventArgs(Level level, string message)
        {
            Message = message;
            MessageLevel = level;
        }

        public WAEventArgs(Level level, string message, IDictionary<string, string> eventParameters)
        {
            Message = message;
            MessageLevel = level;
            EventParameters = eventParameters;
        }

        public string Message { get; set; }
        public Level MessageLevel { get; set; }

        /// <summary>
        /// Optional properties for events to enrich 
        /// telemetry sent to listeners
        /// </summary>
        public IDictionary<string, string> EventParameters { get; set; } = new Dictionary<string, string>();
    }

    public class WAData
    {
        // We don't work with logging anything explicitly here, instead
        // we throw it back to the caller to deal with it
        public event EventHandler<WAEventArgs> RaiseCustomEvent;

        private static readonly HttpClient client = new HttpClient();
     
        // The token is authorized-application-specific to your Wild Apricot account
        private string apiToken = "";

        private string oauthToken;
        private string accountId;
       
        protected virtual void OnRaiseCustomEvent(WAEventArgs e)
        {
            EventHandler<WAEventArgs> raiseEvent = RaiseCustomEvent;

            // Event will be null if there are no subscribers
            if (raiseEvent != null)
            {                           
                raiseEvent(this, e);
            }
        }

        private void Log(WAEventArgs.Level level, string message)
        {
            OnRaiseCustomEvent(new WAEventArgs(level, message));
        }

        private void Log(WAEventArgs.Level level, string message, IDictionary<string,string> eventParameters)
        {
            OnRaiseCustomEvent(new WAEventArgs(level, message, eventParameters));
        }

        private static string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes);
        }

        private static string Base64Decode(string base64EncodedData)
        {
            var base64EncodedBytes = System.Convert.FromBase64String(base64EncodedData);
            return System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
        }

        private async Task<JObject> SendRequest(HttpRequestMessage message)
        {
            message.Headers.Add("User-Agent", "WA2AD");
            message.Headers.Add("Accept", "application/json");

            using (var response = await client.SendAsync(message))
            {
                System.Threading.Thread.Sleep(4000);
                var responseString = await response.Content.ReadAsStringAsync();
                JObject json = JObject.Parse(responseString);

                return json;
            }
        }

        private bool PutWAData(string putURL, string jsonPayload)
        {
            Log(WAEventArgs.Level.Informational, "Entering PutWAData");
            try
            {
                // we are not logging the payload because it might
                // contain a secret in it
                Log(WAEventArgs.Level.Informational, String.Format("PutWAData URL is '{0}'", putURL));

                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, putURL);
                request.Headers.Add("Authorization", "Bearer " + this.oauthToken);
                request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                Log(WAEventArgs.Level.Informational, "Sending request");

                JObject response = SendRequest(request).Result;

                Log(WAEventArgs.Level.Informational, "Request sent");
                //Console.WriteLine(response);

                //TODO: This is not accomplishing anything
                return true;
            }
            finally
            {
                Log(WAEventArgs.Level.Informational, "Exiting PutWAData");
            }
        }

        private JObject GetWAData(string waResponseURL)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, waResponseURL);
            request.Headers.Add("Authorization", "Bearer " + this.oauthToken);
          
            // Actually get the data from Wild Apricot
            JObject memberData = SendRequest(request).Result;
            // Successful?
            if (memberData != null && memberData["State"].ToString() != "Processing")
            {

                // Yep, so return it for whatever usage
                Log(WAEventArgs.Level.Informational, "We got data!");

                return memberData;
            }
           
            // If we're here we weren't able to get the data after five tries. WTF?
            Log(WAEventArgs.Level.Error, "WTF? Why couldn't we get the member data?");
            return null;
        }

        private void GetOauthToken()
        {
            try
            {
                Log(WAEventArgs.Level.Informational, "Entering GetOAuthToken");

                string authString = "Basic " + Base64Encode("APIKEY:" + apiToken);

                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://oauth.wildapricot.org/auth/token");
                request.Content = new StringContent("grant_type=client_credentials&scope=auto",
                                        Encoding.UTF8,
                                        "application/x-www-form-urlencoded");

                request.Headers.Add("Authorization", authString);

                JObject dataObj = SendRequest(request).Result;

                this.oauthToken = dataObj.GetValue("access_token").ToString();
                Log(WAEventArgs.Level.Informational, "Received access_token value");

                this.accountId = (string)dataObj.SelectToken("Permissions[0].AccountId");
                Log(WAEventArgs.Level.Informational, String.Format("Found AccountID {0} in reply", this.accountId));
            }
            finally
            {
                Log(WAEventArgs.Level.Informational, "Exiting GetOAuthToken");
            }
        }

        public JObject GetAllMemberData()
        {
            string GetMemberListUrl()
            {
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "https://api.wildapricot.org/v2/Accounts/" + this.accountId + "/Contacts");
                request.Headers.Add("Authorization", "Bearer " + this.oauthToken);

                JObject dataObj = SendRequest(request).Result;

                // Now we need to get the real url
                return dataObj.GetValue("ResultUrl").ToString();
            }

            Log(WAEventArgs.Level.Informational, "Starting to get all the member data from Wild Apricot...");

            GetOauthToken();

            string resultsURL = GetMemberListUrl();
            Log(WAEventArgs.Level.Informational, "Our results URL is " + resultsURL);

            System.Threading.Thread.Sleep(5000);
            JObject memberData = GetWAData(resultsURL);

            Log(WAEventArgs.Level.Informational, "Finished getting the data from Wild Apricot...");
             
            if (memberData != null && memberData.HasValues)
            {
                Log(WAEventArgs.Level.Informational, "...and we have data to work with.");
                
                return memberData;
            }
            
            Log(WAEventArgs.Level.Warning, "...hmm, we don't have any data to work with!");
            
            return null;
        }

        public JObject GetMemberForWAID(string memberID)
        {
            Dictionary<string, string> traceParameters = new Dictionary<string, string>() { { "MemberID", memberID } };

            Log(WAEventArgs.Level.Informational, "Starting to get member data from Wild Apricot for id " + memberID + "...", traceParameters);

            GetOauthToken();

            string GetMemberUrl()
            {
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "https://api.wildapricot.org/v2/Accounts/" + this.accountId + "/Contacts?$filter='Id'%20eq%20'" + memberID + "'");
                request.Headers.Add("Authorization", "Bearer " + this.oauthToken);

                JObject dataObj = SendRequest(request).Result;

                // Now we need to get the real url
                return dataObj.GetValue("ResultUrl").ToString();
            }

            string resultsURL = GetMemberUrl();

            System.Threading.Thread.Sleep(5000);
            JObject memberData = GetWAData(resultsURL);

            Log(WAEventArgs.Level.Informational, "Finished getting the data from Wild Apricot...", traceParameters);
            
            if (memberData.HasValues)
            {
                Log(WAEventArgs.Level.Informational, "...and we have data to work with.", traceParameters);
                return memberData;
            }
                
            Log(WAEventArgs.Level.Error, "...hmm, we don't have any data to work with!", traceParameters);                
            return null;
        }

        public bool SaveMember(Member member)
        {
            Dictionary<string, string> traceParameters = new Dictionary<string, string>() { { "MemberID", member.Id.ToString() } };

            Log(WAEventArgs.Level.Informational, "Entering SaveMember", traceParameters);

            try
            {
                // Now save it back to Wild Apricot
                string memberJson = JsonConvert.SerializeObject(member);

                // And now we need to set up our put request to update WA with 
                // the new member data
                string putURL = "https://api.wildapricot.org/v2/Accounts/" + this.accountId + "/Contacts/" + member.Id;

                Log(WAEventArgs.Level.Informational, "Sending data to WA", traceParameters);
                // And save it to Wild Apricot...
                return PutWAData(putURL, memberJson);
            }
            finally
            {
                Log(WAEventArgs.Level.Informational, "Exiting SaveMember", traceParameters);
            }
        }

        public bool ResetPassword(string waID, string newPassword)
        {
            Dictionary<string, string> traceParameters = new Dictionary<string, string>() { { "MemberID", waID } };

            Log(WAEventArgs.Level.Informational, "Entering ResetPassword", traceParameters);

            try
            {
                Log(WAEventArgs.Level.Informational, "Calling GetMember", traceParameters);
                // Get and transform the data into our member object
                Member memberData = (Member)GetMemberForWAID(waID).GetValue("Contacts")[0].ToObject<Member>();
                memberData.Password = newPassword;

                Log(WAEventArgs.Level.Informational, "Calling SaveMember", traceParameters);

                bool result = SaveMember(memberData);

                Log(WAEventArgs.Level.Informational, String.Format("SaveMember returned {0}", result), traceParameters);

                return result;
            }
            finally
            {
                Log(WAEventArgs.Level.Informational, "Exiting ResetPassword", traceParameters);
            }
        }

        public WAData(string apiToken)
        {
            // Gotta have a token to do anything useful
            this.apiToken = apiToken;          
        }       
    }
}
