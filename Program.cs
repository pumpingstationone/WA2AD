using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using System;
using System.Diagnostics;
using WildApricotAPI;

namespace WA2AD
{
    class Program
    {
        EventLogTraceListener wa2adTraceListener = new EventLogTraceListener("WA2AD");

        private ADActions adActions = null;

        private TelemetryClient aiTelemetryClient = new TelemetryClient(TelemetryConfiguration.Active);

        private void HandleLogEvent(object sender, WAEventArgs e) { Log.Write((Log.Level)e.MessageLevel, e.Message); }

        public Program()
        {
            try
            {
                Trace.Listeners.Add(this.wa2adTraceListener);

                Log.Write(Log.Level.Informational, "Beginning job...");

                // Here we're instantiating the object that handled all the Active Directory
                // work (it also calls the B2C stuff as well)
                this.adActions = new ADActions();

                // First we need to get the api token to pass to the WA DLL
                // Get our token from the ini file
                var MyIni = new IniFile();
                string waAPIToken = MyIni.Read("WAToken").Trim();
                if(waAPIToken.Length == 0)
                {
                    Log.Write(Log.Level.Error, "Whoops, can't get the WA oauth token! Check the ini file is in the same dir as the executable and set properly!");
                    return;
                }

                // Sweet, we got a token, so we can use that. 
                WAData waData = new WAData(waAPIToken);
                // And we'll get the events from the DLL here for logging
                waData.RaiseCustomEvent += HandleLogEvent;

                try
                {
                    var memberData = waData.GetAllMemberData();

                    foreach(var obj in memberData.GetValue("Contacts"))
                    {
                        Member member = (Member)obj.ToObject<Member>();

                        // Our guinea pig for everything...
                        //if (member.FirstName != "Testy" || member.LastName != "McTestface")                  
                        //    continue;

                        try
                        {
                            Log.Write(Log.Level.Informational, "Going to work with " + member.FirstName + " " + member.LastName);
                            adActions.HandleMember(member);
                        }
                        catch(Exception me)
                        {
                            Log.Write(Log.Level.Error, string.Format("Hmm, An error occurred when working with {0}: '{1}'", member.FirstName, me));
                            aiTelemetryClient.TrackException(me);
                        }
                    }
                }
                catch(Exception e)
                {
                    Log.Write(Log.Level.Error, string.Format("An error occurred: '{0}'", e));
                    aiTelemetryClient.TrackException(e);
                }
            }
            finally
            {
                Log.Write(Log.Level.Informational, "...Finished job");
            }
        }

        static void Main(string[] args)
        {
            Log.Write(Log.Level.Informational, "Starting...");

            new Program();
        }
    }
}