using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace WildApricotAPI
{
    public class WAData
    {
        private static readonly HttpClient client = new HttpClient();

        // For writing to the system event log (See program.cs for how to make sure this works)
        private EventLog appLog;

        // The token is authorized-application-specific to your
        // Wild Apricot account
        private string apiToken = "";

        private string oauthToken;
        private string accountId;

        private string contactUrl;

        private JObject memberData;

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
                var responseString = await response.Content.ReadAsStringAsync();
                JObject json = JObject.Parse(responseString);

                return json;
            }
        }

        private JObject GetMemberList()
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, this.contactUrl);
            request.Headers.Add("Authorization", "Bearer " + this.oauthToken);

            // We try five times to get the data
            for (int x = 0; x < 5; ++x)
            {
                // Actually get the data from Wild Apricot
                JObject memberData = SendRequest(request).Result;
                // Successful?
                if (memberData != null)
                {
                    // Yep, so return it for whatever usage
                    return memberData;
                }

                // Crap, not successful, so let's sleep for a second before
                // trying again
                appLog.WriteEntry("Hmm, couldn't get the data from WA, so gonna try again");
                Console.WriteLine("Hmm, couldn't get the data from WA, so gonna try again");
                System.Threading.Thread.Sleep(1000);
            }

            // If we're here we weren't able to get the data after five tries. WTF?
            return null;
        }

        private void GetMemberListUrl()
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "https://api.wildapricot.org/v2/Accounts/" + accountId + "/Contacts");
            request.Headers.Add("Authorization", "Bearer " + this.oauthToken);

            JObject dataObj = SendRequest(request).Result;

            // Now we need to get the real url
            this.contactUrl = dataObj.GetValue("ResultUrl").ToString();
        }

        private void GetOauthToken()
        {
            string authString = "Basic " + Base64Encode("APIKEY:" + apiToken);

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://oauth.wildapricot.org/auth/token");
            request.Content = new StringContent("grant_type=client_credentials&scope=auto",
                                    Encoding.UTF8,
                                    "application/x-www-form-urlencoded");

            request.Headers.Add("Authorization", authString);

            JObject dataObj = SendRequest(request).Result;

            this.oauthToken = dataObj.GetValue("access_token").ToString();
            this.accountId = (string)dataObj.SelectToken("Permissions[0].AccountId");
        }

        public WAData(string apiToken, string logSource)
        {
            // Gotta have a token to do anything useful
            this.apiToken = apiToken;

            this.appLog = new EventLog("Application");
            appLog.Source = logSource;

            appLog.WriteEntry("Starting to get the data from Wild Apricot...");
            Console.WriteLine("Starting to get the data from Wild Apricot...");
            GetOauthToken();
            GetMemberListUrl();

            System.Threading.Thread.Sleep(5000);
            this.memberData = GetMemberList();

            appLog.WriteEntry("Finished getting the data from Wild Apricot...");
            Console.WriteLine("Finished getting the data from Wild Apricot...");
            if (this.memberData.HasValues)
            {
                appLog.WriteEntry("...and we have data to work with.");
                Console.WriteLine("...and we have data to work with.");
            }
            else
            {
                appLog.WriteEntry("...hmm, we don't have any data to work with!", EventLogEntryType.Error);
                Console.WriteLine("...hmm, we don't have any data to work with!");
            }
        }

        public ref JObject GetMemberData()
        {
            return ref this.memberData;
        }
    }
}
