using System;
using System.Diagnostics;
using WildApricotAPI;
//
// For this program to write to the Event Log properly, run
// this command using an elevated power shell:
//
//       New-EventLog -LogName Application -Source "WA2AD"
//

namespace WA2AD
{
    class Program
    {        
        private ADActions adActions = new ADActions();
      
        public Program()
        {           
            var appLog = new EventLog("Application");
            appLog.Source = "WA2AD";
            appLog.WriteEntry("Beginning job...");

            // First we need to get the api token to pass to the WA DLL
            // Get our token from the ini file
            var MyIni = new IniFile();
            string waAPIToken = MyIni.Read("WAToken").Trim();
            if (waAPIToken.Length == 0)
            {
                appLog.WriteEntry("Whoops, can't get the WA oauth token! Check the ini file is in the same dir as the executable and set properly!", EventLogEntryType.Error);
                Console.WriteLine("Whoops, can't get the WA oauth token! Check the ini file is in the same dir as the executable and set properly!");
                return;
            }

            // Sweet, we got a token, so we can use that. We also
            // pass the application source name so the DLL can use the
            // same source name
            WAData waData = new WAData(waAPIToken, appLog.Source);

            try
            {
                var memberData = waData.GetAllMemberData();

                foreach (var obj in memberData.GetValue("Contacts"))
                {
                    Member member = (Member)obj.ToObject<Member>();

                    // Our guinea pig for everything...
                    //if (member.FirstName != "Testy" || member.LastName != "McTestface")
                    if (member.FirstName != "Ron" || member.LastName != "Olson")
                            continue;

                    try
                    {
                        Console.WriteLine("Going to work with " + member.FirstName + " " + member.LastName);
                        adActions.HandleMember(member);
                    }
                    catch (Exception me)
                    {                        
                        appLog.WriteEntry(string.Format("Hmm, An error occurred when working with {0}: '{1}'", member.FirstName, me), EventLogEntryType.Error);
                        Console.WriteLine("Hmm, An error occurred when working with {0}: '{1}'", member.FirstName, me);
                    }
                }
            }
            catch (Exception e)
            {
                appLog.WriteEntry(string.Format("An error occurred: '{0}'", e), EventLogEntryType.Error);
                Console.WriteLine("An error occurred: '{0}'", e);
            }

            appLog.WriteEntry("...Finished job");
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Starting...");

            new Program();
        }
    }
}
