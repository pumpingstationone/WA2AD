using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;
using WildApricotAPI;
using SyncDB;


namespace WA2AD
{
    class Program
    {
        // Our job types, which we can use as contants right now
        public const string FULL_SYNC = "full";
        public const string LATEST_SYNC = "latest";

        // Our sync database to keep track of when we last synced, and who
        private MemberDB memberDB = new MemberDB();

        EventLogTraceListener wa2adTraceListener = new EventLogTraceListener("WA2AD");

        private ADActions adActions = null;

        private TelemetryClient aiTelemetryClient = new TelemetryClient(TelemetryConfiguration.Active);

        private void HandleLogEvent(object sender, WAEventArgs e) { Log.Write((Log.Level)e.MessageLevel, e.Message); }

        // This method is called via a Task object in Program() below
        private void processMember(Member member)
        {
            try
            {
                Log.Write(Log.Level.Informational, "(mid:" + member.Id + ") " + "\t\t*** Going to work with " + member.FirstName + " " + member.LastName + " - member ID: " + member.Id + " ***");
                adActions.HandleMember(member);
            }
            catch (Exception me)
            {
                Log.Write(Log.Level.Error, "(mid:" + member.Id + ") " + string.Format("Hmm, An error occurred when working with {0}: '{1}'", member.FirstName, me));
                aiTelemetryClient.TrackException(me);
            }
        }

        public Program(string jobType)
        {
            try
            {
                Trace.Listeners.Add(this.wa2adTraceListener);

                Log.Write(Log.Level.Informational, "Beginning job...this is a " + jobType + " job");

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
                    Newtonsoft.Json.Linq.JObject memberData = null;

                    if (jobType == FULL_SYNC)
                    {
                        // Get all the data from WA (this is a full sync)
                        memberData = waData.RetrieveAllMemberData();
                    }
                    else if (jobType == LATEST_SYNC)
                    {
                        // Get the latest data from WA (this is a delta sync)
                        // First we need to get the last time we synced from
                        // our database
                        string lastSync = memberDB.GetLastSyncTime();
                        Log.Write(Log.Level.Informational, "Last sync was: " + lastSync);
                        memberData = waData.GetLatestMemberData(lastSync);
                    }
                   
                    if (memberData == null)
                    {
                        Log.Write(Log.Level.Error, "No data to work with so not continuing");
                        return;
                    }

                    List<Task> TaskList = new List<Task>();
                    int memberCount = 0;
                    foreach (var obj in memberData.GetValue("Contacts"))
                    {
                        Member member = (Member)obj.ToObject<Member>();

                        // Our guinea pig for everything...
                        //if (member.FirstName != "Ed" || member.LastName != "Bennett")                  
                        //    continue;

                        processMember(member);

                        // And update the sync database with the last time we synced this person
                        memberDB.UpdateMember(member.Id, member.FirstName, member.LastName);

                        /*
                        // Now we're going to throw each member over to a task to process
                        // asynchronously
                        var processMemberTask = new Task(() => processMember(member));
                        processMemberTask.Start();
                        TaskList.Add(processMemberTask);
                        */
                        ++memberCount;
                    }

                    Log.Write(Log.Level.Informational, String.Format("Processed {0} people", memberCount));

                    /*
                    // Now we're gonna wait for everyone to finish processing...
                    Task.WaitAll(TaskList.ToArray());
                    Log.Write(Log.Level.Informational, "Our member task list is now empty");
                    */

                    // And update our sync database with the last time we synced. If there
                    // was any kind of exception that didn't allow the job to succeed, then
                    // this won't be updated to the latest time, so cool, we can try again
                    // later from that checkpoint
                    memberDB.UpdateSyncTime();
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

            // And make sure we close the database
            memberDB.Close();
        }

        static void Main(string[] args)
        {
            Log.Write(Log.Level.Informational, "Starting...");

            string jobType = "";

            // Now we need to check if there's a switch to tell
            // us what to do, either --full or --latest
            if (args.Length == 0)
            {
                Log.Write(Log.Level.Error, "No arguments passed, please pass either --full or --latest");
                return;
            }

            if (args[0] == "--full")
            {
                Log.Write(Log.Level.Informational, "Running full sync");
                jobType = FULL_SYNC;
            }
            else if (args[0] == "--latest")
            {
                Log.Write(Log.Level.Informational, "Running latest sync");
                jobType = LATEST_SYNC;
            }
            else
            {
                Log.Write(Log.Level.Error, "Invalid argument passed, please pass either --full or --latest");
                return;
            }

            new Program(jobType);
        }
    }
}